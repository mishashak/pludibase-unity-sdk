# 백엔드 검증 계약 (구현 정본)

v1.2.0 (2026-08-14). SDK 0.1.5 / 백엔드 `pludibase` 브랜치 기준.

> **이전 판(v1.0.0) 정정.** 이 문서는 원래 P1 착수 **전에 쓴 설계 초안**이었습니다. 그래서 §5가 실제 구현과 어긋나 있었고(존재하지 않는 `/v1/players/auth/google`, 실재하지 않는 `sessionToken` 배선), §2의 금액 처리 설명도 실제와 달랐습니다. 이 판은 **구현된 코드를 읽고 쓴 것**입니다.

이 문서는 SDK와 pludibase 백엔드 사이의 계약입니다. 클라이언트가 준 값은 신뢰하지 않으며, 구매 **유효성**의 정본은 백엔드 검증 결과입니다.

공통:
- 모든 요청 헤더에 `Authorization: Bearer <access key>` (게임 전용 키).
- 플레이어 컨텍스트가 필요한 요청은 `x-talo-alias` 헤더도 필요합니다. Talo SDK가 자동으로 붙입니다.
- 본문은 JSON.
- 스토어/제공자 자격증명(구글 서비스 계정, 애플 키, OAuth client id, Steam publisher 키)은 **게임별 Integration**으로 백엔드에 저장합니다. 시크릿은 커밋하지 않습니다.

---

## 1. 소셜 로그인 검증 (구현 완료)

**별도 로그인 엔드포인트가 없습니다.** Talo가 이미 Steam/Google Play Games/Game Center를 처리하는 `GET /v1/players/identify` + `resolveIdentifier` + `Integration` 패턴에 google/apple을 얹었습니다.

### 호출 (SDK/게임)
Talo SDK의 identify를 그대로 씁니다. SDK는 `PludibaseAuth.SignInWithGoogle(idToken)`으로 감쌌습니다(내부는 `Talo.Players.Identify("google", idToken)`).
```
GET /v1/players/identify?service=google&identifier=<Google ID 토큰(JWT)>
GET /v1/players/identify?service=apple&identifier=<Apple identity 토큰(JWT)>
```

### 백엔드 처리
1. `resolveIdentifier`가 게임의 `google-sign-in` / `apple-sign-in` Integration을 찾아 토큰을 검증합니다. 구글/애플 공개키(JWKS, 1시간 캐시)로 RS256 서명 확인 + `aud`(게임 설정 `clientId`) + `iss` + 만료 확인.
2. 토큰의 `sub`를 식별자로 `player_alias`(service=`google`/`apple`) 조회/생성.
3. 세션은 기존 identify 흐름이 그대로 처리합니다. 응답의 `socketToken`을 Talo SDK가 받아 세션을 엽니다. **게스트 identify와 완전히 같은 경로라 SDK에 추가로 배선할 것이 없습니다.**

> **Integration이 없으면 거부합니다 (0.1.5부터).** 예전에는 이 분기가 `if (integration)` 조건부라, 해당 Integration이 없는 게임에서는 검증을 건너뛰고 넘어온 문자열이 그대로 alias 식별자가 됐습니다. 남의 `sub` 값을 적어 보내면 그 사람으로 로그인되는 구멍이었습니다. 지금은 `400 Google Sign-In is not configured for this game`(애플도 동일)으로 막힙니다. 새 게임을 붙일 때 **Integration 등록이 로그인의 전제**입니다.

### 설정 (게임별)
- Integration 타입 `google-sign-in` / `apple-sign-in`, config = `{ clientId }` (검증용 `aud`, 비밀 아님이라 미암호화).
- alias service `google` / `apple`.

### 게스트에서 소셜로 잇기
전용 link 엔드포인트는 없습니다. **`POST /v1/players/merge`** 로 익명 플레이어를 소셜 플레이어에 흡수시킵니다(Unity: `Talo.Players.Merge`). 상세는 §2-B.

---

## 2. 인앱결제 검증 (구현 완료, 3개 스토어)

### `POST /v1/purchases/verify`

필요 스코프: **`write:events`** 하나. `x-talo-alias` 헤더 필요(플레이어 세션).

```json
{
  "store": "google_play",
  "productId": "gem_pouch",
  "purchaseToken": "<스토어별 토큰>",
  "amount": 4900,
  "currency": "KRW"
}
```

`store` 와 필요한 Integration, `purchaseToken` 에 넣을 값:

| store | Integration | purchaseToken |
|---|---|---|
| `google_play` | `google-play-billing` (서비스 계정 키) | Play purchase token |
| `app_store` | `app-store-billing` (.p8 / issuerId / keyId / bundleId) | StoreKit2 transactionId |
| `steam` | `steamworks` (기존 통합 재사용) | Steam `transid` |

