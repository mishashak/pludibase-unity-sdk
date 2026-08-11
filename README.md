# Pludibase Unity SDK

Talo 게임 백엔드 위에 얹는 pludibase 얇은 SDK입니다. **구글/애플 계정 로그인 서버검증**과 **인앱결제(IAP) 서버검증**을 게임에 붙여줍니다.

네이티브 로그인 창과 스토어 결제 창은 이 SDK가 띄우지 않습니다. Unity IAP와 각 로그인 플러그인이 창을 띄우고, 이 SDK는 거기서 나온 **토큰/영수증을 pludibase 백엔드로 넘겨 검증**합니다. 어렵고 위험한 검증/환불/매출 집계는 전부 백엔드가 맡습니다.

> **연동은 [`docs/INTEGRATION-GUIDE.md`](docs/INTEGRATION-GUIDE.md) 하나만 따라가면 됩니다.**
> 설치, 설정, 계측 지점, 자주 나는 문제까지 전부 거기 있습니다.

> 상태: **0.1.4**. 백엔드 서버검증은 3개 스토어(Google Play, App Store, Steam)와
> 구글/애플 로그인까지 붙어 있고, 광고수익(IAA) 전송 헬퍼도 들어 있습니다.
> 백엔드 계약은 [`docs/VERIFY-CONTRACT.md`](docs/VERIFY-CONTRACT.md).

## 무엇을 하고 무엇을 안 하나

| 이 SDK(우리)가 하는 것 | 게임/스토어 몫 |
|---|---|
| ID 토큰/영수증을 백엔드로 넘겨 검증, 세션 확보 | 네이티브 로그인 창(구글/애플 플러그인) 띄워 토큰 얻기 |
| 검증 결과(엔타이틀먼트) 반환 | Unity IAP로 결제 창 띄우고 상품 등록(스토어 콘솔) |
| 게스트 세션 시작 + 이벤트 헬퍼(Bootstrap) | 스토어 개발자 계정, 심사, 수수료 |

## 설치

### 1. 선행: Talo SDK
이 SDK는 Talo SDK 위에서 돕니다. 먼저 설치하세요.
- Unity Asset Store에서 "Talo Game Services" 검색 후 Import, 또는
- https://github.com/TaloDev/unity/releases 에서 `.unitypackage` 받아 드래그.

### 2. 이 SDK
Package Manager > Add package from git URL:
```
https://github.com/mishashak/pludibase-unity-sdk.git
```
공개 저장소라 별도 권한이나 인증 없이 받아집니다.
설치가 됐는지는 `Library/ScriptAssemblies` 에 `Pludibase.dll` 이 생겼는지로 확인합니다.

## 설정
`apiUrl`과 `accessKey`는 Talo Settings 애셋에 넣는 값과 동일하게 줍니다(같은 백엔드를 가리키므로).
```csharp
Pludibase.PludibaseClient.Configure("https://api.example.com", "talo_xxxxxxxx");
```
`PludibaseBootstrap` 컴포넌트를 쓰면 인스펙터에 apiUrl/accessKey를 넣어 자동으로 Configure합니다.

## 사용

### 소셜 로그인 (구글/애플)
게임이 네이티브 플러그인으로 ID 토큰을 얻은 뒤 넘깁니다. 내부는 `Talo.Players.Identify`라, 로그인 후 플레이어는 `Talo.CurrentAlias`로 접근합니다.
```csharp
using Pludibase;

// 구글 로그인 플러그인이 idToken을 콜백으로 준 뒤
await PludibaseAuth.SignInWithGoogle(idToken);

// 애플
await PludibaseAuth.SignInWithApple(identityToken);

// 로그인 후: TaloGameServices.Talo.CurrentAlias 로 플레이어 접근
```
> ID 토큰 얻는 법: 구글은 Google Sign-In 계열 Unity 플러그인, 애플은 "Sign in with Apple" Unity 플러그인(예: lupidan/apple-signin-unity)을 씁니다. iOS에 구글 로그인을 넣으면 App Store 정책상 애플 로그인도 함께 제공해야 합니다.

### 인앱결제 검증
Unity IAP로 결제가 성공하면, 받은 구매 토큰을 넘겨 서버검증합니다.
```csharp
using Pludibase;

var result = await PludibasePurchases.Verify(
    Stores.GooglePlay,           // Stores.AppStore / Stores.Steam 도 지원(전부 서버검증)
    "gem_pouch",                 // 스토어에 등록한 상품 ID
    purchaseToken,               // Google Play purchase token
    4900, "KRW");                // 금액/통화(분석용). 구매 유효성은 백엔드가 스토어로 서버검증

if (result.valid)
{
    // 지급. valid=false면 지급하지 않습니다.
}
```
Unity IAP의 `ProcessPurchase`에 어떻게 끼우는지는 [`Samples~/PurchaseVerification`](Samples~/PurchaseVerification/README.md) 참고.

### 이벤트(선택)
`PludibaseBootstrap`은 게스트 세션 시작과 함께 `LogPurchase`, `LogAdRevenue`, `StageStart/End`, `TutorialStep`, `CurrencyChange` 헬퍼를 제공합니다(대시보드 지표 자동 집계).

## 로드맵
- P1: 백엔드 verify 구현 후 안드로이드 E2E(구글 로그인 + Google Play 결제).
- P2: iOS(Apple Sign-In + App Store 결제) + 서버 알림(환불).
- P3: 네이티브 sign-in 번들 one-call, Unity IAP 자동 프로세서, prefab auto-init, autocapture.

## 라이선스
MIT. Talo SDK도 MIT입니다.
