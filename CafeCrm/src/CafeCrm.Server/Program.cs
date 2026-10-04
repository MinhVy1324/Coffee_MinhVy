using System.Security.Claims;
using System.Threading.RateLimiting;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using CafeCrm.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CrmDbContext>(o => {
    if (builder.Configuration["DatabaseProvider"] == "SqlServer")
        o.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer"));
    else o.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
});
builder.Services.AddIdentityApiEndpoints<AppUser>(o => {
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 8;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<CrmDbContext>();
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o => {
    o.BearerTokenExpiration = TimeSpan.FromMinutes(30);
    o.RefreshTokenExpiration = TimeSpan.FromDays(7);
});
builder.Services.ConfigureApplicationCookie(o => {
    o.LoginPath = "/Account/Login";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<FeedbackService>();
builder.Services.AddScoped<SurveyService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new() {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) => {
    try { await next(context); }
    catch (Exception ex) when (ex is CrmException or DbUpdateException) {
        if (context.Response.HasStarted) throw;
        var status = ex is CrmException crm ? crm.Status : 409;
        var message = ex is CrmException ce ? ce.Message : "Dữ liệu trùng hoặc đã thay đổi. Hãy tải lại và thử lại.";
        if (context.Request.Path.StartsWithSegments("/api")) {
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { title = message, status });
        } else {
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($"<h2>{System.Net.WebUtility.HtmlEncode(message)}</h2><a href='javascript:history.back()'>Quay lại</a>");
        }
    }
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication();
// Verify the current user and security stamp for every request, including already issued mobile tokens.
app.Use(async (context, next) => {
    if (context.User.Identity?.IsAuthenticated == true) {
        var manager = context.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var user = await manager.GetUserAsync(context.User);
        var stamp = context.User.FindFirstValue(manager.Options.ClaimsIdentity.SecurityStampClaimType);
        if (user == null || user.IsDisabled || stamp != user.SecurityStamp) {
            if (context.Request.Path.StartsWithSegments("/api")) {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { title = "Phiên đã hết hiệu lực hoặc tài khoản bị khóa.", status = 401 });
            } else {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Redirect("/Account/Login");
            }
            return;
        }
    }
    await next(context);
});
app.UseAuthorization();
app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
using (var scope = app.Services.CreateScope()) {
    var db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
    // SQLite demo is self-contained. SQL Server uses explicit EF migrations from README.
    if (builder.Configuration["DatabaseProvider"] != "SqlServer") await db.Database.EnsureCreatedAsync();
    await SeedData.Run(scope.ServiceProvider, app.Environment, builder.Configuration);
    if (args.Contains("--schema")) {
        await File.WriteAllTextAsync("schema.generated.sql", db.Database.GenerateCreateScript());
        return;
    }
}
app.Run();
public partial class Program { }
