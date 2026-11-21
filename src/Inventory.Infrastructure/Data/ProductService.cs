using Inventory.Application;
using Inventory.Application.Products;
using Inventory.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class ProductService(InventoryDbContext context) : IProductService
{
    public async Task<IReadOnlyList<ProductDto>> ListAsync(string? search, int? categoryId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(product => product.Name.Contains(term) || product.Sku.Contains(term));
        }
        if (categoryId.HasValue)
        {
            query = query.Where(product => product.CategoryId == categoryId);
        }
        return await query.OrderBy(product => product.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(product => new ProductDto(product.Id, product.Sku, product.Name, product.CategoryId,
                product.Unit, product.IsActive, product.QuantityOnHand)).ToListAsync(cancellationToken);
    }

    public async Task<ProductDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var product = await context.Products.AsNoTracking().SingleOrDefaultAsync(product => product.Id == id, cancellationToken);
        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(string sku, string name, int categoryId, string unit, CancellationToken cancellationToken)
    {
        await RequireCategoryAsync(categoryId, cancellationToken);
        var product = new Product(sku, name, categoryId, unit);
        context.Products.Add(product);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new InventoryException(409, "This SKU already exists.");
        }
        return ToDto(product);
    }

    public async Task<ProductDto?> UpdateAsync(int id, string name, int categoryId, CancellationToken cancellationToken)
    {
        var product = await context.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }
        await RequireCategoryAsync(categoryId, cancellationToken);
        product.UpdateDetails(name, categoryId);
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(product);
    }

    public async Task<ProductDto?> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken)
    {
        var product = await context.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }
        product.SetActive(isActive);
        await context.SaveChangesAsync(cancellationToken);
        return ToDto(product);
    }

    private async Task RequireCategoryAsync(int categoryId, CancellationToken cancellationToken)
    {
        if (!await context.Categories.AnyAsync(category => category.Id == categoryId, cancellationToken))
        {
            throw new InventoryException(400, "Category does not exist.");
        }
    }

    private static ProductDto ToDto(Product product) => new(product.Id, product.Sku, product.Name,
        product.CategoryId, product.Unit, product.IsActive, product.QuantityOnHand);
}
