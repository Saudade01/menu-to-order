using MenuToOrder.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(MenuToOrder.Tests.PostgresFixture))]

namespace MenuToOrder.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public MenuDb CreateDb() =>
        new(new DbContextOptionsBuilder<MenuDb>().UseNpgsql(_container.GetConnectionString()).Options);

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDb();
        // The real migrations, not EnsureCreated, so a broken migration fails the suite.
        await db.Database.MigrateAsync();
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
