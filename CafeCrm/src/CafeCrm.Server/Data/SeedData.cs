using CafeCrm.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CafeCrm.Server.Data;

public static class SeedData
{
    public static async Task Run(IServiceProvider services, IWebHostEnvironment env, IConfiguration configuration)
    {
        var db = services.GetRequiredService<CrmDbContext>();
        var users = services.GetRequiredService<UserManager<AppUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new(role));
        if (!env.IsDevelopment()) {
            var email = configuration["Bootstrap:Email"];
            var password = configuration["Bootstrap:Password"];
            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password) && !await users.Users.AnyAsync())
                await AddUser(users, email, password, Roles.Admin);
            return;
        }
        // These accounts exist only in Development. Never copy this password to a deployment.
        var admin = await AddUser(users, "admin@cafe.test", "CafeDemo@123", Roles.Admin);
        var manager = await AddUser(users, "manager@cafe.test", "CafeDemo@123", Roles.Manager);
        await AddUser(users, "staff@cafe.test", "CafeDemo@123", Roles.Staff);
        var customer = await AddUser(users, "customer@cafe.test", "CafeDemo@123", Roles.Customer);
        if (!await db.Customers.AnyAsync(x => x.UserId == customer.Id))
            db.Customers.Add(new() { UserId = customer.Id, FullName = "Khách hàng demo", BirthDate = new(2004,5,20), Phone = "0901234567" });
        if (!await db.Preferences.AnyAsync()) db.Preferences.AddRange(
            new() { Name = "Cà phê" }, new() { Name = "Trà" }, new() { Name = "Bánh ngọt" }, new() { Name = "Ít đường" });
        if (!await db.Categories.AnyAsync()) {
            var category = new Category { Name = "Cà phê" }; var supplier = new Supplier { Name = "Nhà rang demo", Phone = "02812345678" };
            db.Products.AddRange(new() { Name = "Cà phê sữa", Category = category, Supplier = supplier, Price = 35000 },
                new() { Name = "Cold brew", Category = category, Supplier = supplier, Price = 45000 });
        }
        await db.SaveChangesAsync();
    }
    private static async Task<AppUser> AddUser(UserManager<AppUser> users, string email, string password, string role)
    {
        var user = await users.FindByEmailAsync(email);
        if (user != null) return user;
        user = new() { UserName = email, Email = email, EmailConfirmed = true };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(x => x.Description)));
        await users.AddToRoleAsync(user, role); return user;
    }
}
