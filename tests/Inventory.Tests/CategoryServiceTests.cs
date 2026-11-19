using Inventory.Application.Categories;
using Inventory.Domain;

namespace Inventory.Tests;

public sealed class CategoryServiceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task InvalidNameDoesNotReachPersistence(string? name)
    {
        var repository = new TestCategoryRepository();
        var service = new CategoryService(repository);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(name!));

        Assert.Empty(repository.Categories);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task NameLongerThanSqlColumnDoesNotReachPersistence()
    {
        var repository = new TestCategoryRepository();
        var service = new CategoryService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new string('a', 121)));

        Assert.Empty(repository.Categories);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task CreateAcceptsMaximumLengthNameAfterTrimming()
    {
        var repository = new TestCategoryRepository();
        var service = new CategoryService(repository);
        var name = new string('a', 120);

        var created = await service.CreateAsync($"  {name}  ");

        Assert.Equal(name, created.Name);
        Assert.True(created.IsActive);
        Assert.Single(repository.Categories);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task InvalidRenamePreservesTheExistingName()
    {
        var repository = new TestCategoryRepository();
        var category = new Category("Bàn phím");
        repository.Add(category);
        var service = new CategoryService(repository);

        await Assert.ThrowsAsync<ArgumentException>(() => service.RenameAsync(category.Id, new string('a', 121)));

        Assert.Equal("Bàn phím", category.Name);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task DeactivationPreservesCategoryAndAllowsReactivation()
    {
        var repository = new TestCategoryRepository();
        var category = new Category("Chuột");
        repository.Add(category);
        var service = new CategoryService(repository);

        var inactive = await service.SetActiveAsync(category.Id, false);
        var listed = await service.ListAsync();

        Assert.False(inactive!.IsActive);
        Assert.False(Assert.Single(listed).IsActive);
        Assert.Same(category, Assert.Single(repository.Categories));

        var active = await service.SetActiveAsync(category.Id, true);
        Assert.True(active!.IsActive);
    }

    [Fact]
    public async Task MissingCategoryReturnsNullWithoutSaving()
    {
        var repository = new TestCategoryRepository();
        var service = new CategoryService(repository);

        Assert.Null(await service.GetAsync(99));
        Assert.Null(await service.RenameAsync(99, "Màn hình"));
        Assert.Null(await service.SetActiveAsync(99, false));
        Assert.Equal(0, repository.SaveCount);
    }

    // Domain IDs are assigned by SQL Server. This double only supplies an ID
    // so application tests can exercise operations on a persisted category.
    private sealed class TestCategoryRepository : ICategoryRepository
    {
        public List<Category> Categories { get; } = [];
        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Category>>(Categories.ToArray());

        public Task<Category?> FindAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.SingleOrDefault(category => category.Id == id));

        public void Add(Category category)
        {
            typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, Categories.Count + 1);
            Categories.Add(category);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
