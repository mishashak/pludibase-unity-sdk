# 설치·초기 활동 계측

SDK 0.2.0 후보 / 2026-10-08. 설치별 마케팅 계측은 Talo Identify와 별도 HTTP·영구 큐를 사용합니다. 키에는 `write:events` 스코프가 필요하며, alias 없이 첫 실행·초기 다운로드 이탈을 기록합니다. Talo의 `write:continuityRequests`와 관계없이 이벤트 ID와 최초 발생 UTC 시각을 그대로 보존합니다.

## 초기화 위치

**추가 다운로드를 시작하기 전에** 첫 실행 씬의 `Awake` 또는 `BeforeSceneLoad` 진입점에서 호출합니다. 씬이 추가 콘텐츠에 포함돼 있거나 다운로드가 끝난 뒤 로드된다면 그 씬의 Bootstrap만으로 초기 이탈을 측정할 수 없습니다.

```csharp
using Pludibase;
using UnityEngine;

public static class EarlyInstallationAnalytics
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeDownloads()
    {
        // 실제 게임 설정값을 사용합니다. access key는 게임용 키이며 운영 관리자 키를 넣지 않습니다.
        PludibaseAcquisition.Initialize(new AcquisitionOptions
        {
            ApiUrl = "https://api.example.com",
            AccessKey = "GAME_ACCESS_KEY_WITH_WRITE_EVENTS",
            StorageNamespace = "my-game",
            DevelopmentBuild = Debug.isDebugBuild,
            BuildVersion = "build-123",
            Country = "", // 선택한 테스트/대상 국가; 실제 위치 감지가 아닙니다.
            CollectGooglePlayReferrer = false // Java bridge 설치 후 명시적으로 켭니다.
        });
        // InitReady는 영구 저장 완료입니다. HTTP 연결 완료를 기다리지 않아 다운로드를 막지 않습니다.
    }
}
```

`PludibaseBootstrap.Awake`에도 fallback 초기화가 들어 있습니다. 위 호출로 이미 초기화했으면 그대로 사용합니다. `Start`의 Identify를 기다리지 않습니다. 동일 설정의 Initialize를 반복 호출해도 설치·collector·app_open을 복제하지 않으며, 다른 설정으로 재초기화하면 오류를 냅니다. 모든 공개 API는 Unity main thread에서 호출합니다. 외부 SDK의 worker-thread 콜백은 게임의 main-thread dispatcher를 거쳐 호출하세요.

## 설치 식별과 저장

`Application.persistentDataPath/pludibase-acquisition/<namespace/environment hash>/installation.json`에 설치 GUID, 32바이트 암호학적 난수의 64자리 hex secret, first-open eventId·UTC 시각과 큐를 함께 저장합니다. secret은 로그·진단·일반 이벤트·CSV로 내보내지 않습니다. 로컬 파일에는 secret과 Referrer 증거가 있으므로 디버그 첨부나 백업 공유 전에 제거하세요.

checksummed 임시 파일을 flush한 후 atomic replace를 사용합니다. Replace 미지원 플랫폼은 이름 이동과 main/tmp/backup의 최신 유효 snapshot 복구를 사용합니다. 전부 손상된 파일이 있으면 새 설치 ID를 만들지 않고 수집을 중지합니다. 저장 공간·권한 오류도 `InitReady = false`로 중지하며 `Diagnostics.LastError`로 확인합니다. 파일을 복구한 뒤 앱을 다시 실행합니다.

기존 `pludibase_player_id` PlayerPrefs·결제 player GUID와 installId는 별개입니다. 로그인·Player 병합으로 설치 기록을 바꾸지 않습니다. dev/live 저장·헤더를 분리하며 `x-talo-dev-build`를 `1`/`0`으로 전송합니다. 같은 namespace의 API URL을 바꿔 미전송 큐를 다른 서버로 보내려 하면 초기화를 거부합니다. 테스트 환경 변경은 별도 namespace를 사용합니다.

