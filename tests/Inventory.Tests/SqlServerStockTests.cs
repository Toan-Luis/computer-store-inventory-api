using Inventory.Application;
using Inventory.Application.Stock;
using Inventory.Domain;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

public sealed class SqlServerStockTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    private async Task<MovementDto> PostAsync(StockMovementType type, params StockMovementLine[] lines)
    {
        await using var context = database.CreateContext();
        return await new StockService(context).PostAsync(
            new StockMovement(type, database.UserId, DateTimeOffset.UtcNow, lines), CancellationToken.None);
    }

    private async Task<int> BalanceAsync(int productId)
    {
        await using var context = database.CreateContext();
        return await context.Products.Where(product => product.Id == productId).Select(product => product.QuantityOnHand).SingleAsync();
    }

    [SqlServerFact]
    public async Task ReceiptAndIssuePersistBalancesActorAndLines()
    {
        var products = await database.CreateProductsAsync(2);
        var receipt = await PostAsync(StockMovementType.Receipt, new StockMovementLine(products[0].Id, 10), new StockMovementLine(products[1].Id, 2));
        var issue = await PostAsync(StockMovementType.Issue, new StockMovementLine(products[0].Id, 4), new StockMovementLine(products[1].Id, 1));
        Assert.Equal(6, await BalanceAsync(products[0].Id));
        Assert.Equal(1, await BalanceAsync(products[1].Id));

        await using var context = database.CreateContext();
        var saved = await new StockService(context).GetAsync(issue.Id, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(database.UserId, saved.CreatedByUserId);
        Assert.Equal(StockMovementType.Issue, saved.Type);
        Assert.Equal(2, saved.Lines.Count);
        Assert.NotEqual(receipt.Id, issue.Id);
    }

    [SqlServerFact]
    public async Task InsufficientStockRollsBackEarlierLinesAndTheWholeDocument()
    {
        var products = await database.CreateProductsAsync(2);
        await PostAsync(StockMovementType.Receipt, new StockMovementLine(products[0].Id, 10), new StockMovementLine(products[1].Id, 2));

        var exception = await Assert.ThrowsAsync<InventoryException>(() => PostAsync(StockMovementType.Issue,
            new StockMovementLine(products[0].Id, 3), new StockMovementLine(products[1].Id, 99)));

        Assert.Equal(409, exception.StatusCode);
        Assert.Contains("insufficient stock", exception.Message);
        Assert.Equal(10, await BalanceAsync(products[0].Id));
        Assert.Equal(2, await BalanceAsync(products[1].Id));
        await using var context = database.CreateContext();
        Assert.False(await context.StockMovements.AnyAsync(movement => movement.Type == StockMovementType.Issue
            && movement.Lines.Any(line => line.ProductId == products[0].Id)));
    }

    [SqlServerFact]
    public async Task MissingProductRollsBackEarlierReceiptLines()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        var exception = await Assert.ThrowsAsync<InventoryException>(() => PostAsync(StockMovementType.Receipt,
            new StockMovementLine(product.Id, 3), new StockMovementLine(int.MaxValue, 1)));
        Assert.Equal(404, exception.StatusCode);
        Assert.Equal(0, await BalanceAsync(product.Id));
        await using var context = database.CreateContext();
        Assert.False(await context.StockMovements.AnyAsync(movement => movement.Lines.Any(line => line.ProductId == product.Id)));
    }

    [SqlServerFact]
    public async Task ConcurrentIssuesAllowOnlyOneSaleWhenStockIsInsufficientForBoth()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        await PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 5));
        var results = await RunConcurrentIssuesAsync([product.Id]);
        Assert.Single(results, status => status == 201);
        Assert.Single(results, status => status == 409);
        Assert.Equal(1, await BalanceAsync(product.Id));
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.StockMovements.CountAsync(movement => movement.Type == StockMovementType.Issue
            && movement.Lines.Any(line => line.ProductId == product.Id)));
    }

    [SqlServerFact]
    public async Task ConcurrentReceiptsPreserveBothIncrements()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        await Task.WhenAll(
            PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 3)),
            PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 4)));
        Assert.Equal(7, await BalanceAsync(product.Id));
    }

    [SqlServerFact]
    public async Task ReversedMultiProductIssuesRemainAtomicUnderConcurrency()
    {
        var products = await database.CreateProductsAsync(2);
        await PostAsync(StockMovementType.Receipt, new StockMovementLine(products[0].Id, 5), new StockMovementLine(products[1].Id, 5));
        var results = await RunConcurrentIssuesAsync(products.Select(product => product.Id).ToArray());
        Assert.Single(results, status => status == 201);
        Assert.Single(results, status => status == 409);
        Assert.Equal(1, await BalanceAsync(products[0].Id));
        Assert.Equal(1, await BalanceAsync(products[1].Id));
    }

    [SqlServerFact]
    public async Task InactiveProductAllowsExistingStockToBeIssuedButRejectsReceipts()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        await PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 5));
        await using var context = database.CreateContext();
        await new ProductService(context).SetActiveAsync(product.Id, false, CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InventoryException>(() => PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 1)));
        Assert.Equal(409, exception.StatusCode);
        Assert.Contains("inactive", exception.Message);
        await PostAsync(StockMovementType.Issue, new StockMovementLine(product.Id, 2));
        Assert.Equal(3, await BalanceAsync(product.Id));
    }

    [SqlServerFact]
    public async Task ReceiptAtIntegerLimitIsRejectedWithoutChangingBalance()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        await PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, int.MaxValue));
        var exception = await Assert.ThrowsAsync<InventoryException>(() => PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 1)));
        Assert.Equal(409, exception.StatusCode);
        Assert.Contains("quantity limit", exception.Message);
        Assert.Equal(int.MaxValue, await BalanceAsync(product.Id));
    }

    [SqlServerFact]
    public async Task EditingMetadataReturnsCurrentStockAndPreservesTheBalance()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        await using var context = database.CreateContext();
        await context.Products.SingleAsync(item => item.Id == product.Id);
        await PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 10));
        var service = new ProductService(context);
        var updated = await service.UpdateAsync(product.Id, "Renamed", database.CategoryId, CancellationToken.None);
        Assert.Equal(10, updated!.QuantityOnHand);

        await PostAsync(StockMovementType.Issue, new StockMovementLine(product.Id, 4));
        var inactive = await service.SetActiveAsync(product.Id, false, CancellationToken.None);
        Assert.Equal(6, inactive!.QuantityOnHand);
        Assert.Equal(6, await BalanceAsync(product.Id));
    }

    [SqlServerFact]
    public async Task HistorySupportsProductTypeDateFiltersAndStablePagination()
    {
        var product = (await database.CreateProductsAsync(1))[0];
        var receipt = await PostAsync(StockMovementType.Receipt, new StockMovementLine(product.Id, 10));
        var issue = await PostAsync(StockMovementType.Issue, new StockMovementLine(product.Id, 4));
        await using var context = database.CreateContext();
        var service = new StockService(context);

        var firstPage = await service.ListAsync(null, product.Id, null, null, 1, 1, CancellationToken.None);
        var secondPage = await service.ListAsync(null, product.Id, null, null, 2, 1, CancellationToken.None);
        Assert.Equal(issue.Id, Assert.Single(firstPage).Id);
        Assert.Equal(receipt.Id, Assert.Single(secondPage).Id);
        var issues = await service.ListAsync(StockMovementType.Issue, product.Id, null, null, 1, 20, CancellationToken.None);
        Assert.Equal(issue.Id, Assert.Single(issues).Id);

        var receiptTime = receipt.CreatedAt.ToOffset(TimeSpan.FromHours(7));
        var atReceipt = await service.ListAsync(null, product.Id, receiptTime, receiptTime, 1, 20, CancellationToken.None);
        Assert.Equal(receipt.Id, Assert.Single(atReceipt).Id);
        Assert.Null(await service.GetAsync(int.MaxValue, CancellationToken.None));
    }

    private async Task<int[]> RunConcurrentIssuesAsync(int[] productIds)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> IssueAsync(IEnumerable<int> ids)
        {
            await ready.Task;
            try
            {
                await PostAsync(StockMovementType.Issue, ids.Select(id => new StockMovementLine(id, 4)).ToArray());
                return 201;
            }
            catch (InventoryException exception)
            {
                return exception.StatusCode;
            }
        }

        var first = IssueAsync(productIds);
        var second = IssueAsync(productIds.Reverse());
        ready.SetResult();
        return await Task.WhenAll(first, second);
    }
}
