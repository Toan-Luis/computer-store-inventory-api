namespace Inventory.Domain;

public sealed class Category
{
    private Category() { }

    public Category(string name)
    {
        Rename(name);
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
