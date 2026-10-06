using System.Security.Claims;
using System.Threading.RateLimiting;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using CafeCrm.Server.Services;
using CafeCrm.Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

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
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<CrmDbContext>().AddErrorDescriber<VietnameseIdentityErrors>();
builder.Services.Configure<BearerTokenOptions>(IdentityConstants.BearerScheme, o => {
    o.BearerTokenExpiration = TimeSpan.FromMinutes(30);
    o.RefreshTokenExpiration = TimeSpan.FromDays(7);
});
builder.Services.ConfigureApplicationCookie(o => {
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews(options => {
    // Lỗi chuyển chuỗi form sang ngày/số xuất hiện trước service, cũng cần thông báo dễ đọc.
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((_,_) => "Thông tin không đúng định dạng. Kiểm tra ngày sinh và các trường số.");
    options.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Thông tin không đúng định dạng. Kiểm tra ngày sinh và các trường số.");
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(_ => "Vui lòng nhập giá trị là số.");
    options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_ => "Vui lòng nhập thông tin bắt buộc.");
});
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<FeedbackService>();
builder.Services.AddScoped<SurveyService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    // API và form web có ngân sách riêng; GET trang đăng nhập không bị tính lượt.
    o.OnRejected = async (context, cancellation) => {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        const string message = "Bạn thao tác quá nhanh. Vui lòng chờ một phút rồi thử lại.";
        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
            await context.HttpContext.Response.WriteAsJsonAsync(new { title = message, status = 429 }, cancellation);
        else {
            context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
            await context.HttpContext.Response.WriteAsync(message, cancellation);
        }
    };
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        (context.Request.Path.StartsWithSegments("/api") ? "api:" : "web:") + (context.Connection.RemoteIpAddress?.ToString() ?? "local"), _ => new() {
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
            // Trả trang Razor cùng giao diện và giữ mã HTTP (404/409...), không redirect thành 200.
            // Message là lỗi nghiệp vụ đã kiểm soát; không đưa SQL/stack trace ra trình duyệt.
            var actionContext = new ActionContext(context,new RouteData(),new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
            var view = new ViewResult {
                ViewName = "~/Views/Shared/BusinessError.cshtml", StatusCode = status,
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(),new ModelStateDictionary()) {
                    Model = new BusinessErrorViewModel(status,message) }
            };
            await context.RequestServices.GetRequiredService<IActionResultExecutor<ViewResult>>().ExecuteAsync(actionContext,view);
        }
    }
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication();
// Khóa/xóa khách hoặc đổi quyền sẽ đổi SecurityStamp trong service.
// Kiểm tra mỗi request để phiên web và token app bị thu hồi ngay, không đợi hết hạn.
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
