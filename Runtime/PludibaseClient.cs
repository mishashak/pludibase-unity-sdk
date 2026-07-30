using System;

namespace Pludibase
{
    /// <summary>
    /// SDK 전역 설정. apiUrl과 accessKey는 Talo Settings 애셋에 넣는 값과 동일하게 준다.
    /// (같은 pludibase 백엔드를 가리키므로.) 게임 시작 시 한 번 Configure를 호출한다.
    /// P3에서 Talo Settings에서 자동으로 읽어오도록 개선 예정.
    /// </summary>
    public static class PludibaseClient
    {
        public static string ApiUrl { get; private set; } = "";
        public static string AccessKey { get; private set; } = "";

        public static bool IsConfigured => !string.IsNullOrEmpty(ApiUrl) && !string.IsNullOrEmpty(AccessKey);

        /// <param name="apiUrl">백엔드 API 주소. 예: https://api.example.com (끝 슬래시 무관)</param>
        /// <param name="accessKey">게임 전용 access key (Talo Settings와 동일).</param>
        public static void Configure(string apiUrl, string accessKey)
        {
            ApiUrl = apiUrl ?? "";
            AccessKey = accessKey ?? "";
        }
    }

    /// <summary>스토어 식별자. purchase 이벤트의 store 속성과 동일하게 맞춘다.</summary>
    public static class Stores
    {
        public const string GooglePlay = "google_play";
        public const string AppStore = "app_store";
        public const string Steam = "steam";
    }

    /// <summary>SDK 호출 실패 시 던지는 예외.</summary>
    public class PludibaseException : Exception
    {
        public long StatusCode { get; }

        public PludibaseException(string message, long statusCode = 0) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
