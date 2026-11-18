namespace Inventory.Domain;

public sealed class Product
{
    private Product() { }

    public Product(string sku, string name, int categoryId, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);

        Sku = sku.Trim().ToUpperInvariant();
        Unit = unit.Trim();
        UpdateDetails(name, categoryId);
    }

    public int Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int CategoryId { get; private set; }
    public string Unit { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public int QuantityOnHand { get; private set; }

    public void UpdateDetails(string name, int categoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(categoryId);

        Name = name.Trim();
        CategoryId = categoryId;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
