using System.Globalization;
using System.Threading.Tasks;
using TaloGameServices;
using UnityEngine;

namespace Pludibase
{
    /// <summary>
    /// pludibase 연동 부트스트랩 (딸깍 파일). 씬의 빈 GameObject에 붙이면:
    ///   - 게임 시작 시 게스트 플레이어 세션 시작 (DAU/동접/리텐션 자동 집계)
    ///   - LogPurchase / LogAdRevenue 등 헬퍼로 이벤트 전송
    /// 준비물: Resources 폴더의 "Talo Settings" 애셋에 accessKey/apiUrl/socketUrl 세 값.
    /// 소셜 로그인(구글/애플)이 필요하면 Start의 게스트 Identify를 PludibaseAuth.SignInWithGoogle/Apple로 대체/승격한다.
    /// (구 TaloBootstrap.cs를 SDK로 흡수한 것. Talo 어셈블리에 의존.)
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class PludibaseBootstrap : MonoBehaviour
    {
        [Tooltip("이 플레이어를 식별하는 고정 ID. 비워두면 기기별로 자동 생성해 저장합니다.")]
        public string playerId = "";

        [Tooltip("PludibaseClient(결제/소셜 로그인)도 함께 설정할 API 주소와 access key. Talo Settings와 동일하게.")]
        public string apiUrl = "";
        public string accessKey = "";

        [Tooltip("설치별 계측을 Awake에서 초기화합니다. 추가 다운로드 전 첫 씬에 두거나 Acquisition.Initialize를 더 먼저 호출하세요.")]
        public bool initializeAcquisition = true;
        public string acquisitionStorageNamespace = "default";
        [Tooltip("Google Play Referrer Java bridge와 Gradle 의존성을 직접 설치했을 때만 켭니다.")]
        public bool collectGooglePlayReferrer = false;
        [Tooltip("선택한 테스트/대상 국가(두 글자). 기기 위치를 감지하는 값이 아닙니다.")]
        public string acquisitionCountry = "";
        public string buildVersion = "";

        public bool InitReady => PludibaseAcquisition.InitReady;
        private static PludibaseBootstrap activeBootstrap;
        private bool ownsBootstrap;

        private const string PlayerIdKey = "pludibase_player_id";

        private void Awake()
        {
            if (activeBootstrap != null && activeBootstrap != this)
            {
                Destroy(this);
                return;
            }
            activeBootstrap = this;
            ownsBootstrap = true;
            if (!initializeAcquisition || PludibaseAcquisition.InitReady) return;
            if (string.IsNullOrEmpty(apiUrl) || string.IsNullOrEmpty(accessKey))
            {
                Debug.LogWarning("[pludibase] 설치 계측에는 Bootstrap의 Api Url / Access Key 또는 사전 Acquisition.Initialize 호출이 필요합니다.");
                return;
            }
            try
            {
                PludibaseAcquisition.Initialize(new AcquisitionOptions
                {
                    ApiUrl = apiUrl, AccessKey = accessKey,
                    StorageNamespace = acquisitionStorageNamespace,
                    CollectGooglePlayReferrer = collectGooglePlayReferrer,
                    Country = acquisitionCountry, BuildVersion = buildVersion
                });
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[pludibase] 설치 계측 초기화 실패 (" + ex.GetType().Name + "). 설정·저장소를 확인하세요.");
            }
        }

        private void OnDestroy()
        {
            if (activeBootstrap == this) activeBootstrap = null;
        }

        private async void Start()
        {
            if (!ownsBootstrap) return;
            // 결제/소셜 로그인 SDK도 같은 백엔드를 쓰도록 설정.
            if (!string.IsNullOrEmpty(apiUrl) && !string.IsNullOrEmpty(accessKey))
            {
                PludibaseClient.Configure(apiUrl, accessKey);
            }

            // 기기별 고정 ID 확보 (최초 실행 때 생성/저장 → 재접속 시 같은 플레이어로 인식되어 리텐션이 잡힘)
            if (string.IsNullOrEmpty(playerId))
            {
                playerId = PlayerPrefs.GetString(PlayerIdKey, "");
                if (string.IsNullOrEmpty(playerId))
                {
                    playerId = System.Guid.NewGuid().ToString();
                    PlayerPrefs.SetString(PlayerIdKey, playerId);
                    PlayerPrefs.Save();
                }
            }

            try
            {
                // 게스트 식별(비밀번호 없음). 이 한 줄로 플레이어/별칭 생성 + 소켓 세션 시작.
                // 실제 계정 로그인이 필요하면 PludibaseAuth.SignInWithGoogle/Apple 로 승격하세요.
                await Talo.Players.Identify("username", playerId);
                Debug.Log($"[pludibase] 연결 성공 · alias={Talo.CurrentAlias.id}");
                if (PludibaseAcquisition.InitReady)
                    PludibaseAcquisition.LinkIdentifiedPlayer(Talo.CurrentAlias.id.ToString());
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[pludibase] 연결 실패: {ex.Message}");
            }
        }

        // 결제 성공 시 호출(간이). 서버검증까지 원하면 PludibasePurchases.Verify를 쓰세요.
        // 예) await bootstrap.LogPurchase(4900, "KRW", "gem_pouch", Stores.AppStore);
        public async Task LogPurchase(double amount, string currency, string product, string store = "")
        {
            await Talo.Events.Track("purchase",
                ("amount", amount.ToString(CultureInfo.InvariantCulture)),
                ("currency", currency),
                ("product", product),
                ("store", store),
                ("order_id", System.Guid.NewGuid().ToString())); // 주문번호: 동일 상품 연속 결제가 중복제거(dedup)에 걸리지 않게
            await Talo.Events.Flush(); // 테스트 중 즉시 반영용. 실서비스에선 생략해도 10건마다 자동 flush됨.
        }

        // 광고 노출당 수익 발생 시 호출(미디에이션 SDK의 수익 콜백에서).
        // format: rewarded / interstitial / banner / native,  network: admob / applovin 등
        //
        // ⚠️ currency 는 IAP와 같은 통화로 맞춰 보낼 것. 대시보드는 통화를 환산하지 않아
        //    섞이면 합산 매출이 나오지 않는다. AppLovin MAX 등 미디에이션은 보통 USD로 주므로
        //    게임에서 매출 발생일 환율로 환산한 뒤 넘긴다.
        //
        // adUnit, placement 는 선택. 지금 대시보드가 읽지는 않지만 저장이 스키마리스라
        // 심어두면 나중에 광고 단위별 분석을 소급해서 할 수 있다.
        public async Task LogAdRevenue(
            double amount,
            string currency,
            string format,
            string network,
            string adUnit = "",
            string placement = "")
        {
            await Talo.Events.Track("ad_revenue",
                ("amount", amount.ToString(CultureInfo.InvariantCulture)),
                ("currency", currency),
                ("format", format),
                ("network", network),
                ("ad_unit", adUnit),
                ("placement", placement));
            await Talo.Events.Flush();
        }

        // 스테이지 진입. ※ stage는 순서 비교 위해 숫자로.
        public async Task StageStart(int stage)
        {
            if (PludibaseAcquisition.InitReady)
                PludibaseAcquisition.Track("stage_start", new System.Collections.Generic.Dictionary<string, string>
                {
                    { "stage", stage.ToString(CultureInfo.InvariantCulture) }
                });
            await Talo.Events.Track("stage_start", ("stage", stage.ToString()));
            await Talo.Events.Flush();
        }

        // 스테이지 종료. result: clear / fail / quit.
        public async Task StageEnd(int stage, string result, int durationSec = 0)
        {
            if (PludibaseAcquisition.InitReady)
                PludibaseAcquisition.Track("stage_end", new System.Collections.Generic.Dictionary<string, string>
                {
                    { "stage", stage.ToString(CultureInfo.InvariantCulture) },
                    { "result", result ?? "" }, { "duration_sec", durationSec.ToString(CultureInfo.InvariantCulture) }
                });
            await Talo.Events.Track("stage_end",
                ("stage", stage.ToString()),
                ("result", result),
                ("duration_sec", durationSec.ToString()));
            await Talo.Events.Flush();
        }

        // 튜토리얼 각 단계 도달.
        public async Task TutorialStep(string step, int index)
        {
            await Talo.Events.Track("tutorial_step", ("step", step), ("index", index.ToString()));
            await Talo.Events.Flush();
        }

        // 재화 증감. 보상 지급은 +, 상점/강화 소모는 -.
        public async Task CurrencyChange(string currency, double delta, string reason = "")
        {
            await Talo.Events.Track("currency_change",
                ("currency", currency),
                ("delta", delta.ToString(CultureInfo.InvariantCulture)),
                ("reason", reason));
            await Talo.Events.Flush();
        }
    }
}
