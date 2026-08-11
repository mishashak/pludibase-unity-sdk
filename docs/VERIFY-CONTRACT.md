# 백엔드 검증 계약 (구현 정본)

v1.1.0 (2026-08-11). SDK 0.1.4 / 백엔드 `pludibase` 브랜치 기준.

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

> ⚠️ **알려진 구멍.** 1번 분기가 `if (integration)` 조건부입니다(`player-alias.ts:159`). 그 게임에 해당 Integration이 없으면 **검증을 건너뛰고 넘어온 문자열이 그대로 alias 식별자가 됩니다.** 새 게임을 붙일 때 Integration 등록을 빠뜨리면 토큰 검증 없이 통과합니다. 가드 추가 예정(§5).

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
  "store": "google_play",
  "status": "verified"
}
```

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

> ⚠️ **금액은 아직 서버가 확정하지 않습니다.** `amount` / `currency` 는 **클라이언트가 보낸 값을 그대로 저장하고 그대로 돌려주는 값**입니다. 서버가 확정하는 것은 **구매의 유효성**(위조 아님, 재사용 아님)까지입니다. 위조된 금액을 지표에 넣을 수는 있으므로, 정산 정본으로 쓰지 마세요. 하드닝 예정(§5).

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

> ⚠️ **두 플레이어가 같은 service의 별칭을 가지면 400으로 거부됩니다.** 익명 플레이를 `username` 별칭으로 잡는 구조라면, 재설치로 새 게스트가 생겼을 때 소셜 플레이어에 이미 `username` 별칭이 있어 병합이 막힙니다. 즉 **기기당 최초 1회만 성공**합니다. 재설치 직후 게스트는 빈 플레이어라 잃을 것이 없으므로, 이 400은 정상으로 처리하고 넘어가면 됩니다.

> ⚠️ Unity의 `MergeOptions.postMergeIdentityService` 에 `"google"` / `"apple"` 을 넣지 마세요. 저장된 `sub` 값으로 다시 identify를 걸어버리는데, 백엔드는 그 값을 ID 토큰으로 보고 검증하려 해서 실패합니다.

### 재시도 판정
재시도 금지(전부 상태 문제): `400 Cannot merge a player into themselves` / `404 Player {id} does not exist` / `403 This merge must be initiated by player {id}` / `400` 제한 별칭 보유 / `400` 같은 service 중복.
재시도 가능: 네트워크 오류, 5xx.
`playerId2` 가 404면 이미 병합된 것이니 **성공으로 처리**하면 됩니다.

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
| amount, currency | 클라이언트 보고값 (서버 확정 아님, §2 경고 참조) |
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
- 금액은 아직 이 원칙의 예외다. §2 경고 참조.

---

## 5. 남은 일

P1(소셜 로그인 + 3개 스토어 결제 검증)은 끝났습니다. 남은 것은 다음과 같습니다.

1. **금액 서버 확정.** Google Play 카탈로그 가격 대조로 `amount` / `currency` 를 서버가 확정하도록 하드닝.
2. **Integration 누락 가드.** `google-sign-in` / `apple-sign-in` Integration이 없을 때 조용히 통과시키지 않고 거부하도록(§1 경고).
3. **환불/취소 알림 수신** 엔드포인트(§2).
4. **스토어 실검증 E2E.** 게임별 자격증명이 있어야 합니다. 코드는 준비돼 있고, Play 서비스 계정 키를 받는 대로 실제 게임에서 확인합니다.
