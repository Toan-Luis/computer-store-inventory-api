using Inventory.Domain;

namespace Inventory.Application.Categories;

public sealed class CategoryService(ICategoryRepository repository)
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var categories = await repository.ListAsync(cancellationToken);
        return categories.Select(ToDto).ToArray();
    }

    public async Task<CategoryDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var category = await repository.FindAsync(id, cancellationToken);
        return category is null ? null : ToDto(category);
    }

    public async Task<CategoryDto> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var category = new Category(name);
        repository.Add(category);
        await repository.SaveChangesAsync(cancellationToken);
        return ToDto(category);
    }

    public async Task<CategoryDto?> RenameAsync(int id, string name, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var category = await repository.FindAsync(id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        category.Rename(name);
        await repository.SaveChangesAsync(cancellationToken);
        return ToDto(category);
    }

    public async Task<CategoryDto?> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        var category = await repository.FindAsync(id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        category.SetActive(isActive);
        await repository.SaveChangesAsync(cancellationToken);
        return ToDto(category);
    }

    private static CategoryDto ToDto(Category category) => new(category.Id, category.Name, category.IsActive);
}
