namespace Sellora.OrderService.Domain.Orders;

/// <summary>The four checks every order must pass, in the order they run.</summary>
public enum VerificationStep
{
    RepShopRelationship = 1,
    PriceResolution = 2,
    CreditLimit = 3,
    StockReservation = 4
}
