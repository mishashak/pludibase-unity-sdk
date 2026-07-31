# 백엔드 검증 계약 (P0 설계 정본)

이 문서는 SDK와 pludibase 백엔드 사이의 계약입니다. **SDK(클라)는 이 계약대로 호출하고, 백엔드(P1)는 이 계약대로 구현**합니다. 클라이언트가 준 값은 신뢰하지 않으며, 금액/통화/유효성의 정본은 백엔드 검증 결과입니다.

공통:
- 모든 요청 헤더에 `Authorization: Bearer <access key>` (게임 전용 키).
- 본문은 JSON.
- 소셜/구매 검증에 쓰는 스토어/제공자 자격증명(구글 서비스 계정, 애플 키, OAuth client id, bundle id)은 **게임별 설정**으로 백엔드에 저장합니다. Talo의 integration 설정 패턴을 따르며, 시크릿은 커밋하지 않습니다.

---

## 1. 소셜 로그인 검증 (identify 흐름 재사용 · 구현 완료)

**별도 로그인 엔드포인트를 만들지 않았습니다.** Talo가 이미 Steam/Google Play Games/Game Center를 처리하는 `GET /v1/players/identify` + `resolveIdentifier` + `Integration` 패턴에 google/apple을 얹었습니다.

### 호출 (SDK/게임)
Talo SDK의 identify를 그대로 씁니다. SDK는 `PludibaseAuth.SignInWithGoogle(idToken)`으로 감쌌습니다(내부는 `Talo.Players.Identify("google", idToken)`).
```
GET /v1/players/identify?service=google&identifier=<Google ID 토큰(JWT)>
GET /v1/players/identify?service=apple&identifier=<Apple identity 토큰(JWT)>
```

### 백엔드 처리 (구현 완료)
1. `resolveIdentifier`가 게임의 google-sign-in / apple-sign-in Integration을 찾아 토큰을 검증합니다. 구글/애플 공개키(JWKS)로 서명 확인 + `aud`(게임 설정 clientId) + `iss` + 만료 확인.
2. 토큰의 `sub`를 식별자로 `player_alias`(service=`google`/`apple`) 조회/생성.
3. 세션은 기존 identify 흐름이 그대로 처리합니다(socketToken 반환). 게스트 identify와 동일 경로라 새 세션 코드가 없습니다.

### 설정 (게임별)
- Integration 타입 `google-sign-in` / `apple-sign-in`, config = `{ clientId }`(검증용 aud, 비밀 아님이라 미암호화).
- alias service `google`/`apple` 추가(service 컬럼 varchar라 player_alias 마이그레이션 불필요). integration.type enum 마이그레이션만 추가.

### 이월 (미구현)
- 게스트→소셜 명시적 연동(link) 엔드포인트: 백로그. 현재는 같은 `sub`면 같은 player로 이어집니다.

---

## 2. 인앱결제 검증

### `POST /v1/purchases/verify`
플레이어 세션으로 인증된 요청.
```json
{
  "store": "google_play",
  "productId": "gem_pouch",
  "purchaseToken": "<Google Play purchase token>",
  "amount": 4900,
  "currency": "KRW"
}
```
> 파일럿: `amount`/`currency`는 클라이언트 보고값(분석용)입니다. 구매 **유효성**은 백엔드가 스토어로 서버검증합니다. 하드닝 = Google Play 카탈로그 가격 대조로 금액도 서버 확정.
백엔드 처리:
1. `store`에 따라 스토어에 직접 검증.
   - **google_play**: Google Play Developer API `purchases.products.get`(소비성) 로 purchaseToken 검증. 서비스 계정 인증. 상태(구매완료/취소/환불), 상품 일치, 이미 처리된 토큰인지(dedup) 확인. 검증 후 `acknowledge`(3일 내 필수).
   - **app_store**: StoreKit2 서명 트랜잭션(JWS)을 애플 루트 인증서로 검증하거나 App Store Server API로 트랜잭션 조회. 상품/상태 확인.
2. `transaction_id`로 **중복 방지**(같은 결제 재지급 차단). 신규면 `GamePurchase` 저장.
3. 검증된 금액/통화로 **purchase 이벤트 발행**(규격 `amount`/`currency`/`product`/`store` + `order_id`=transaction_id). 대시보드 매출/LTV 자동 반영.
4. 응답 반환.

### 응답 (성공 200)
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
> `valid=false`면 SDK/게임은 지급하지 않습니다. 금액/통화는 **스토어가 준 검증값**입니다(클라가 보낸 값 무시).

### 서버 알림 (환불/취소, P2)
- Google Real-time Developer Notifications(Pub/Sub), Apple App Store Server Notifications V2 수신 엔드포인트 → `GamePurchase.status`를 refunded/revoked로 갱신, 필요 시 환불 이벤트 발행.

---

## 3. 데이터 모델 `GamePurchase` (신규 엔티티)

| 필드 | 설명 |
|---|---|
| id | PK |
| game_id | 게임 |
| player_alias_id (또는 player_id) | 구매자 |
| store | google_play / app_store / steam |
| product_id | 상품 |
| transaction_id | 스토어 트랜잭션 ID (store와 함께 **유니크** = dedup 키) |
| amount, currency | 검증된 금액/통화 |
| status | verified / consumed / refunded / revoked |
| acknowledged | Google acknowledge 여부 |
| raw | 원본 응답/토큰(감사용, 민감정보 주의) |
| created_at, updated_at | |

---

## 4. 보안 원칙
- 서버검증 필수. 클라가 보낸 금액/유효성은 절대 신뢰하지 않는다.
- 영수증/토큰 **재사용 차단**(transaction_id 유니크).
- 스토어 자격증명(서비스 계정 키, 애플 private key)은 시크릿 관리, 커밋 금지.
- 소셜 토큰은 `aud`/`iss`/만료를 모두 확인(다른 앱 토큰 재사용 차단).

---

## 5. 구현 순서 (P1 = 이 계약의 안드로이드 최소 경로)
1. `player_alias` enum에 google/apple 추가 마이그레이션.
2. `/v1/players/auth/google` 구현(구글 토큰 검증 + alias + 세션).
3. `GamePurchase` 엔티티 + 마이그레이션.
4. `/v1/purchases/verify` 구현(Google Play 소비성 검증 + acknowledge + purchase 이벤트).
5. SDK 배선: 응답 sessionToken을 Talo 세션에 연결.
6. Play 샌드박스로 E2E(로그인 → 결제 → 대시보드 매출 확인).

> 스토어 실제 호출(2,4)은 게임별 자격증명이 있어야 E2E가 됩니다(구글 서비스 계정 등). 코드는 먼저 준비하되, 실검증은 자격증명 세팅 후.
