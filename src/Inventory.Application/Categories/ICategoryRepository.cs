using Inventory.Domain;

namespace Inventory.Application.Categories;

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);
    Task<Category?> FindAsync(int id, CancellationToken cancellationToken);
    void Add(Category category);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
