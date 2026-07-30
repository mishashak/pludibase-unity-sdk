# 샘플: Unity IAP 결제 검증 연결

Unity IAP(`com.unity.purchasing`)의 구매 성공 콜백에서 pludibase 서버검증을 호출하는 최소 예시입니다.

핵심: Unity IAP의 `ProcessPurchase`에서 구매 토큰을 꺼내 `PludibasePurchases.Verify`로 넘기고, **검증이 끝날 때까지 지급을 미룹니다.** 검증 전에는 `PurchaseProcessingResult.Pending`을 돌려 스토어가 트랜잭션을 유지하게 합니다.

```csharp
using System.Threading.Tasks;
using Pludibase;
using UnityEngine;
using UnityEngine.Purchasing;

public class PurchaseHandler : IStoreListener
{
    // ... 초기화(UnityPurchasing.Initialize)와 상품 등록은 Unity IAP 문서 참고 ...

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args)
    {
        // Google Play는 receipt에서 purchase token을 파싱, App Store는 서명 트랜잭션을 사용.
        // (플랫폼별 파싱은 Unity IAP의 receipt/Product.transactionID 사용.)
        VerifyAsync(args.purchasedProduct);

        // 검증이 비동기라 우선 Pending. 검증 성공 후 아래에서 ConfirmPendingPurchase로 확정.
        return PurchaseProcessingResult.Pending;
    }

    private async void VerifyAsync(Product product)
    {
        string store = Application.platform == RuntimePlatform.Android
            ? Stores.GooglePlay : Stores.AppStore;

        // purchaseToken: 실제로는 receipt(JSON)에서 store별로 꺼낸다. 여기선 개념만.
        string purchaseToken = ExtractToken(product.receipt, store);

        try
        {
            var result = await PludibasePurchases.Verify(store, product.definition.id, purchaseToken);
            if (result.valid)
            {
                GrantItem(product.definition.id);                 // 게임 내 지급
                controller.ConfirmPendingPurchase(product);       // 스토어에 소비 확정
            }
            else
            {
                Debug.LogWarning("[pludibase] 결제 검증 실패, 지급하지 않음");
            }
        }
        catch (PludibaseException e)
        {
            Debug.LogError($"[pludibase] 검증 오류: {e.Message}. 다음 실행에 재시도(Pending 유지).");
        }
    }

    private string ExtractToken(string receipt, string store) { /* store별 파싱 */ return receipt; }
    private void GrantItem(string productId) { /* 지급 */ }

    // IStoreController controller; 는 OnInitialized에서 보관.
    private IStoreController controller;
    public void OnInitialized(IStoreController c, IExtensionProvider e) { controller = c; }
    // 나머지 IStoreListener 멤버 생략.
    public void OnInitializeFailed(InitializationFailureReason error) { }
    public void OnInitializeFailed(InitializationFailureReason error, string message) { }
    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason) { }
}
```

주의:
- 검증 성공 전에는 지급하지 않습니다. 검증 실패/오류면 `Pending`을 유지해 다음 실행에서 재시도합니다.
- 금액/통화의 정본은 `result`(백엔드 검증값)입니다. 클라가 보낸 값이 아닙니다.
