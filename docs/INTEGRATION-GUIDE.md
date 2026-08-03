# pludibase 연동 가이드 (Unity)

v2.0.0 (2026-08-03). SDK 0.1.2 기준으로 다시 씀.

pludibase는 오픈소스 게임 백엔드(Talo 기반)에 한국식 지표 대시보드를 얹은 서비스입니다.
Talo Unity SDK가 세션과 이벤트를 담당하고, pludibase SDK가 그 위에서 **소셜 로그인 서버검증**과
**인앱결제 서버검증**을 얹습니다.

> **큰 그림 하나.** 지표는 두 종류입니다.
> - **세션 지표**: 이벤트를 하나도 안 심어도 SDK가 접속만 하면 자동으로 잡힙니다 (DAU, 동접, 리텐션, 이탈 위험).
> - **이벤트 지표**: 게임의 특정 지점에 한 줄을 심어야 잡힙니다 (매출, 광고수익, 스테이지 퍼널, 튜토리얼, 재화).
>
> 그래서 연동은 "① SDK 붙이기(세션 자동) → ② 필요한 지점에 이벤트 심기" 두 단계입니다.

---

## 0. 시작 전에 받는 것

| 항목 | 설명 | 형태 |
|---|---|---|
| **access key** | 이 게임 전용 API 키 | **JWT 문자열** (`eyJ...`로 시작하는 긴 문자열) |
| **Api Url** | 백엔드 주소 | `https://<백엔드-호스트>` (끝에 `/` 없이) |
| **Socket Url** | 세션용 WebSocket 주소 | `wss://<백엔드-호스트>` (보통 백엔드와 같은 호스트) |

기본 스코프는 `read:players`, `write:players`, `write:events` 입니다.
결제 검증까지 쓰려면 발급 요청 시 말씀해 주세요.

> `localhost`는 서버와 같은 PC(에디터)에서만 통합니다. 폰이나 다른 기기 빌드로 테스트하면
> 서버 PC의 실제 주소여야 합니다.

---

## 1. SDK 설치 (둘 다 필요)

### 1-1. Talo SDK (선행)

pludibase SDK는 Talo 위에서 돕니다. **먼저 설치해야 합니다.**

- Unity Asset Store에서 "Talo Game Services" 검색 후 Import, 또는
- https://github.com/TaloDev/unity/releases 에서 `.unitypackage` 받아 드래그

레포 직접 클론은 서브모듈 때문에 권하지 않습니다.

### 1-2. pludibase SDK

Package Manager > `+` > **Add package from git URL**:

```
https://github.com/mishashak/pludibase-unity-sdk.git
```

공개 저장소라 별도 권한이나 인증 없이 받아집니다.

**설치 확인**: `Library/ScriptAssemblies` 에 `Pludibase.dll` 과 `Pludibase.Talo.dll` 이 생기면 정상입니다.
Console에 `has no meta file, but it's in an immutable folder` 가 보이면 패키지가 무시된 상태이니
버전을 확인하고 알려주세요(0.1.2 미만에서 발생하던 문제입니다).

---

## 2. 설정 애셋 만들기

1. `Assets` 아래에 **`Resources` 폴더**가 없으면 만듭니다.
2. 우클릭 > **Create > Talo > Settings Asset**
3. 이름을 정확히 **`Talo Settings`** 로 합니다. **공백이 들어갑니다.**
4. ⚠️ 위치는 **`Assets/Resources/` 바로 아래**여야 합니다. 하위 폴더에 넣으면 안 됩니다.
   SDK가 `Resources.Load<TaloSettings>("Talo Settings")` 로 찾기 때문에
   `Assets/Resources/Talo/Talo Settings.asset` 은 못 찾고 null이 됩니다.
5. Inspector에서 세 값을 채웁니다.

| 필드 | 값 |
|---|---|
| `Access Key` | 0단계에서 받은 JWT |
| `Api Url` | 0단계의 Api Url |
| `Socket Url` | 0단계의 Socket Url |

나머지 옵션은 기본값 그대로 둡니다. `autoConnectSocket`(기본 켜짐)이 세션 시작을 담당합니다.

### HTTP 허용 (Api Url이 `http://`일 때만)

Unity 2022.1 이상은 평문 `http://` 요청을 기본 차단합니다.
**Edit > Project Settings > Player > Other Settings > Allow downloads over HTTP** 를
`Allowed in development builds` 로 바꿉니다. `https://` 주소면 건드릴 필요 없습니다.

---

## 3. 부트스트랩 붙이기 (세션 자동 시작)

`PludibaseBootstrap` 은 **패키지에 이미 들어 있습니다.** 따로 파일을 받을 필요가 없습니다.

