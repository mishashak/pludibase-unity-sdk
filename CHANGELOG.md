# 변경 이력

이 프로젝트는 [Semantic Versioning](https://semver.org)을 따른다.

## [0.1.5] - 2026-08-14 (props 최소 페이로드 + 백엔드 하드닝 2건)

### 추가
- **`PludibasePlayers.SetProps(playerId, ...)`**. `Talo.Players.Update()` 자리에 쓰면 본문이 `props` 만
  담긴 최소 페이로드로 나간다. Talo의 Update()는 `JsonUtility.ToJson(Talo.CurrentPlayer)` 로 Player 전체를
  직렬화하는데 `PlayerAlias.player` 가 자기 자신을 다시 물어 `presence` 줄기가 깊이 제한까지 반복된다.
  실제 게임 실측에서 prop 하나를 쓰는 본문의 93%가 그것이었다(props+id 320자 대 presence 4,440자).
  서버 PATCH 핸들러는 `props` 외의 필드를 읽지 않으므로 전부 버려지는 낭비였다.
  `value` 에 `null` 을 주면 그 prop을 삭제한다(JsonUtility로는 표현할 수 없어 JSON을 직접 만든다).
  로컬 `SetProp` 은 그대로 두고 마지막 네트워크 호출만 바꾸면 된다.

### 백엔드 (`pludibase` 브랜치, SDK 코드 변경 아님)
- **merge의 `username` 별칭 충돌 예외.** 두 플레이어가 모두 `username` 별칭을 가져도 병합된다.
  `username` 은 기기 GUID를 담는 자리라 한 플레이어가 여럿 갖는 것이 정상이다. 이게 막혀 있어서
  재설치나 기기추가마다 게스트 플레이어가 고아로 남았다. 병합 후 두 기기의 별칭이 모두 남는다.
  나머지 service는 그대로 400.
- **`google-sign-in` / `apple-sign-in` Integration 누락 가드.** Integration이 없는 게임에서 토큰 검증을
  건너뛰고 넘어온 문자열이 그대로 alias 식별자가 되던 구멍을 막았다(400). 0.1.4 문서 §1의 "알려진 구멍".
- **결제 금액 서버 확정.** Google Play는 카탈로그 가격(구매 지역가 우선, 수량 반영), App Store는 서명된
  트랜잭션의 `price`/`currency` 를 정본으로 쓴다. 응답에 `amountSource`(`store`/`client`) 추가.
  클라이언트 값과 어긋나면 스토어 값으로 기록하고 백엔드 로그에 양쪽을 남긴다.
  Steam은 금액 최소 단위 규칙을 실거래로 확인하기 전까지 클라이언트 값을 그대로 둔다.

### 문서
- 연동 가이드 §5-B(플레이어 속성 저장) 신설, §5 결제 금액 설명 갱신 (v2.2.0).
- 검증 계약 §2-C(`PATCH /v1/players/:id`) 신설. §1 구멍 경고를 "막았다"로, §2 금액 절을 스토어 확정으로,
  §2-B 병합 경고를 `username` 예외로 갱신. §5 남은 일에서 2건 제거 (v1.2.0).

## [0.1.4] - 2026-08-11 (문서 정정: 낡은 설계 초안 제거)
코드 변경 없음. 문서가 실제 구현과 어긋나 연동 담당자를 헤매게 한 부분을 고쳤다.

### 고침
- **`docs/VERIFY-CONTRACT.md` 를 구현 정본으로 다시 씀(v1.1.0).** 이전 판은 P1 착수 **전에 쓴 설계 초안**이라
  실재하지 않는 것을 할 일로 남겨두고 있었다.
  - §5에 있던 `/v1/players/auth/google` 구현 항목 삭제. 그런 라우트는 없다(identify + Integration 흐름).
  - §5에 있던 "SDK 배선: 응답 sessionToken을 Talo 세션에 연결" 삭제. `sessionToken` 이라는 값 자체가 없다.
  - ⚠️ **§2의 금액 설명이 사실과 반대였다.** "금액/통화는 스토어가 준 검증값(클라가 보낸 값 무시)" 이라고
    돼 있었으나, 실제로는 **클라이언트가 보낸 값을 그대로 저장하고 그대로 반환한다.** 서버가 확정하는 것은
    구매의 유효성까지다. 정산 정본으로 쓰면 안 된다는 경고와 함께 정정.
  - Steam 결제 검증(3번째 스토어), 에러코드(`UNSUPPORTED_STORE`/`BILLING_NOT_CONFIGURED`),
    멱등성, 스토어별 `purchaseToken` 의미를 실제 코드 기준으로 채움.
  - §2-B 플레이어 병합 절 신설: 방향, props 우선순위(**흡수되는 쪽이 이김**), 세션 유지,
    같은 service 별칭 중복 시 400(재설치에서 걸림), 재시도 판정.
  - `GamePurchase.raw` 는 현재 기록하지 않음을 명시.
- **연동 가이드 §0의 결제 스코프 문구 정정(v2.1.0).** "결제 검증까지 쓰려면 발급 요청 시 말씀해 주세요"는
  오해를 부른다. `/v1/purchases/verify` 는 기본 스코프에 이미 있는 `write:events` 하나만 요구한다.
  결제에 필요한 건 스코프가 아니라 게임별 스토어 자격증명이다. 대신 실제로 추가 발급이 필요한
  `write:continuityRequests`(없으면 403이 아니라 조용히 무시됨)를 그 자리에 적었다.

## [0.1.3] - 2026-08-10 (광고수익 IAA 연동 준비)
### 변경
- `LogAdRevenue` 에 `adUnit`, `placement` 선택 인자 추가. 미디에이션이 공짜로 주는 값이라
  심어두면 나중에 광고 단위별 분석을 소급할 수 있다(저장이 스키마리스라 비용 0).
  기존 4인자 호출은 그대로 동작한다.
### 문서
- 연동 가이드 6-2장을 IAA 절로 확장: AppLovin MAX / Unity LevelPlay / AdMob 콜백 대조표,
  MAX 연동 예시 코드(4개 포맷), 통화 원칙.
- ⚠️ **통화**: 미디에이션은 보통 USD로 수익을 준다(MAX는 USD 고정). 대시보드가 통화를 환산하지
  않으므로 **게임에서 매출 발생일 환율로 환산해 IAP와 같은 통화로** 보내야 합산 매출이 나온다.
  제약 10번에도 반영.

## [0.1.2] - 2026-08-03 (연동 차단 결함 수정)
### 추가
- **연동 가이드를 이 저장소로 옮김**: `docs/INTEGRATION-GUIDE.md` (v2.0.0). 기존 가이드는 SDK보다 먼저 쓰여
  설치 방법, 파일명, access key 형태가 현행과 어긋나 있었다. 공개 저장소에 두어 연동 담당자가 바로 볼 수 있게 한다.
  회사별 접속값(URL, 키)은 문서에 넣지 않고 자리표시자로 둔다.
- `Tools~/check-package.py`: 유니티 없이 패키지 구조를 검사한다(아래 결함 2건이 이 검사로 잡힌다).

### 고침
- **`.meta` 파일 전량 추가(17개).** UPM 패키지는 immutable folder라 Unity가 `.meta`를 생성해주지 않는다. 없으면 모든 파일이 `has no meta file, but it's in an immutable folder. The asset will be ignored.` 로 무시되고 `Library/ScriptAssemblies`에 `Pludibase.dll`이 아예 안 만들어진다. 즉 **패키지 전체가 컴파일되지 않았다.**
- **`Pludibase.Talo.asmdef`의 references를 `Talo` → `Talo.Runtime`으로 정정.** Talo SDK의 실제 런타임 어셈블리 이름은 `Talo.Runtime`이다(TaloDev/unity `TaloRuntime.asmdef` 확인). 위 `.meta` 문제를 고치면 다음으로 막히던 지점.

> 두 결함 모두 실제 Unity 프로젝트에 붙여본 적 없이 배포해서 생겼다. 0.1.2부터는 연동 확인 후 태그한다.

## [0.1.1] - 미출시 (P1 백엔드 연동)
### 변경
- 소셜 로그인을 identify 흐름으로 정리: `PludibaseAuth.SignInWithGoogle/Apple`이 `Talo.Players.Identify("google"/"apple", token)`을 감싼다(백엔드가 별도 로그인 엔드포인트 대신 identify + Integration으로 처리). 로그인 후 `Talo.CurrentAlias`로 접근.
- `PludibasePurchases.Verify`에 `amount`, `currency` 인자 추가(분석용 보고값, 유효성은 서버검증).
### 백엔드 (pludibase-backend, pludibase 브랜치)
- 인증 서버검증 완료(d6d5c9c5, 구글/애플 identify + 스팀은 Talo 네이티브). 결제 서버검증 3개 스토어: Google Play(b97736bd) + App Store(5f872497) + Steam(4867ea92). SDK는 세 스토어를 `store` 인자로 균일 지원.
- 실 E2E는 게임별 자격증명 설정 후: 구글 OAuth clientId, Play 서비스계정, App Store 키(.p8/issuerId/keyId/bundleId), Steam publisher 키(기존 steamworks 통합 재사용).

## [0.1.0] - 미출시 (P0 스캐폴딩)
### 추가
- 패키지 스캐폴딩: `package.json`, asmdef(코어/Bootstrap 분리), README, LICENSE(MIT).
- 코어 API 표면(백엔드 계약 대기 = P1):
  - `PludibaseAuth.SignInWithGoogle(idToken)` / `SignInWithApple(identityToken)` - 소셜 ID 토큰 서버검증 → 세션.
  - `PludibasePurchases.Verify(store, productId, purchaseToken)` - 인앱결제 서버검증.
  - `PludibaseClient.Configure(apiUrl, accessKey)` - 전역 설정.
- `PludibaseBootstrap` - 게스트 세션 시작 + 이벤트 헬퍼(구 TaloBootstrap 흡수, Talo 의존).
- 백엔드 검증 계약 스펙 `docs/VERIFY-CONTRACT.md`.

### 아직 아님 (다음 단계)
- P1: 백엔드 verify 엔드포인트 구현(구글 토큰 검증 + Google Play 영수증 검증) 후 안드로이드 E2E.
- P2: Apple Sign-In + App Store 영수증 검증(iOS).
- P3: 네이티브 sign-in 번들 one-call, Unity IAP 자동 프로세서, prefab auto-init, autocapture.
