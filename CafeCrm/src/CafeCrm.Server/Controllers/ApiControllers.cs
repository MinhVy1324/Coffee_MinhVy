using System.Security.Claims;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using CafeCrm.Server.Services;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CafeCrm.Server.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("login")]
public sealed class AuthApiController(UserManager<AppUser> users, SignInManager<AppUser> signIn,
    CustomerService customers, IOptionsMonitor<BearerTokenOptions> tokenOptions) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request) { await customers.Register(request); return StatusCode(201); }
    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user == null || user.IsDisabled || !(await signIn.CheckPasswordSignInAsync(user, request.Password, true)).Succeeded)
            return Unauthorized(new { title = "Email, mật khẩu không đúng hoặc tài khoản bị khóa." });
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(user, false); return new EmptyResult();
    }
    [HttpPost("refresh"), AllowAnonymous]
    public async Task<IActionResult> Refresh(RefreshRequest request)
    {
        var ticket = tokenOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || expires < DateTimeOffset.UtcNow) return Unauthorized();
        var user = await signIn.ValidateSecurityStampAsync(ticket.Principal);
        if (user == null || user.IsDisabled) return Unauthorized();
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(user, false); return new EmptyResult();
    }
    [HttpPost("logout-all"), Authorize(AuthenticationSchemes = "Identity.Bearer")]
    public async Task<IActionResult> LogoutAll()
    { var user = await users.GetUserAsync(User); await users.UpdateSecurityStampAsync(user!); return NoContent(); }
}
[ApiController, Route("api/customer"), Authorize(AuthenticationSchemes = "Identity.Bearer", Roles = Roles.Customer)]
public sealed class CustomerApiController(CustomerService customers, FeedbackService feedback,
    SurveyService surveys, CrmDbContext db) : ControllerBase
{
    // Cùng nguyên tắc với PortalController: token quyết định hồ sơ, không cho client chọn khách khác.
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet("profile")] public Task<ProfileDto> Profile() => customers.Profile(UserId);
    [HttpPut("profile")] public async Task<IActionResult> Update(ProfileUpdate request)
    { var c = await customers.ByUser(UserId); await customers.Update(c.Id, request, UserId); return NoContent(); }
    [HttpGet("preferences")] public Task<LookupDto[]> Preferences() => db.Preferences.Select(x => new LookupDto(x.Id,x.Name)).ToArrayAsync();
    [HttpGet("products")] public Task<ProductDto[]> Products() => db.Products.Where(x => x.IsActive)
        .Select(x => new ProductDto(x.Id, x.Name,x.Category.Name,x.Supplier == null ? null : x.Supplier.Name,x.Price,x.IsActive)).ToArrayAsync();
    [HttpGet("feedback")] public async Task<FeedbackDto[]> Feedback() => await feedback.List((await customers.ByUser(UserId)).Id);
    [HttpPost("feedback")] public async Task<IActionResult> CreateFeedback(FeedbackCreate request) => Ok(new { id = await feedback.Create((await customers.ByUser(UserId)).Id, request) });
    [HttpGet("surveys")] public async Task<InvitationDto[]> Inbox() => await surveys.Inbox((await customers.ByUser(UserId)).Id);
    [HttpGet("surveys/{id:guid}")] public async Task<SurveyDto> Survey(Guid id) => await surveys.ForCustomer(id,(await customers.ByUser(UserId)).Id);
    [HttpGet("surveys/{id:guid}/response")] public async Task<SurveyResponseDto?> SurveyResponse(Guid id) => await surveys.ResponseForCustomer(id,(await customers.ByUser(UserId)).Id);
    [HttpPost("surveys/{id:guid}/responses")] public async Task<IActionResult> Submit(Guid id, SubmitResponse request) => Ok(new { id = await surveys.Submit(id,(await customers.ByUser(UserId)).Id,request) });
}
// API dùng chung quy tắc với web: Staff và Manager tác nghiệp; khóa/xóa/báo cáo/khảo sát chỉ Manager.
// Bearer token dành cho client/app. Cookie đăng nhập web không tự cấp quyền gọi các API này.
[ApiController, Route("api/crm"), Authorize(AuthenticationSchemes = "Identity.Bearer", Roles = Roles.Manager + "," + Roles.Staff)]
public sealed class CrmApiController(CustomerService customers, FeedbackService feedback,
    SurveyService surveys, ReportService reports) : ControllerBase
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet("customers")] public Task<CustomerRow[]> Customers(string? search, bool archived = false) => customers.List(search,archived);
    [HttpPost("customers")] public async Task<IActionResult> Create(CustomerCreate request) => Ok(new { id = await customers.Create(request,UserId) });
    [HttpPut("customers/{id:guid}")] public async Task<IActionResult> Update(Guid id, ProfileUpdate request) { await customers.Update(id,request,UserId); return NoContent(); }
    [HttpPost("customers/{id:guid}/lock"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Lock(Guid id, [FromBody] bool disabled) { await customers.SetDisabled(id,disabled,UserId); return NoContent(); }
    [HttpDelete("customers/{id:guid}"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid version) { await customers.Archive(id,version,UserId); return NoContent(); }
    [HttpGet("feedback")] public Task<FeedbackDto[]> Feedback(FeedbackStatus? status) => feedback.List(status:status);
    [HttpPost("feedback/{id:guid}/replies")] public async Task<IActionResult> Reply(Guid id, FeedbackReply request) { await feedback.Reply(id,request,UserId); return NoContent(); }
    [HttpGet("surveys"), Authorize(Roles = Roles.Manager)] public Task<SurveyDto[]> Surveys() => surveys.List();
    [HttpPost("surveys"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> CreateSurvey(SurveyDraft request) => Ok(new { id = await surveys.Create(request,UserId) });
    [HttpPut("surveys/{id:guid}"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> EditSurvey(Guid id, SurveyUpdate request) { await surveys.Update(id,request); return NoContent(); }
    [HttpDelete("surveys/{id:guid}"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> DeleteSurvey(Guid id, [FromQuery] Guid version) { await surveys.DeleteDraft(id,version); return NoContent(); }
    [HttpPost("surveys/{id:guid}/publish"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Publish(Guid id, PublishRequest request) => Ok(new { sent = await surveys.Publish(id,request) });
    [HttpPost("surveys/{id:guid}/close"), Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Close(Guid id, [FromBody] Guid version) { await surveys.Close(id,version); return NoContent(); }
    [HttpGet("surveys/{id:guid}/results"), Authorize(Roles = Roles.Manager)] public Task<SurveyResults> Results(Guid id) => surveys.Results(id);
    [HttpGet("reports"), Authorize(Roles = Roles.Manager)] public Task<ReportDto> Reports() => reports.Overview();
}
