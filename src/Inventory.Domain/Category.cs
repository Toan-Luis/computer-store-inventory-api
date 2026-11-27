namespace Inventory.Domain;

public sealed class Category
{
    public const int MaxNameLength = 120;

    private Category() { }

    public Category(string name) => Rename(name);

    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException($"Category name must have at most {MaxNameLength} characters.", nameof(name));
        }

        Name = trimmedName;
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
