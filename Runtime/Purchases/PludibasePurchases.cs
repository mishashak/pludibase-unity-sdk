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
        /// <param name="amount">결제 금액(스토어 상품 가격). 분석용 보고값이며, 구매 유효성은 백엔드가 스토어로 검증한다.</param>
        /// <param name="currency">통화 ISO 코드(예: KRW).</param>
        public static async Task<PurchaseResult> Verify(
            string store,
            string productId,
            string purchaseToken,
            double amount,
            string currency)
        {
            if (string.IsNullOrEmpty(store) || string.IsNullOrEmpty(productId) || string.IsNullOrEmpty(purchaseToken))
            {
                throw new PludibaseException("store, productId, purchaseToken은 모두 필수입니다.");
            }

            var body = JsonUtility.ToJson(new VerifyBody
            {
                store = store,
                productId = productId,
                purchaseToken = purchaseToken,
                amount = amount,
                currency = currency
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
            public double amount;
            public string currency;
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
        // amount가 어디서 온 값인지. store = 스토어가 확정해 준 값(Google Play 카탈로그 가격,
        // App Store 서명 트랜잭션), client = 보낸 값이 그대로 남은 것(Steam, 또는 카탈로그 조회 실패).
        public string amountSource;
        public string store;
        public string status; // verified / consumed / refunded
    }
}
