using Inventory.Application.Categories;
using Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class CategoryRepository(InventoryDbContext context) : ICategoryRepository
{
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
    {
        return await context.Categories.AsNoTracking()
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<Category?> FindAsync(int id, CancellationToken cancellationToken) =>
        context.Categories.SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    public void Add(Category category) => context.Categories.Add(category);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await context.SaveChangesAsync(cancellationToken);
    }
}
