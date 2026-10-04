using System.ComponentModel.DataAnnotations;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CafeCrm.Server.Services;

public sealed record ProductInput([Required, StringLength(150)] string Name, int CategoryId,
    int? SupplierId, [Range(typeof(decimal),"0","9999999999")] decimal Price, bool IsActive);
public sealed record SupplierInput([Required, StringLength(150)] string Name,
    [StringLength(30)] string? Phone, [EmailAddress] string? Email, bool IsActive);
public sealed class AdminService(CrmDbContext db, UserManager<AppUser> users, CustomerService customers)
{
    public async Task<UserRow[]> UserList(string? search)
    {
        var query = users.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Email!.Contains(search));
        var rows = await query.OrderBy(x => x.Email).ToListAsync();
        var result = new List<UserRow>();
        foreach (var row in rows) result.Add(new(row.Id,row.Email!,row.IsDisabled,(await users.GetRolesAsync(row)).ToArray()));
        return result.ToArray();
    }
    public async Task CreateUser(UserCreate input, string actor)
    {
        Rules.Validate(input); Rules.Require(Roles.All.Contains(input.Role), "Vai trò không hợp lệ.");
        if (input.Role == Roles.Customer) { await customers.Create(new(input.Email,input.Email,input.InitialPassword),actor); return; }
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = new AppUser { Email = input.Email.Trim(), UserName = input.Email.Trim() };
        var result = await users.CreateAsync(user,input.InitialPassword);
        Rules.Require(result.Succeeded,string.Join(" ",result.Errors.Select(x => x.Description)));
        Rules.Require((await users.AddToRoleAsync(user,input.Role)).Succeeded,"Không thể cấp vai trò.");
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = "CreateUser", Entity = "AppUser", EntityId = user.Id });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private async Task<AppUser> Find(string id) => await users.FindByIdAsync(id) ?? throw new CrmException(404,"Tài khoản không tồn tại.");
    public async Task Disable(string id, bool disabled, string actor)
    {
        Rules.Require(id != actor,"Không được tự khóa hoặc xóa tài khoản đang dùng.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var user = await Find(id);
        if (disabled && await users.IsInRoleAsync(user,Roles.Admin)) await RequireOtherAdmin(id);
        user.IsDisabled = disabled; await users.UpdateSecurityStampAsync(user);
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = disabled ? "DisableUser" : "EnableUser", Entity = "AppUser", EntityId = id });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task ChangeRole(string id, string role, string actor)
    {
        Rules.Require(id != actor,"Không được tự đổi vai trò của tài khoản đang dùng.");
        Rules.Require(Roles.All.Contains(role),"Vai trò không hợp lệ.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var user = await Find(id); var current = await users.GetRolesAsync(user);
        Rules.Require(!current.Contains(Roles.Customer) && role != Roles.Customer,
            "Không chuyển tài khoản khách hàng thành nhân sự. Hãy tạo tài khoản nhân sự riêng.");
        if (current.Contains(Roles.Admin) && role != Roles.Admin) await RequireOtherAdmin(id);
        Rules.Require((await users.RemoveFromRolesAsync(user,current)).Succeeded,"Không thể thu hồi vai trò.");
        Rules.Require((await users.AddToRoleAsync(user,role)).Succeeded,"Không thể cấp vai trò.");
        await users.UpdateSecurityStampAsync(user);
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = "ChangeRole", Entity = "AppUser", EntityId = id });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private async Task RequireOtherAdmin(string excluded)
    {
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        Rules.Require(admins.Any(x => x.Id != excluded && !x.IsDisabled),"Phải giữ ít nhất một Admin hoạt động.",409);
    }
    public async Task<ProductDto[]> Products(string? search, int? categoryId, int? supplierId,
        decimal? minPrice, decimal? maxPrice, bool? active, string? sort)
    {
        var q = db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.Supplier).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search));
        if (categoryId != null) q = q.Where(x => x.CategoryId == categoryId);
        if (supplierId != null) q = q.Where(x => x.SupplierId == supplierId);
        if (minPrice != null) q = q.Where(x => x.Price >= minPrice);
        if (maxPrice != null) q = q.Where(x => x.Price <= maxPrice);
        if (active != null) q = q.Where(x => x.IsActive == active);
        var rows = await q.ToListAsync();
        IEnumerable<Product> ordered = sort switch { "price" => rows.OrderBy(x => x.Price), "price_desc" => rows.OrderByDescending(x => x.Price), _ => rows.OrderBy(x => x.Name) };
        return ordered.Select(x => new ProductDto(x.Id,x.Name,x.Category.Name,x.Supplier?.Name,x.Price,x.IsActive)).ToArray();
    }
    public async Task SaveProduct(int? id, ProductInput input)
    {
        Rules.Validate(input); Rules.Require(!string.IsNullOrWhiteSpace(input.Name),"Tên sản phẩm không được trống.");
        Rules.Require(await db.Categories.AnyAsync(x => x.Id == input.CategoryId),"Nhóm sản phẩm không hợp lệ.");
        Rules.Require(input.SupplierId == null || await db.Suppliers.AnyAsync(x => x.Id == input.SupplierId),"Nhà cung cấp không hợp lệ.");
        var product = id == null ? new Product() : await db.Products.FindAsync(id) ?? throw new CrmException(404,"Sản phẩm không tồn tại.");
        product.Name = input.Name.Trim(); product.CategoryId = input.CategoryId;
        product.SupplierId = input.SupplierId; product.Price = input.Price; product.IsActive = input.IsActive;
        if (id == null) db.Products.Add(product); await db.SaveChangesAsync();
    }
    public async Task SaveSupplier(int? id, SupplierInput input)
    {
        Rules.Validate(input); Rules.Require(!string.IsNullOrWhiteSpace(input.Name),"Tên nhà cung cấp không được trống.");
        var s = id == null ? new Supplier() : await db.Suppliers.FindAsync(id) ?? throw new CrmException(404,"Nhà cung cấp không tồn tại.");
        s.Name = input.Name.Trim(); s.Phone = input.Phone; s.Email = input.Email; s.IsActive = input.IsActive;
        if (id == null) db.Suppliers.Add(s); await db.SaveChangesAsync();
    }
}
