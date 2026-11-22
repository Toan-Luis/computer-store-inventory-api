using Inventory.Application;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api;

public sealed class InventoryExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not InventoryException inventoryException)
        {
            return false;
        }
        context.Response.StatusCode = inventoryException.StatusCode;
        var problem = new ProblemDetails
        {
            Status = inventoryException.StatusCode,
            Title = inventoryException.Message
        };
        if (!await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem
        }))
        {
            await context.Response.WriteAsJsonAsync(problem, cancellationToken);
        }
        return true;
    }
}