앱 삭제·데이터 초기화로 파일이 사라지면 새 설치입니다. OS 백업이 앱 파일을 복원하면 기존 설치 ID도 복원될 수 있습니다. 재설치 분모를 엄밀히 구분해야 하는 게임은 Android backup/data-extraction 규칙에서 이 로컬 저장 경로를 제외하는 정책을 별도 검수해야 합니다. 이 SDK는 AndroidManifest를 변경하지 않습니다.

## 이벤트 지점

```csharp
using System.Collections.Generic;

PludibaseAcquisition.Track("download_start");
// 추가 콘텐츠 다운로드 성공 후
PludibaseAcquisition.Track("download_complete");

// 실제 플레이가 시작/끝나는 위치. 앱 전경에 있을 때만 시간을 더합니다.
PludibaseAcquisition.SetGameplayActive(true);
PludibaseAcquisition.Track("stage_start", new Dictionary<string, string> { { "stage", "12" } });
// 메뉴·로딩으로 나갈 때
PludibaseAcquisition.SetGameplayActive(false);
PludibaseAcquisition.Track("stage_end", new Dictionary<string, string> { { "stage", "12" }, { "result", "clear" } });

// 검증된 Identify/소셜 로그인 성공 후
PludibaseAcquisition.LinkIdentifiedPlayer(TaloGameServices.Talo.CurrentAlias.id.ToString());
PludibaseAcquisition.Track("login_success");
```

| 이벤트 | 실행 지점 |
|---|---|
| first-open endpoint | 설치 저장 최초 1회; 원래 ID·시각으로 재전송 |
| `app_open` | 초기 전경 진입과 pause/focus 조합으로 실제 전경에 돌아왔을 때 |
| `foreground_duration` | 전경 시간 자동 누적·기본 15초 checkpoint |
| `gameplay_duration` | `SetGameplayActive(true)` 이후 전경인 구간만 누적 |
| `download_start`, `download_complete` | 추가 다운로드 시작·성공 |
| `login_success` | 실제 로그인 성공 |
| `tutorial_start`, `tutorial_complete` | 튜토리얼 시작·완료 |
| `stage_start`, `stage_end`, `next_stage_start` | 해당 스테이지 지점 |

duration props는 `seconds`, `startedAt`, `endedAt`입니다. 각각 60초 이하로 잘라 UTC 자정 경계를 분할하고 monotonic timer로 background·wall-clock 변경에 따른 음수 시간을 피합니다. resume 시 UTC anchor를 새로 잡습니다. pause/focus 중복 콜백은 같은 전경 상태를 두 번 시작하지 않습니다. 강제 종료 직전 아직 checkpoint하지 못한 시간은 유실될 수 있습니다(기본 최대 약 15초). OS가 pause 콜백 없이 장시간 프로세스를 suspend한 경우는 기기 검수가 필요합니다.

Bootstrap `StageStart/StageEnd` 헬퍼는 기존 Talo 이벤트와 설치 계측을 함께 호출합니다. 같은 행동에 위 직접 `Track`까지 중복으로 심지 않습니다. 기존 `TutorialStep`는 의미가 다른 이벤트이므로 `tutorial_start/complete`를 게임의 실제 지점에서 별도로 심습니다.

## link 인증 문맥

첫 실행·활동·Referrer는 alias 없이 보냅니다. 설치와 플레이어를 연결하는 link는 검증된 alias가 필요합니다. **`verifyRequests=true` 게임은 `x-talo-signature`도 필요**합니다. native TALO 로그인 플레이어는 로그인으로 얻은 `x-talo-session` JWT와 그 토큰의 `x-talo-player` UUID를 함께 보내야 하며 둘 다 link의 alias와 일치해야 합니다. SDK가 기존 서버 보안을 해제하거나 Talo 내부 자격증명을 읽지는 않습니다.

