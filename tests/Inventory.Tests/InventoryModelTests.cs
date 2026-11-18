using Inventory.Domain;

namespace Inventory.Tests;

public sealed class InventoryModelTests
{
    [Fact]
    public void NewProductStartsWithZeroStockAndNormalizedSku()
    {
        var product = new Product(" kb-001 ", "Bàn phím cơ", 1, "cái");

        Assert.Equal("KB-001", product.Sku);
        Assert.Equal(0, product.QuantityOnHand);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void StockMovementLineRejectsNonPositiveQuantity(int quantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StockMovementLine(1, quantity));
    }

    [Fact]
    public void StockMovementRequiresAtLeastOneLine()
    {
        Assert.Throws<ArgumentException>(() =>
            new StockMovement(StockMovementType.Receipt, "employee-1", DateTimeOffset.UtcNow, []));
    }

    [Fact]
    public void StockMovementRejectsDuplicateProductLines()
    {
        var lines = new[] { new StockMovementLine(1, 4), new StockMovementLine(1, 2) };

        Assert.Throws<ArgumentException>(() =>
            new StockMovement(StockMovementType.Issue, "employee-1", DateTimeOffset.UtcNow, lines));
    }

    [Fact]
    public void StockMovementRecordsMultipleProductsAndNormalizesTimeToUtc()
    {
        var time = new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.FromHours(7));
        var lines = new[] { new StockMovementLine(1, 4), new StockMovementLine(2, 2) };

        var movement = new StockMovement(StockMovementType.Issue, "employee-1", time, lines);
        lines[0] = new StockMovementLine(3, 1);

        Assert.Equal(StockMovementType.Issue, movement.Type);
        Assert.Equal("employee-1", movement.CreatedByUserId);
        Assert.Equal(time, movement.CreatedAt);
        Assert.Equal(TimeSpan.Zero, movement.CreatedAt.Offset);
        Assert.Equal(new[] { 1, 2 }, movement.Lines.Select(line => line.ProductId));
    }
}
