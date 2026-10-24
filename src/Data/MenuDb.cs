using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MenuToOrder.Data;

public sealed class MenuDb(DbContextOptions<MenuDb> options) : DbContext(options)
{
    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<MenuVersion> MenuVersions => Set<MenuVersion>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<ExtractionRun> ExtractionRuns => Set<ExtractionRun>();
    public DbSet<Flag> Flags => Set<Flag>();
    public DbSet<Correction> Corrections => Set<Correction>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<decimal>().HavePrecision(10, 2);
        builder.Properties<Enum>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Restaurant>(r =>
        {
            r.HasIndex(x => x.Slug).IsUnique();
            r.HasIndex(x => x.OwnerTokenHash).IsUnique();
        });

        model.Entity<MenuVersion>().HasOne<Restaurant>().WithMany().HasForeignKey(v => v.RestaurantId);

        // The audit trail outlives the rows it describes, so deleting those must not erase it.
        model.Entity<ExtractionRun>().HasOne<MenuVersion>().WithMany().HasForeignKey(r => r.MenuVersionId)
            .OnDelete(DeleteBehavior.Restrict);
        model.Entity<Correction>().HasOne<MenuItem>().WithMany().HasForeignKey(c => c.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

// Used only by `dotnet ef`. The connection matches compose.yaml.
internal sealed class DesignTimeMenuDbFactory : IDesignTimeDbContextFactory<MenuDb>
{
    public MenuDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MenuDb>()
            .UseNpgsql("Host=localhost;Database=menu;Username=menu;Password=menu")
            .Options);
}
