using Inventory.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Data;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(item => item.Name).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable(table => table.HasCheckConstraint(
                "CK_Products_QuantityOnHand", "[QuantityOnHand] >= 0"));
            product.Property(item => item.Sku).HasMaxLength(64).IsRequired();
            product.Property(item => item.Name).HasMaxLength(160).IsRequired();
            product.Property(item => item.Unit).HasMaxLength(32).IsRequired();
            product.HasIndex(item => item.Sku).IsUnique();
            product.HasOne<Category>().WithMany()
                .HasForeignKey(item => item.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.ToTable(table => table.HasCheckConstraint(
                "CK_StockMovements_Type", "[Type] IN (1, 2)"));
            movement.HasIndex(item => item.CreatedAt);
            movement.HasOne<IdentityUser>().WithMany()
                .HasForeignKey(item => item.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            movement.HasMany(item => item.Lines).WithOne()
                .HasForeignKey(line => line.StockMovementId)
                .OnDelete(DeleteBehavior.Restrict);
            movement.Navigation(item => item.Lines).HasField("_lines")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<StockMovementLine>(line =>
        {
            line.ToTable("StockMovementLines", table => table.HasCheckConstraint(
                "CK_StockMovementLines_Quantity", "[Quantity] > 0"));
            line.HasIndex(item => new { item.StockMovementId, item.ProductId }).IsUnique();
            line.HasOne<Product>().WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
