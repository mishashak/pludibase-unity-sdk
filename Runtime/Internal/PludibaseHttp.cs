using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Pludibase
{
    /// <summary>
    /// pludibase 백엔드로 JSON POST를 보내는 내부 헬퍼.
    /// UnityWebRequest를 async/await로 감싸(SendWebRequest는 자체로 awaitable이 아니므로 completed 콜백을 TaskCompletionSource로 브리지).
    /// </summary>
    internal static class PludibaseHttp
    {
        internal static async Task<string> Post(string path, string json)
        {
            if (!PludibaseClient.IsConfigured)
            {
                throw new PludibaseException(
                    "PludibaseClient.Configure(apiUrl, accessKey)를 먼저 호출하세요.");
            }

            var url = PludibaseClient.ApiUrl.TrimEnd('/') + path;

            using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(PludibaseClient.AccessKey))
            {
                req.SetRequestHeader("Authorization", "Bearer " + PludibaseClient.AccessKey);
            }

            var op = req.SendWebRequest();
            var tcs = new TaskCompletionSource<bool>();
            op.completed += _ => tcs.TrySetResult(true);
            await tcs.Task;

            if (req.result != UnityWebRequest.Result.Success)
            {
                var detail = req.downloadHandler != null ? req.downloadHandler.text : "";
                throw new PludibaseException(
                    $"요청 실패 [{req.responseCode}] {path}: {req.error} {detail}", req.responseCode);
            }

            return req.downloadHandler.text;
        }
    }
}
