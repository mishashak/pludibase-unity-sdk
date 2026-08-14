using System;
using System.Text;
using System.Threading.Tasks;

namespace Pludibase
{
    /// <summary>
    /// 플레이어 속성(props) 저장. <c>Talo.Players.Update()</c> 자리에 쓰면 본문이 props만 담긴
    /// 최소 페이로드로 나간다.
    ///
    /// 왜 있나: Talo의 Update()는 <c>JsonUtility.ToJson(Talo.CurrentPlayer)</c> 로 Player 전체를
    /// 직렬화한다. Player → PlayerAlias → player → ... 가 자기 자신을 다시 물어 깊이 제한까지 반복되고,
    /// JsonUtility는 null을 빈 객체로 쓰기 때문에 presence를 null로 만들어도 사라지지 않는다.
    /// 그래서 prop 하나를 쓸 때 본문이 실제 내용의 15배까지 부푼다
    /// (2026-08-14 실제 게임 실측: props+id 약 320자, presence 이하 4,440자로 본문의 93%).
    /// 서버 PATCH 핸들러는 props 외의 필드를 읽지 않으므로 나머지는 전부 버려지는 낭비다.
    ///
    /// 쓰는 법: 지금 쓰시는 SetProp(로컬 값 세팅)은 그대로 두고, 마지막 네트워크 호출만 바꾼다.
    /// <code>
    /// // await Talo.Players.Update();
    /// await PludibasePlayers.SetProps(Talo.CurrentPlayer.id, ("VID", vid));
    /// </code>
    /// 이 호출은 서버만 갱신한다. 로컬 <c>Talo.CurrentPlayer</c> 의 props는 SetProp이 이미 맞춰 둔 값이다.
    /// </summary>
    public static class PludibasePlayers
    {
        /// <param name="playerId">플레이어 ID(<c>Talo.CurrentPlayer.id</c>).</param>
        /// <param name="props">저장할 key/value. value에 null을 주면 그 prop을 삭제한다.</param>
        public static async Task SetProps(string playerId, params (string key, string value)[] props)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new PludibaseException("playerId는 필수입니다. Talo.CurrentPlayer.id를 넘기세요.");
            }
            if (props == null || props.Length == 0)
            {
                return;
            }

            // JsonUtility를 쓰지 않고 직접 만든다. JsonUtility는 null 문자열을 ""로 써버려서
            // "이 prop을 지운다"(value=null)를 표현할 방법이 없다. 애초에 이 클래스가 있는 이유이기도 하다.
            var sb = new StringBuilder("{\"props\":[");
            for (var i = 0; i < props.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append("{\"key\":").Append(Quote(props[i].key)).Append(",\"value\":");
                sb.Append(props[i].value == null ? "null" : Quote(props[i].value));
                sb.Append('}');
            }
            sb.Append("]}");

            await PludibaseHttp.Send("PATCH", "/v1/players/" + Uri.EscapeDataString(playerId), sb.ToString());
        }

        private static string Quote(string value)
        {
            var sb = new StringBuilder("\"", (value?.Length ?? 0) + 2);
            foreach (var c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