시작 씬의 빈 GameObject를 하나 만들고 `PludibaseBootstrap` 컴포넌트를 붙입니다.

### ⚠️ 인스펙터에 값을 한 번 더 넣어야 합니다

| 필드 | 넣을 값 | 안 넣으면 |
|---|---|---|
| `Player Id` | 비워두세요 | 기기별로 자동 생성해 저장합니다 |
| `Api Url` | Talo Settings와 **같은 값** | 세션과 이벤트는 되지만 **결제/소셜 로그인이 동작하지 않습니다** |
| `Access Key` | Talo Settings와 **같은 값** | 위와 같음 |

Talo Settings는 Talo SDK용이고, 이 두 칸은 pludibase SDK(결제/소셜)용입니다.
같은 백엔드를 가리키므로 값은 동일합니다.

이것만으로 게임 시작 시 게스트 세션이 시작되고 DAU, 동접, 리텐션이 자동으로 쌓입니다.

---

## 4. 소셜 로그인 (선택)

게임이 네이티브 플러그인으로 ID 토큰을 얻은 뒤 넘기면 서버가 검증합니다.

```csharp
using Pludibase;

await PludibaseAuth.SignInWithGoogle(idToken);       // 구글
await PludibaseAuth.SignInWithApple(identityToken);  // 애플
// 로그인 후: TaloGameServices.Talo.CurrentAlias 로 플레이어 접근
```

- **네이티브 로그인 창은 이 SDK가 띄우지 않습니다.** 구글은 Google Sign-In 계열 Unity 플러그인,
  애플은 Sign in with Apple 플러그인이 창을 띄우고 토큰을 줍니다. 이 SDK는 그 토큰을 검증만 합니다.
- 서버 쪽에 게임별 자격증명(구글 OAuth clientId, 애플 키)이 등록돼 있어야 실제 검증이 통과합니다.
- iOS에 구글 로그인을 넣으면 App Store 정책상 애플 로그인도 함께 제공해야 합니다.

---

## 5. 인앱결제 서버검증 (선택)

Unity IAP로 결제가 성공하면 받은 토큰을 넘겨 서버검증합니다.

```csharp
using Pludibase;

var result = await PludibasePurchases.Verify(
    Stores.GooglePlay,   // GooglePlay / AppStore / Steam
    "gem_pouch",         // 스토어에 등록한 상품 ID
    purchaseToken,       // 스토어가 준 구매 토큰/영수증
    4900, "KRW");        // 금액, 통화 (분석용)

if (result.valid)
{
    // 여기서만 지급합니다. valid=false면 지급하지 않습니다.
}
```

- 3개 스토어를 같은 함수로 지원합니다: Google Play, App Store, Steam.
- 금액과 통화는 분석용 보고값이고, **구매 유효성은 서버가 스토어에 직접 물어서** 판정합니다.
- 서버에 게임별 스토어 자격증명이 등록돼야 실제 검증이 됩니다
  (Play 서비스계정, App Store 키, Steam publisher 키).
- Unity IAP `ProcessPurchase` 에 끼우는 예시는 `Samples~/PurchaseVerification` 참고.

---

## 6. 계측 지도 (어느 화면을 켜려면 어디에 무엇을 심나)

여기가 핵심입니다. **모두 심을 필요는 없습니다.** 게임에 있는 기능만 골라 심으면 됩니다.
`bootstrap` 은 씬에 붙인 `PludibaseBootstrap` 컴포넌트 참조입니다.

### 6-1. 결제 지표

**심는 위치: IAP 결제 성공 콜백 안.** (5번의 서버검증과 별개로, 지표 집계용입니다)

```csharp
await bootstrap.LogPurchase(4900, "KRW", "gem_pouch", "appstore");
```

켜지는 화면: 수익화(매출, ARPPU, 결제 유저 수, ARPDAU, 상품별 매출), LTV

### 6-2. 광고 수익

**심는 위치: 미디에이션 SDK의 수익 콜백 안.**

```csharp
await bootstrap.LogAdRevenue(55.5, "KRW", "rewarded", "admob");
```

심는 콜백 예: AdMob `OnAdPaid`, AppLovin MAX `OnAdRevenuePaidEvent`
켜지는 화면: 광고수익(IAA), eCPM, 형태별, 네트워크별

### 6-3. 스테이지 퍼널

게임에 스테이지, 레벨, 챕터 개념이 있으면 여기가 가장 중요합니다.

```csharp
await bootstrap.StageStart(12);                // 진입
await bootstrap.StageEnd(12, "clear", 83);     // 종료 (clear / fail)
```

