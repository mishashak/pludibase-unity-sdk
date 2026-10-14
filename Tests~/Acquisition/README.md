# 설치 계측 오프라인 검수

Windows .NET Framework compiler로 새 acquisition 모듈을 Unity stubs와 함께 컴파일·실행합니다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests~\Acquisition\Run.ps1
# PowerShell 7의 bundled Roslyn이 있을 때: 기존 Bootstrap 포함 전체 Runtime 조건부 컴파일만 수행
pwsh -NoProfile -File .\Tests~\Acquisition\CompileAll.ps1
```

테스트는 명시적으로 이름 붙인 `pludibase-acquisition-tests` 임시 폴더에만 저장합니다. UnityWebRequest stub에는 네트워크 구현이 없으며 `http://127.0.0.1:1/v1/acquisition/` 이외 URL도 거부합니다. 실제 게임 설정·환경변수·키·Unity 프로젝트·서버·DB를 읽지 않습니다. 설치 ID/secret 값은 출력하지 않습니다.

UTC 자정·60초·submillisecond 분할, first-open/queue 재시작 보존, tmp 복구·손상 fail-closed, pause/focus 중복·배경 제외, Identify 전 HTTP 헤더, first-open/event 응답 유실 재전송, duplicate acknowledgement, rejected link 보존·재시도를 검사합니다.

추가 회귀 검수: invalid ASCII/한글/공백 이벤트 이름이 저장되지 않는지, link provider가 매 시도 정확한 body·alias와 main-thread 문맥을 받는지, 새 signature·session/player 전달, 핵심 헤더 override·중복·줄바꿈 차단, 헤더 credential 비영구화, 이전 alias의 인증 deferral 중 현재 alias link와 설치 활동 계속 전송, 1 MiB 초과 적법 큐의 512 KiB UTF-8 배치 분할, 한글·최악 JSON escape 단일 이벤트, 배치 retry와 원래 이벤트 ID·시각 보존을 확인합니다. 실제 HMAC/로그인 검증은 수행하지 않습니다.

provider 예외 중 실제 lifecycle의 gameplay duration 업로드, attribution 503 backoff 중 app_open·후속 다운로드 활동 전송, control 원문·event ID·시각 보존도 검사합니다. first-open ack 전에 활동을 전송하지 않는 초기 gate는 유지합니다.

JsonUtility는 test-only JavaScriptSerializer로 대체하므로 실제 Unity serializer/IL2CPP, Android Java/Gradle, Play 설치 경로 검증을 대신하지 않습니다. `Run.ps1`은 acquisition만 컴파일·실행합니다. `CompileAll.ps1`은 기존 Bootstrap/결제 SDK까지 Editor/Android/iOS 조건으로 stub 컴파일하며 실행하지 않습니다. 실제 Talo SDK 내부 API/queue 검증은 포함하지 않습니다.
