using Inventory.Application;
using Inventory.Application.Stock;
using Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class StockService(InventoryDbContext context) : IStockService
{
    public async Task<MovementDto> PostAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // All transactions lock product rows in the same order. The predicate
        // and arithmetic run together in SQL so concurrent issues cannot oversell.
        foreach (var line in movement.Lines.OrderBy(line => line.ProductId))
        {
            var query = context.Products.Where(product => product.Id == line.ProductId);
            int changed;
            if (movement.Type == StockMovementType.Receipt)
            {
                changed = await query.Where(product => product.IsActive && product.QuantityOnHand <= int.MaxValue - line.Quantity)
                    .ExecuteUpdateAsync(update => update.SetProperty(product => product.QuantityOnHand,
                        product => product.QuantityOnHand + line.Quantity), cancellationToken);
            }
            else
            {
                changed = await query.Where(product => product.QuantityOnHand >= line.Quantity)
                    .ExecuteUpdateAsync(update => update.SetProperty(product => product.QuantityOnHand,
                        product => product.QuantityOnHand - line.Quantity), cancellationToken);
            }

            if (changed == 0)
            {
                var product = await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (product is null)
                {
                    throw new InventoryException(404, $"Product {line.ProductId} does not exist.");
                }
                if (movement.Type == StockMovementType.Issue)
                {
                    throw new InventoryException(409,
                        $"Product {line.ProductId} has insufficient stock. Available: {product.QuantityOnHand}, requested: {line.Quantity}.");
                }
                if (!product.IsActive)
                {
                    throw new InventoryException(409, $"Product {line.ProductId} is inactive and cannot receive stock.");
                }
                throw new InventoryException(409, $"Receipt would exceed the stock quantity limit for product {line.ProductId}.");
            }
        }

        context.StockMovements.Add(movement);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(movement);
    }

    public async Task<MovementDto?> GetAsync(int id, CancellationToken cancellationToken)
    {
        var movement = await context.StockMovements.AsNoTracking().Include(item => item.Lines)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return movement is null ? null : ToDto(movement);
    }

    public async Task<IReadOnlyList<MovementDto>> ListAsync(StockMovementType? type, int? productId,
        DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.StockMovements.AsNoTracking();
        if (type.HasValue)
        {
            query = query.Where(item => item.Type == type);
        }
        if (productId.HasValue)
        {
            query = query.Where(item => item.Lines.Any(line => line.ProductId == productId));
        }
        if (from.HasValue)
        {
            query = query.Where(item => item.CreatedAt >= from);
        }
        if (to.HasValue)
        {
            query = query.Where(item => item.CreatedAt <= to);
        }

        var movements = await query.OrderByDescending(item => item.CreatedAt).ThenByDescending(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Include(item => item.Lines).ToListAsync(cancellationToken);
        return movements.Select(ToDto).ToArray();
    }

    private static MovementDto ToDto(StockMovement movement) => new(movement.Id,
        movement.Type, movement.CreatedByUserId, movement.CreatedAt,
        movement.Lines.OrderBy(line => line.ProductId).Select(line => new MovementLineDto(line.ProductId, line.Quantity)).ToArray());
}