⚠️ `stage` 는 **반드시 숫자**입니다. 순서 비교로 퍼널을 그리기 때문에 `"1-3"` 같은 문자열은 정렬이 깨집니다.
스테이지 이름이 `1-3` 이라면 통짜 순번으로 매핑해서 보내세요.

켜지는 화면: 스테이지 퍼널(스테이지별 도달자, 구간 이탈률, 평균 도달 스테이지)

### 6-4. 튜토리얼 퍼널

```csharp
await bootstrap.TutorialStep("이동", 1);
await bootstrap.TutorialStep("완료", 4);
```

마지막 "완료" 단계를 꼭 하나 두면 완료율이 정확히 잡힙니다.
켜지는 화면: 튜토리얼 완료율, 단계별 이탈률

### 6-5. 재화

```csharp
await bootstrap.CurrencyChange("보석", 300, "quest_reward");  // 획득
await bootstrap.CurrencyChange("보석", -500, "shop_buy");     // 소진
```

켜지는 화면: 재화 분석(재화별 획득, 소진, 순증감)

### 6-6. 커스텀 이벤트

전용 화면이 아직 없어도 **저장은 되므로**, 중요한 행동은 지금 심어두면 나중에 분석에 씁니다.

```csharp
await Talo.Events.Track("boss_kill", ("boss", "dragon"), ("try", "3"));
await Talo.Events.Flush();
```

---

## 7. 이벤트 규격 정본

| 이벤트 | props | 심는 위치 | 켜지는 화면 |
|---|---|---|---|
| `purchase` | `amount`, `currency`, `product`, `store`(선택), `order_id`(권장) | IAP 결제 **성공** | 매출, ARPPU, PUR, LTV, 상품별 |
| `ad_revenue` | `amount`, `currency`, `format`, `network` | 미디에이션 수익 콜백 | 광고수익, eCPM |
| `stage_start` | `stage`(숫자) | 스테이지 진입 | 스테이지 퍼널, 구간 이탈률 |
| `stage_end` | `stage`(숫자), `result`, `duration_sec`(선택) | 스테이지 종료 | 지금은 저장만 |
| `tutorial_step` | `step`(이름), `index`(숫자) | 튜토리얼 각 단계 | 튜토리얼 완료율 |
| `currency_change` | `currency`, `delta`(부호), `reason`(선택) | 재화 증감 지점 | 재화 획득, 소진, 순증감 |

숫자는 문자열로 보내도 됩니다. 소수점은 반드시 `.` 을 씁니다
(`PludibaseBootstrap` 이 `InvariantCulture` 로 처리해 이 문제를 막아둡니다).

---

## 8. 대시보드에서 확인하기

### 8-1. "테스트했는데 화면에 아무것도 없어요"

가장 많이 걸리는 지점입니다.

에디터 실행이나 Development Build는 SDK가 `X-Talo-Dev-Build: 1` 헤더를 자동으로 보냅니다.
그 데이터는 **개발 데이터**로 분류되고, 대시보드는 기본적으로 **운영 데이터만** 보여줍니다.
즉 테스트 데이터가 안 보이는 게 정상입니다.

해결은 둘 중 하나입니다.
- 대시보드 상단의 **"개발 데이터 포함" 토글을 켜기**
- Development Build 체크를 끄고 빌드해서 실행

### 8-2. 왜 바로 안 뜨나

전송에서 화면까지 **최대 약 1.5분**이 정상입니다.

- 이벤트는 10건마다 또는 `Flush()` 호출 시 전송 (`PludibaseBootstrap` 은 테스트 편의로 매번 Flush)
- 서버가 30초 주기로 저장소에 적재
- 대시보드 응답 캐시 60초

세션(DAU, 동접)은 소켓이 붙는 즉시 잡히고, **리텐션 D1은 다음 날 재접속해야** 값이 생깁니다.

---

## 9. 로그와 디버깅

| 위치 | 보는 법 | 남는 것 |
|---|---|---|
| **게임** | Unity Console | Debug 빌드에서 모든 요청, 응답 전문 자동 출력. 이벤트 거부 시 응답 JSON의 `errors` 에 이유 |
| **서버** | 운영자에게 문의 | 요청 로그, 거부 사유, 소켓 오류, 500 스택 + traceId |

오류 응답에 `traceId` 가 담겨 옵니다. 서버 로그와 대조하면 어느 요청인지 바로 찾습니다.

### 9-1. 자주 나는 문제

