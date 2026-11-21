using System.ComponentModel.DataAnnotations;
using Inventory.Application.Categories;
using Inventory.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("api/categories")]
[Authorize(Roles = "Admin,Staff")]
public sealed class CategoriesController(CategoryService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryDto>> Get([Range(1, int.MaxValue)] int id, CancellationToken cancellationToken)
    {
        var category = await service.GetAsync(id, cancellationToken);
        return category is null ? NotFound() : Ok(category);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> Create(CategoryNameRequest request, CancellationToken cancellationToken)
    {
        var category = await service.CreateAsync(request.Name, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> Rename(
        [Range(1, int.MaxValue)] int id, CategoryNameRequest request, CancellationToken cancellationToken)
    {
        var category = await service.RenameAsync(id, request.Name, cancellationToken);
        return category is null ? NotFound() : Ok(category);
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> SetActive(
        [Range(1, int.MaxValue)] int id, CategoryStatusRequest request, CancellationToken cancellationToken)
    {
        var category = await service.SetActiveAsync(id, request.IsActive!.Value, cancellationToken);
        return category is null ? NotFound() : Ok(category);
    }
}

public sealed record CategoryNameRequest(
    [Required, StringLength(Category.MaxNameLength)] string Name);

public sealed record CategoryStatusRequest([Required] bool? IsActive);