게임의 기존 Talo 인증 어댑터를 `AcquisitionOptions.LinkHeadersProvider`에 전달하세요. Bootstrap을 먼저 초기화한 게임은 main thread에서 `PludibaseAcquisition.SetLinkHeadersProvider(provider)`로 설정할 수 있습니다. 콜백은 **실제 HTTP 시도마다** main thread에서 호출되고, 읽기 전용 context의 `Method`, `Url`, `Path`, `Body`, `AliasId`를 받습니다. `Body`는 실제 전송 JSON과 정확히 같습니다. installationSecret이 포함돼 있으므로 context나 헤더 값을 로그·파일로 저장하지 마세요.

아래 연결 예시의 `currentAliasId`, `playerId`, `sessionToken`, `makeFreshSignature`는 게임의 기존 로그인·서명 모듈에서 제공하는 값/함수입니다. SDK에 같은 이름의 Talo API가 있다고 가정하는 예시는 아닙니다.

```csharp
PludibaseAcquisition.SetLinkHeadersProvider(request =>
{
    // 로그아웃 또는 이전 alias의 큐 항목에는 다른 플레이어의 자격증명을 붙이지 않습니다.
    if (request.AliasId != currentAliasId) return null;
    var headers = new Dictionary<string, string>();
    if (requiresSignedRequests)
        headers["x-talo-signature"] = makeFreshSignature(request.Body);
    if (isNativeTaloAuth)
    {
        headers["x-talo-session"] = sessionToken;
        headers["x-talo-player"] = playerId;
    }
    return headers;
});
// 인증 설정을 고친 뒤 이전에 4xx로 보존된 link를 다시 올리는 경우
PludibaseAcquisition.RetryRejected();
```

현재 백엔드 `signature-middleware.ts`, `verify-signature.ts`, `player-auth-middleware.ts`(2026-10-08 작업 코드) 계약은 다음과 같습니다. 서명 header JSON은 `{ rid, payload, timestamp }`이며 payload는 **실제 body UTF-8의 SHA256 hex**, timestamp는 Unix 밀리초입니다. header JSON의 UTF-8을 Base64한 문자열을 기존 verification key로 HMAC-SHA256한 뒤 Base64하여 `keyVersion|headerB64.signatureB64`를 구성합니다. 현재 검증은 method/URL을 body hash 재료에 넣지 않습니다. 재전송에서도 rid와 timestamp·signature는 매 시도 새로 생성해야 합니다. public game access key를 verification key로 대신 사용하지 마세요. 이 SDK는 서명을 자체 생성하거나 Talo package의 미확인 내부 API를 호출하지 않습니다.

반환 헤더는 현재 알려진 추가 인증 헤더 `x-talo-signature`, `x-talo-session`, `x-talo-player`만 허용합니다. session/player는 함께 제공해야 합니다. Bearer Authorization, alias, dev/live, Content-Type과 설치 proof는 SDK가 정하고 override를 차단합니다. 대소문자만 다른 중복 헤더·CR/LF/NUL도 거부합니다. provider가 null을 반환하거나 실패하면 `Diagnostics.LastError = "link_headers_unavailable"`로만 표시하고 해당 link를 보존하여 재시도합니다. 이전 alias의 인증 문맥이 없더라도 뒤의 설치 활동과 현재 alias의 link 전송을 막지 않습니다. 예외 메시지나 credential header는 영구 큐·진단·로그에 남기지 않습니다. provider 미설정은 서명·native session이 필요 없는 기존 alias-only 게임의 동작을 유지합니다.

실제 게임이 사용하는 Talo package의 인증 어댑터·verification key·native session 연결은 별도 통합 검수 대상입니다. 여기의 테스트는 header 전달·재시도·보호·비영구화를 검증했으며 실제 서버 서명 통과나 플레이어 세션 검증을 실행하지 않았습니다.

## 오프라인 전송과 장애 상태

