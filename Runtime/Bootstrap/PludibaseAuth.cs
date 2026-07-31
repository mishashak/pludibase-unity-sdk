using System.Threading.Tasks;
using TaloGameServices;

namespace Pludibase
{
    /// <summary>
    /// 소셜 계정 로그인(구글/애플). 백엔드는 Talo의 identify 흐름에 google/apple 서비스를 얹어
    /// 처리하므로(별도 로그인 엔드포인트 없음), 이 헬퍼는 Talo.Players.Identify를 그 서비스로 감싼
    /// 얇은 래퍼다. 게임은 네이티브 플러그인으로 ID 토큰을 얻어 넘긴다. 로그인 후 Talo.CurrentAlias로 접근.
    /// (네이티브 로그인 창은 각 플러그인이 띄운다. 이 SDK는 토큰 전달만.)
    /// </summary>
    public static class PludibaseAuth
    {
        // idToken = 네이티브 Google Sign-In이 발급한 ID 토큰(JWT).
        public static Task SignInWithGoogle(string idToken)
        {
            return Talo.Players.Identify("google", idToken);
        }

        // identityToken = Sign in with Apple이 발급한 identity 토큰(JWT).
        public static Task SignInWithApple(string identityToken)
        {
            return Talo.Players.Identify("apple", identityToken);
        }
    }
}
