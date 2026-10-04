using System.Security.Claims;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using CafeCrm.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CafeCrm.Server.Controllers;

[ApiController, Route("api/admin"), Authorize(AuthenticationSchemes = "Identity.Bearer", Roles = Roles.Admin)]
public sealed class AdminApiController(AdminService admin, CrmDbContext db) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet("users")] public Task<UserRow[]> Users(string? search) => admin.UserList(search);
    [HttpPost("users")] public async Task<IActionResult> Create(UserCreate request) { await admin.CreateUser(request,UserId); return StatusCode(201); }
    [HttpPut("users/{id}/role")] public async Task<IActionResult> Role(string id, RoleChange request) { await admin.ChangeRole(id,request.Role,UserId); return NoContent(); }
    [HttpPost("users/{id}/lock")] public async Task<IActionResult> Lock(string id, [FromBody] bool disabled) { await admin.Disable(id,disabled,UserId); return NoContent(); }
    [HttpDelete("users/{id}")] public async Task<IActionResult> Delete(string id) { await admin.Disable(id,true,UserId); return NoContent(); }
    [HttpGet("products")] public Task<ProductDto[]> Products(string? search,int? categoryId,int? supplierId,
        decimal? minPrice,decimal? maxPrice,bool? active,string? sort) => admin.Products(search,categoryId,supplierId,minPrice,maxPrice,active,sort);
    [HttpPost("products")] public async Task<IActionResult> AddProduct(ProductInput request) { await admin.SaveProduct(null,request); return StatusCode(201); }
    [HttpPut("products/{id:int}")] public async Task<IActionResult> Product(int id, ProductInput request) { await admin.SaveProduct(id,request); return NoContent(); }
    [HttpGet("suppliers")] public Task<Supplier[]> Suppliers(string? search,bool? active,string? sort) {
        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || (x.Email != null && x.Email.Contains(search)) || (x.Phone != null && x.Phone.Contains(search)));
        if (active != null) q = q.Where(x => x.IsActive == active);
        q = sort == "name_desc" ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name);
        return q.ToArrayAsync();
    }
    [HttpPost("suppliers")] public async Task<IActionResult> AddSupplier(SupplierInput request) { await admin.SaveSupplier(null,request); return StatusCode(201); }
    [HttpPut("suppliers/{id:int}")] public async Task<IActionResult> Supplier(int id, SupplierInput request) { await admin.SaveSupplier(id,request); return NoContent(); }
}
