namespace Inventory.Domain;

public sealed class Product
{
    public const int MaxSkuLength = 64;
    public const int MaxNameLength = 160;
    public const int MaxUnitLength = 32;
    private Product() { }

    public Product(string sku, string name, int categoryId, string unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        if (sku.Trim().Length > MaxSkuLength || unit.Trim().Length > MaxUnitLength)
        {
            throw new ArgumentException("SKU or unit exceeds the allowed length.");
        }

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
        if (name.Trim().Length > MaxNameLength)
        {
            throw new ArgumentException("Product name exceeds the allowed length.", nameof(name));
        }

        Name = name.Trim();
        CategoryId = categoryId;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