| 증상 | 원인 |
|---|---|
| `has no meta file, but it's in an immutable folder` | SDK 0.1.2 미만. 패키지 전체가 무시된 상태 |
| 어셈블리 참조 오류 (Talo를 못 찾음) | SDK 0.1.2 미만 (`Talo` vs `Talo.Runtime`) |
| `Insecure connection not allowed` | `http://` 주소인데 2단계 HTTP 허용을 안 켬 |
| **401** | access key가 틀림 |
| **403** | 키는 맞는데 스코프 부족 |
| Talo Settings를 못 찾음 (null) | 이름에 공백 누락, 또는 Resources 하위 폴더에 둠 (2단계 참고) |
| 화면에 데이터 0 | 십중팔구 8-1의 dev-build 문제 |
| 이벤트는 뜨는데 DAU, 동접이 0 | 소켓 미연결. Socket Url 확인 |
| 결제, 소셜 로그인만 동작 안 함 | `PludibaseBootstrap` 인스펙터의 Api Url, Access Key 누락 (3단계 참고) |

### 9-2. 중복제거 주의

같은 초에 **이름과 props가 완전히 동일한** 이벤트가 두 번 오면 두 번째는 조용히 버려집니다.

- 결제는 `order_id`(자동 삽입)로 이미 면역입니다.
- 대량 반복 지급은 합산해서 한 번에 보내거나 구분되는 `reason` 을 넣으세요.

---

## 10. 기술적 제약 (연동 전 반드시 인지)

어기면 데이터가 조용히 누락되거나 왜곡됩니다.

1. **props는 (문자열, 문자열) 쌍의 배열**이어야 합니다. 직접 HTTP로 보낼 때 배열이 아니면 이벤트가 통째로 거부됩니다.
2. **소수점은 항상 `.`** 입니다. 지역 설정의 `,` 는 0으로 파싱됩니다.
3. **1초 중복제거**가 모든 이벤트에 적용됩니다.
4. **`stage`, `index` 는 숫자**여야 순서 비교가 됩니다.
5. **세션 지표는 소켓 연결 시에만 생성**됩니다. HTTP로 이벤트만 보내면 세션 지표는 0입니다.
6. **dev-build 데이터는 분리**됩니다.
7. **평문 HTTP는 Unity가 기본 차단**합니다.
8. **이벤트/prop 이름은 계약**입니다. 심은 뒤 바꾸면 게임 재배포가 필요합니다.
9. **일부 필드는 저장만** 되고 전용 화면이 아직 없습니다 (`stage_end` 의 `result`, `duration_sec` 등).
10. **통화 혼합은 자동 환산하지 않습니다.**
11. **엔드투엔드 지연 약 1.5분**은 정상입니다.
12. **리텐션 D1은 다음 날** 재접속해야 생깁니다.
13. ⚠️ **오프라인 기록(continuity)은 별도 스코프가 필요합니다.**
    Talo Settings의 `continuityEnabled` 는 기본 켜짐이라, 네트워크가 끊겼다 붙으면 SDK가
    "이 기록은 원래 언제 발생했다"는 타임스탬프를 함께 보냅니다.
    그런데 access key에 **`write:continuityRequests`** 스코프가 없으면
    **오류가 나지 않고 서버가 그 타임스탬프를 조용히 무시합니다.**
    요청은 200으로 통과하고, 기록의 발생 시각만 실제 시각이 아닌 **동기화된 시각**으로 저장됩니다.
    에러가 안 나서 눈치채기 어렵습니다. 오프라인 대응이 필요하면 키 발급 시 이 스코프를 요청하세요.

---

## 11. 연동 완료 체크리스트

- [ ] Talo SDK 설치
- [ ] pludibase SDK 설치 (`Library/ScriptAssemblies` 에 `Pludibase.dll` 확인)
- [ ] `Assets/Resources/Talo Settings` 애셋 생성 (공백 포함, Resources 바로 아래)
- [ ] Talo Settings에 Access Key, Api Url, Socket Url 입력
- [ ] (http 주소면) Allow downloads over HTTP 설정
- [ ] `PludibaseBootstrap` 을 시작 씬 GameObject에 부착
- [ ] **`PludibaseBootstrap` 인스펙터에 Api Url, Access Key 입력** (결제/소셜 쓸 경우)
- [ ] 실행 후 Unity 콘솔에 연결 성공 확인
- [ ] 대시보드 "개발 데이터 포함" 토글을 켜고, 약 1.5분 뒤 세션이 잡히는지 확인
- [ ] (있으면) 결제 성공 지점에 `LogPurchase`
- [ ] (있으면) 광고 수익 콜백에 `LogAdRevenue`
- [ ] (있으면) 스테이지 진입/종료에 `StageStart` / `StageEnd`
- [ ] (있으면) 튜토리얼 각 단계에 `TutorialStep`
- [ ] (있으면) 재화 증감 지점에 `CurrencyChange`
- [ ] 운영 빌드(Development Build 해제)로 최종 확인

막히면 Unity 콘솔 로그와 `traceId` 를 운영자에게 전달하세요.