first-open 응답을 확인한 뒤 **최대 100건·실제 UTF-8 JSON body 512 KiB 이하**의 연속 일반 이벤트를 배치로 보냅니다. 한글·JSON escape와 설치 proof envelope까지 직렬화된 byte 수를 계산하고 상한 전에 나눕니다. 적법한 단일 최대 이벤트도 이 한도에 들어갑니다. Attribution·link는 독립 요청이며 link에만 검증된 alias와 필요한 추가 인증 헤더를 붙입니다. 서버 확인 뒤에만 실제 보낸 배치의 항목을 큐에서 제거합니다. 응답 유실·앱 재시작·오프라인 재전송도 원래 eventId·발생시각을 사용하므로 서버의 `(game_id,event_id)` 유일키로 중복 제거합니다.

기본 요청 timeout 10초, exponential backoff 최대 약 300초와 jitter를 사용합니다. 네트워크 실패·408·429·5xx를 재시도합니다. first-open 실패는 큐를 보존한 채 재시도합니다. 일반 요청의 그 외 4xx는 로컬 rejected 목록에 보존하고 다음 항목으로 진행합니다. `Diagnostics.RejectedCount`를 확인하고 설정·payload 문제를 해결한 후 `RetryRejected()`를 호출합니다. 큐를 임의로 지우거나 silent drop하지 않습니다. 장기 오프라인이면 로컬 큐가 커지므로 디스크 여유를 관리하세요.

first-open 확인 전에는 다른 이벤트를 보내지 않습니다. 확인 후 link·attribution이 재시도 대기 중이면 해당 원문·ID·시각은 보존하면서 뒤의 활동 이벤트와 현재 alias의 link를 전송합니다. 이전 alias의 인증 문맥이나 늦은 Referrer가 앱 활동 수집을 멈추게 하지 않습니다.

`Flush()`는 시간을 checkpoint하고 재시도를 즉시 허용합니다. HTTP 완료를 반환하거나 앱 종료 시 전송을 보장하는 API가 아닙니다. 저장된 큐는 다음 실행에서 계속 전송합니다. InstallationSecret이나 raw evidence가 담긴 본문은 Console에 출력하지 않습니다.

서버와 같은 payload 상한을 호출 시 검사합니다: 이벤트 이름은 1~64자의 ASCII 영문·숫자·`_ . : -`(`^[a-zA-Z0-9_.:-]+$`), props 최대 32개·키 64자·값 1,024자, raw Referrer 8,192 UTF-8 bytes. 잘못된 이벤트 이름이나 상한 초과는 큐에 넣기 전에 오류를 내며 원문을 자르지 않습니다.

## Referrer 상태

Google Play 실제 수집은 [opt-in Java/Gradle 샘플](../Samples~/GooglePlayInstallReferrer/README.md)을 설치한 Android player에서만 켭니다. 실패·미지원·nonce·원문·Google 시각을 보존하며 first-open 수집을 기다리게 하지 않습니다. 증거 수신 뒤 attribution upload도 영구 큐를 거칩니다.

수신한 외부 provider 원문을 `SubmitAttribution("google_play_referrer", raw, clickAt, beginAt)` 또는 `SubmitAttribution("meta_install_referrer", raw, clickAt, beginAt)`로 전달할 수 있습니다. 이는 원문 전달이며 특정 adId 확정이나 Meta 복호화를 구현한 기능이 아닙니다. Meta 전용 수집·복호화 어댑터와 자격 확인은 미구현이며 `Diagnostics.MetaReferrerStatus`는 `unavailable`입니다. unknown을 자연 유입이나 특정 소재로 바꾸지 않습니다.

## 검수 범위

로컬 pure C# 테스트는 임시 디렉터리와 네트워크 금지 Unity stubs로 실행합니다. 영구 ID·재시작·snapshot 복구·UTC 경계·duration 분할·HTTP 중복 재전송·pause/focus, 이벤트 이름 사전 검증, link 인증 콜백·헤더 보호·비영구화, UTF-8/escape 배치 분할·재시도 상태를 검사합니다. 실제 Unity/IL2CPP 컴파일, 실제 게임 Talo 인증 어댑터, Android Gradle 빌드, Play 설치와 실광고 Referrer·Meta 복호화는 별도 통합·기기 검수 대상입니다. 이 구현 작업에서 운영 HTTP·실게임·실광고를 실행하지 않았습니다.
