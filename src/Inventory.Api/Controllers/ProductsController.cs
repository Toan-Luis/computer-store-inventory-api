using System.ComponentModel.DataAnnotations;
using Inventory.Application.Products;
using Inventory.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize(Roles = "Admin,Staff")]
public sealed class ProductsController(IProductService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductDto>>> List(
        CancellationToken cancellationToken,
        [FromQuery, StringLength(160)] string? search = null,
        [FromQuery, Range(1, int.MaxValue)] int? categoryId = null,
        [FromQuery, Range(1, 1000000)] int page = 1,
        [FromQuery, Range(1, 100)] int pageSize = 20) =>
        Ok(await service.ListAsync(search, categoryId, page, pageSize, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductDto>> Get([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var product = await service.GetAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await service.CreateAsync(request.Sku, request.Name, request.CategoryId, request.Unit, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> Update([Range(1, int.MaxValue)] int id,
        UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await service.UpdateAsync(id, request.Name, request.CategoryId, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> SetActive([Range(1, int.MaxValue)] int id,
        CategoryStatusRequest request, CancellationToken cancellationToken)
    {
        var product = await service.SetActiveAsync(id, request.IsActive!.Value, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }
}

public sealed record CreateProductRequest(
    [Required, StringLength(Product.MaxSkuLength)] string Sku,
    [Required, StringLength(Product.MaxNameLength)] string Name,
    [Range(1, int.MaxValue)] int CategoryId,
    [Required, StringLength(Product.MaxUnitLength)] string Unit);

public sealed record UpdateProductRequest(
    [Required, StringLength(Product.MaxNameLength)] string Name,
    [Range(1, int.MaxValue)] int CategoryId);
