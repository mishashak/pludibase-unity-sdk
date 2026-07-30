using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Pludibase
{
    /// <summary>
    /// 소셜 계정 로그인(구글/애플). 네이티브 로그인 창은 이 SDK가 띄우지 않는다.
    /// 게임이 각 플랫폼 플러그인으로 로그인 창을 띄워 ID 토큰을 얻은 뒤, 그 토큰만 여기로 넘긴다.
    /// 백엔드가 구글/애플 공개키로 토큰을 검증하고 플레이어 별칭(service=google/apple)과 세션을 돌려준다.
    /// (게이밍 신원인 Google Play Games / Apple Game Center와는 다른, 계정 로그인이다.)
    /// </summary>
    public static class PludibaseAuth
    {
        /// <param name="idToken">네이티브 Google Sign-In이 발급한 ID 토큰(JWT).</param>
        public static Task<PludibaseSession> SignInWithGoogle(string idToken)
            => VerifyToken("google", idToken);

        /// <param name="identityToken">Sign in with Apple이 발급한 identity 토큰(JWT).</param>
        public static Task<PludibaseSession> SignInWithApple(string identityToken)
            => VerifyToken("apple", identityToken);

        static async Task<PludibaseSession> VerifyToken(string provider, string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                throw new PludibaseException($"{provider} 토큰이 비어 있습니다.");
            }

            var body = JsonUtility.ToJson(new TokenBody { token = token });
            var text = await PludibaseHttp.Post($"/v1/players/auth/{provider}", body);
            var session = JsonUtility.FromJson<PludibaseSession>(text);

            // TODO(P1): 반환된 sessionToken을 Talo 세션에 연결해, 이후 이벤트/요청이 이 플레이어로 인증되게 한다.
            //   백엔드 auth 응답을 Talo의 로그인 응답과 같은 형태로 맞추고, 여기서 Talo 세션에 주입한다.
            //   (VERIFY-CONTRACT.md 참고. Bootstrap의 게스트 세션을 이 소셜 세션으로 승격.)
            return session;
        }

        [Serializable]
        class TokenBody
        {
            public string token;
        }
    }

    /// <summary>소셜 로그인 성공 시 백엔드가 돌려주는 세션 정보.</summary>
    [Serializable]
    public class PludibaseSession
    {
        public string sessionToken;
        public string aliasId;
        public string playerId;
        public bool isNewPlayer;
    }
}
