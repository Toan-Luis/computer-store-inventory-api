using System.ComponentModel.DataAnnotations;
using Inventory.Application.Stock;
using Inventory.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/stock-movements")]
[Authorize(Roles = "Admin,Staff")]
public sealed class StockMovementsController(IStockService service, TimeProvider timeProvider) : ControllerBase
{
    [HttpPost("receipts")]
    public Task<ActionResult<MovementDto>> Receipt(MovementRequest request, CancellationToken cancellationToken) =>
        PostAsync(StockMovementType.Receipt, request, cancellationToken);

    [HttpPost("issues")]
    public Task<ActionResult<MovementDto>> Issue(MovementRequest request, CancellationToken cancellationToken) =>
        PostAsync(StockMovementType.Issue, request, cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MovementDto>> Get([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var movement = await service.GetAsync(id, cancellationToken);
        return movement is null ? NotFound() : Ok(movement);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MovementDto>>> List(CancellationToken cancellationToken,
        [FromQuery, EnumDataType(typeof(StockMovementType))] StockMovementType? type = null,
        [FromQuery, Range(1, int.MaxValue)] int? productId = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery, Range(1, 1000000)] int page = 1, [FromQuery, Range(1, 100)] int pageSize = 20)
    {
        if (from > to)
        {
            ModelState.AddModelError(nameof(to), "End time must be on or after start time.");
            return ValidationProblem(ModelState);
        }
        return Ok(await service.ListAsync(type, productId, from, to, page, pageSize, cancellationToken));
    }

    private async Task<ActionResult<MovementDto>> PostAsync(StockMovementType type, MovementRequest request, CancellationToken cancellationToken)
    {
        var movement = new StockMovement(type, User.FindFirst("sub")!.Value, timeProvider.GetUtcNow(),
            request.Lines.Select(line => new StockMovementLine(line.ProductId, line.Quantity)));
        var result = await service.PostAsync(movement, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }
}

public sealed record MovementLineRequest([Range(1, int.MaxValue)] int ProductId, [Range(1, int.MaxValue)] int Quantity);

public sealed record MovementRequest([Required, MinLength(1), MaxLength(100)] List<MovementLineRequest> Lines)
    : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines is not null && Lines.Any(line => line is null))
        {
            yield return new ValidationResult("Movement lines cannot be null.", [nameof(Lines)]);
        }
        else if (Lines is not null && Lines.Select(line => line.ProductId).Distinct().Count() != Lines.Count)
        {
            yield return new ValidationResult("Each product may appear only once per movement.", [nameof(Lines)]);
        }
    }
}
