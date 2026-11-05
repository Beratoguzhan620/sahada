using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Sahada.Infrastructure.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; generating migrations does not open a connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SahadaDbContext>
{
    public SahadaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SahadaDbContext>();
        DependencyInjection.ConfigureDbContext(options, DependencyInjection.BuildDataSource("Host=localhost;Database=sahada_design"));
        return new SahadaDbContext(options.Options);
    }
}
