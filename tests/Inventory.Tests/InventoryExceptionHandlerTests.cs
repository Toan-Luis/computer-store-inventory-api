using Inventory.Api;
using Inventory.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Tests;

public sealed class InventoryExceptionHandlerTests
{
    [Fact]
    public async Task BusinessConflictRemainsHandledWhenAcceptHeaderHasNoProblemDetailsWriter()
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers.Accept = "application/xml";
        context.Response.Body = new MemoryStream();
        var handler = new InventoryExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(context, new InventoryException(409, "Insufficient stock."), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(409, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("Insufficient stock.", body);
    }

    [Fact]
    public async Task UnexpectedFailureIsLeftToTheDefaultExceptionHandler()
    {
        using var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var handler = new InventoryExceptionHandler(services.GetRequiredService<IProblemDetailsService>());
        Assert.False(await handler.TryHandleAsync(new DefaultHttpContext(), new InvalidOperationException(), CancellationToken.None));
    }
}