### 백엔드 처리
1. `store`로 Integration을 찾아 스토어에 직접 검증합니다.
2. `(store, transactionId)` 유니크로 **중복 방지**. 이미 있으면 재지급하지 않습니다.
3. **최초 검증일 때만** `purchase` 이벤트를 발행합니다(`amount` / `currency` / `product` / `store` / `order_id`=transactionId). 대시보드 매출과 LTV에 자동 반영됩니다.

> **멱등합니다.** 같은 토큰으로 다시 호출해도 `valid: true` 가 돌아오지만 이벤트는 다시 발행되지 않습니다. 매출이 두 번 잡히지 않습니다.

### 응답 (검증 성공, 200)
```json
{
  "valid": true,
  "transactionId": "GPA.1234-5678",
  "product": "gem_pouch",
  "amount": 4900,
  "currency": "KRW",
  "amountSource": "store",
  "store": "google_play",
  "status": "verified"
}
```

`amountSource` 는 이 금액이 어디서 왔는지입니다. `store` 면 스토어가 확정해 준 값, `client` 면 보내신 값이 그대로 남은 것입니다(§2 금액 항목).

### 응답 (검증 실패, 200)
```json
{
  "valid": false,
  "transactionId": "...",
  "product": "gem_pouch",
  "store": "google_play"
}
```
`valid: false` 면 게임은 지급하지 않습니다. 이때 `amount` / `currency` / `status` 는 오지 않습니다.

### 금액은 스토어 값이 정본입니다 (0.1.5부터)

보내주신 `amount` / `currency` 는 스토어가 금액을 알려주는 경우 **서버가 스토어 값으로 덮어씁니다.** 응답과 대시보드 매출에 들어가는 것도 그 값입니다.

| store | 금액 출처 | `amountSource` |
|---|---|---|
| `google_play` | Play 콘솔 상품 카탈로그 가격(구매 지역가 우선, 수량 반영) | `store` |
| `app_store` | 애플이 서명한 트랜잭션의 `price` / `currency` | `store` |
| `steam` | 아직 클라이언트 보고값 | `client` |

Steam은 QueryTxn이 주는 금액의 최소 단위 규칙을 실거래로 확인하기 전까지 열지 않았습니다. 잘못 환산하면 매출이 100배로 어긋나기 때문입니다.

> ⚠️ 카탈로그 조회에 실패했거나(Play API 오류) 스토어가 금액을 주지 않은 옛 트랜잭션이면 보내신 값이 그대로 남고 `amountSource` 가 `client` 로 돌아옵니다. 이 경우에는 여전히 정산 정본으로 쓰지 마세요. 클라이언트 값과 스토어 값이 어긋나면 서버는 스토어 값으로 기록하고 백엔드 로그(`[purchase] 금액 불일치`)에 양쪽을 남깁니다.

### 에러
| 코드 | errorCode | 뜻 |
|---|---|---|
| 400 | `UNSUPPORTED_STORE` | `store` 값이 위 셋이 아님 |
| 400 | `BILLING_NOT_CONFIGURED` | 그 게임에 해당 Integration이 없음 |

### 서버 알림 (환불/취소, 미구현)
Google Real-time Developer Notifications, Apple App Store Server Notifications V2 수신 엔드포인트로 `GamePurchase.status` 를 refunded/revoked 로 갱신할 계획입니다. 아직 없습니다.

---

## 2-B. 플레이어 병합 `POST /v1/players/merge`

필요 스코프: `read:players`, `write:players`. `x-talo-alias` 헤더 필요.

```json
{ "playerId1": "<살아남는 쪽>", "playerId2": "<흡수되는 쪽>" }
```

- `playerId2` 는 **삭제됩니다.** 별칭, 세이브, 스탯, props가 `playerId1` 로 넘어갑니다.
- 스탯은 **합산**됩니다(stat의 min/max로 클램프).
- props가 양쪽에 같은 key로 있으면 **`playerId2`(흡수되는 쪽) 값이 남습니다.** 직관과 반대이니 주의.
- 세션은 유지됩니다. 별칭이 삭제되지 않고 옮겨지기 때문에 재identify가 필요 없습니다.

> **두 플레이어가 같은 service의 별칭을 가지면 400으로 거부됩니다. 단 `username` 은 예외입니다 (0.1.5부터).** `username` 별칭은 기기 GUID를 담는 자리라 한 플레이어가 여럿 갖는 것이 정상입니다. 그래서 재설치나 기기추가로 생긴 게스트도 그대로 병합됩니다(예전에는 여기서 400이 나 기기당 최초 1회만 성공했고, 재설치마다 고아 플레이어가 남았습니다). 병합 후에는 두 기기의 `username` 별칭이 모두 남아 어느 기기로 들어와도 같은 플레이어가 됩니다. 나머지 service(`custom`, `email` 등)는 그대로 400입니다.

