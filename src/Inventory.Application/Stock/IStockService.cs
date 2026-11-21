using Inventory.Domain;

namespace Inventory.Application.Stock;

public sealed record MovementLineDto(int ProductId, int Quantity);
public sealed record MovementDto(int Id, StockMovementType Type, string CreatedByUserId,
    DateTimeOffset CreatedAt, IReadOnlyList<MovementLineDto> Lines);

public interface IStockService
{
    Task<MovementDto> PostAsync(StockMovement movement, CancellationToken cancellationToken);
    Task<MovementDto?> GetAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<MovementDto>> ListAsync(StockMovementType? type, int? productId,
        DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken);
}
