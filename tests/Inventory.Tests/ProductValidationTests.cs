using Inventory.Domain;

namespace Inventory.Tests;

public sealed class ProductValidationTests
{
    [Fact]
    public void RejectedRenamePreservesExistingDetails()
    {
        var product = new Product("KB-1", "Keyboard", 1, "cái");
        Assert.Throws<ArgumentException>(() => product.UpdateDetails(new string('x', 161), 2));
        Assert.Equal("Keyboard", product.Name);
        Assert.Equal(1, product.CategoryId);
    }

    [Theory]
    [InlineData(65, 3)]
    [InlineData(3, 33)]
    public void ProductRejectsLengthsThatWouldFailSqlPersistence(int skuLength, int unitLength)
    {
        Assert.Throws<ArgumentException>(() => new Product(new string('x', skuLength), "Keyboard", 1, new string('x', unitLength)));
    }

}
