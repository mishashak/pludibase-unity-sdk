using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Pludibase
{
    /// <summary>
    /// 인앱결제(IAP) 서버검증. 결제 창은 Unity IAP(또는 스토어 플러그인)가 띄운다.
    /// 게임이 구매 성공 콜백에서 받은 구매 토큰/영수증을 여기로 넘기면, 백엔드가
    /// 스토어(Google Play / App Store)에 직접 검증하고 위조·재사용을 막은 뒤 매출을 기록하고
    /// purchase 이벤트를 발행한다(대시보드 매출/LTV 자동 반영).
    /// 클라이언트가 준 값은 신뢰하지 않는다. 금액/통화의 정본은 백엔드 검증 결과다.
    /// </summary>
    public static class PludibasePurchases
    {
        /// <param name="store">Stores.GooglePlay / Stores.AppStore.</param>
        /// <param name="productId">스토어에 등록한 상품 ID.</param>
        /// <param name="purchaseToken">Google Play는 purchase token, App Store는 서명된 트랜잭션/영수증.</param>
        public static async Task<PurchaseResult> Verify(string store, string productId, string purchaseToken)
        {
            if (string.IsNullOrEmpty(store) || string.IsNullOrEmpty(productId) || string.IsNullOrEmpty(purchaseToken))
            {
                throw new PludibaseException("store, productId, purchaseToken은 모두 필수입니다.");
            }

            var body = JsonUtility.ToJson(new VerifyBody
            {
                store = store,
                productId = productId,
                purchaseToken = purchaseToken
            });

            var text = await PludibaseHttp.Post("/v1/purchases/verify", body);
            return JsonUtility.FromJson<PurchaseResult>(text);
        }

        [Serializable]
        class VerifyBody
        {
            public string store;
            public string productId;
            public string purchaseToken;
        }
    }

    /// <summary>서버검증 결과. valid=false면 결제를 지급하지 않는다.</summary>
    [Serializable]
    public class PurchaseResult
    {
        public bool valid;
        public string transactionId;
        public string product;
        public double amount;
        public string currency;
        public string store;
        public string status; // verified / consumed / refunded
    }
}
