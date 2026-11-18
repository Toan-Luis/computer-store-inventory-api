namespace Inventory.Domain;

public sealed class StockMovementLine
{
    private StockMovementLine() { }

    public StockMovementLine(int productId, int quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        ProductId = productId;
        Quantity = quantity;
    }

    public int Id { get; private set; }
    public int StockMovementId { get; private set; }
    public int ProductId { get; private set; }
    public int Quantity { get; private set; }
}
