using Sahada.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Sahada.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSahadaPersistence(this IServiceCollection services, string connectionString)
    {
        var dataSource = BuildDataSource(connectionString);
        services.AddSingleton(dataSource);
        services.AddDbContext<SahadaDbContext>(options => ConfigureDbContext(options, dataSource));
        return services;
    }

    public static NpgsqlDataSource BuildDataSource(string connectionString) => new NpgsqlDataSourceBuilder(connectionString).Build();

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SahadaDbContext>().Database.MigrateAsync(ct);
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, NpgsqlDataSource dataSource) =>
        options
            .UseNpgsql(dataSource, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
