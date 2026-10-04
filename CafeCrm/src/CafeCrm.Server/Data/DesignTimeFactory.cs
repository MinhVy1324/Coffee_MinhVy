using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CafeCrm.Server.Data;

// Migrations target SQL Server. SQLite is an independent quick demo created with EnsureCreated.
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args) {
        var configuration = new ConfigurationBuilder().AddJsonFile("appsettings.json",optional:true)
            .AddUserSecrets<DesignTimeFactory>(optional:true).AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("SqlServer") ??
            "Server=(localdb)\\MSSQLLocalDB;Database=CafeCrm;Trusted_Connection=True;TrustServerCertificate=True";
        return new(new DbContextOptionsBuilder<CrmDbContext>().UseSqlServer(connection).Options);
    }
}