> ⚠️ Unity의 `MergeOptions.postMergeIdentityService` 에 `"google"` / `"apple"` 을 넣지 마세요. 저장된 `sub` 값으로 다시 identify를 걸어버리는데, 백엔드는 그 값을 ID 토큰으로 보고 검증하려 해서 실패합니다.

### 재시도 판정
재시도 금지(전부 상태 문제): `400 Cannot merge a player into themselves` / `404 Player {id} does not exist` / `403 This merge must be initiated by player {id}` / `400` 제한 별칭 보유 / `400` 같은 service 중복(`username` 제외).
재시도 가능: 네트워크 오류, 5xx.
`playerId2` 가 404면 이미 병합된 것이니 **성공으로 처리**하면 됩니다.

---

## 2-C. 플레이어 속성 저장 `PATCH /v1/players/:id`

필요 스코프: `write:players`. `x-talo-alias` 헤더는 필요 없습니다.

```json
{ "props": [{ "key": "VID", "value": "abc123" }] }
```

**서버는 `props` 외의 필드를 읽지 않습니다.** 스키마가 `props` 하나뿐이라 나머지는 핸들러에 닿기 전에 버려집니다. `value` 를 `null` 로 주면 그 prop이 삭제됩니다.

> Talo SDK의 `Talo.Players.Update()` 는 `JsonUtility.ToJson(Talo.CurrentPlayer)` 로 Player 전체를 직렬화해 보냅니다. `PlayerAlias.player` 가 자기 자신을 다시 물어 `presence` 줄기가 깊이 제한까지 반복되고, 실측에서 본문의 93%가 그것이었습니다(props+id 320자 대 presence 4,440자). 서버가 어차피 버리는 값이라 SDK에 최소 페이로드 경로를 뒀습니다.
>
> ```csharp
> // await Talo.Players.Update();
> await PludibasePlayers.SetProps(Talo.CurrentPlayer.id, ("VID", vid));
> ```
>
> `SetProp` 으로 로컬 값을 세팅하는 부분은 그대로 두고 마지막 네트워크 호출만 바꾸면 됩니다. 이 호출은 서버만 갱신합니다.

---

## 3. 데이터 모델 `GamePurchase` (구현 완료)

| 필드 | 설명 |
|---|---|
| id | PK |
| game_id | 게임 |
| player_alias_id | 구매자 별칭 |
| store | google_play / app_store / steam |
| product_id | 상품 |
| transaction_id | 스토어 트랜잭션 ID (store와 함께 **유니크** = dedup 키) |
| amount, currency | 스토어 확정값. 스토어가 금액을 주지 않으면 클라이언트 보고값 (§2 금액 항목) |
| status | verified / consumed / refunded / revoked |
| acknowledged | Google acknowledge 여부 |
| raw | 감사용 원본 자리. **현재는 기록하지 않습니다.** |
| created_at, updated_at | |

---

## 4. 보안 원칙

- 구매 **유효성**은 서버검증으로만 판단한다. 클라가 "샀다"고 한 것을 믿지 않는다.
- 영수증/토큰 **재사용 차단**(`store` + `transaction_id` 유니크).
- 스토어 자격증명(서비스 계정 키, 애플 private key, Steam publisher 키)은 시크릿 관리, 커밋 금지.
- 소셜 토큰은 `aud` / `iss` / 만료를 모두 확인한다(다른 앱 토큰 재사용 차단).
- 금액도 스토어가 주는 경우 서버가 확정한다(Google Play 카탈로그, App Store 트랜잭션). Steam만 아직 예외다. §2 금액 항목 참조.

---

## 5. 남은 일

P1(소셜 로그인 + 3개 스토어 결제 검증)은 끝났습니다. 남은 것은 다음과 같습니다.

1. **Steam 금액 서버 확정.** QueryTxn이 주는 금액의 최소 단위 규칙을 실거래로 확인한 뒤 엽니다(§2 금액 항목).
2. **환불/취소 알림 수신** 엔드포인트(§2).
3. **스토어 실검증 E2E.** 게임별 자격증명이 있어야 합니다. 코드는 준비돼 있고, Play 서비스 계정 키를 받는 대로 실제 게임에서 확인합니다.

끝난 것: 금액 서버 확정(Google Play, App Store), `google-sign-in` / `apple-sign-in` Integration 누락 가드. 둘 다 0.1.5입니다.
