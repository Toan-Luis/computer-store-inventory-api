namespace Inventory.Application.Products;

public sealed record ProductDto(int Id, string Sku, string Name, int CategoryId, string Unit, bool IsActive, int QuantityOnHand);

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> ListAsync(string? search, int? categoryId, int page, int pageSize, CancellationToken cancellationToken);
    Task<ProductDto?> GetAsync(int id, CancellationToken cancellationToken);
    Task<ProductDto> CreateAsync(string sku, string name, int categoryId, string unit, CancellationToken cancellationToken);
    Task<ProductDto?> UpdateAsync(int id, string name, int categoryId, CancellationToken cancellationToken);
    Task<ProductDto?> SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken);
}
