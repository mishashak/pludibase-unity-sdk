# 변경 이력

이 프로젝트는 [Semantic Versioning](https://semver.org)을 따른다.

## [0.1.1] - 미출시 (P1 백엔드 연동)
### 변경
- 소셜 로그인을 identify 흐름으로 정리: `PludibaseAuth.SignInWithGoogle/Apple`이 `Talo.Players.Identify("google"/"apple", token)`을 감싼다(백엔드가 별도 로그인 엔드포인트 대신 identify + Integration으로 처리). 로그인 후 `Talo.CurrentAlias`로 접근.
- `PludibasePurchases.Verify`에 `amount`, `currency` 인자 추가(분석용 보고값, 유효성은 서버검증).
### 백엔드 (pludibase-backend, pludibase 브랜치)
- 인증 서버검증 완료(d6d5c9c5), 결제 서버검증 완료(b97736bd). 실 E2E는 게임별 자격증명(구글 OAuth clientId, Play 서비스계정) 설정 후.

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
