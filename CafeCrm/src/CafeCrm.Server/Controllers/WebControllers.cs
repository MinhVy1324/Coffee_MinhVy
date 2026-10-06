using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using CafeCrm.Server.Services;
using CafeCrm.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace CafeCrm.Server.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() {
        if (User.IsInRole(Roles.Admin)) return RedirectToAction("Index","Admin");
        if (User.IsInRole(Roles.Manager) || User.IsInRole(Roles.Staff)) return RedirectToAction("Index","Crm");
        if (User.IsInRole(Roles.Customer)) return RedirectToAction("Index","Portal");
        return RedirectToAction("Login","Account");
    }
}
[AutoValidateAntiforgeryToken, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users, CustomerService customers) : Controller
{
    [HttpGet] public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true
        ? RedirectToAction("Index","Home") : View(new LoginInput { ReturnUrl = returnUrl });
    // Cookie dành cho web. lockoutOnFailure=true kích hoạt khóa tạm khi sai 5 lần.
    // Một thông báo chung tránh tiết lộ email nào tồn tại hay tài khoản nào bị khóa.
    [HttpPost, EnableRateLimiting("login")] public async Task<IActionResult> Login(LoginInput input) {
        var user = ModelState.IsValid ? await users.FindByEmailAsync(input.Email) : null;
        if (user != null && !user.IsDisabled) {
            signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
            if ((await signIn.PasswordSignInAsync(user,input.Password,false,true)).Succeeded) {
                // Chỉ quay lại URL nội bộ, tránh ReturnUrl chuyển khách sang website giả.
                if (Url.IsLocalUrl(input.ReturnUrl)) return LocalRedirect(input.ReturnUrl!);
                return RedirectToAction("Index","Home");
            }
        }
        if (ModelState.IsValid) ModelState.AddModelError("", "Email, mật khẩu không đúng hoặc tài khoản bị khóa. Nếu vừa nhập sai nhiều lần, hãy thử lại sau 10 phút.");
        input.Password = "";
        ModelState.SetModelValue(nameof(input.Password),"","");
        return View(input);
    }
    [HttpGet] public IActionResult Register() => User.Identity?.IsAuthenticated == true
        ? RedirectToAction("Index","Home") : View(new RegisterInput());
    [HttpGet] public IActionResult AccessDenied() { Response.StatusCode = 403; return View(); }
    // Đăng ký luôn cấp Customer trong service, không nhận vai trò từ trình duyệt.
    [HttpPost, EnableRateLimiting("login")] public async Task<IActionResult> Register(RegisterInput input) {
        if (ModelState.IsValid) {
            try {
                await customers.Register(new(input.Email,input.Password,input.FullName));
                TempData["Message"] = "Đăng ký thành công! Bạn có thể đăng nhập bằng email và mật khẩu vừa tạo.";
                return RedirectToAction("Login");
            } catch (CrmException ex) { ModelState.AddModelError("",ex.Message); }
            catch (DbUpdateException) { ModelState.AddModelError("","Email này vừa được đăng ký. Vui lòng dùng email khác hoặc đăng nhập."); }
        }
        // Giữ họ tên/email khi có lỗi nhưng không trả mật khẩu trong HTML.
        input.Password = input.ConfirmPassword = "";
        ModelState.SetModelValue(nameof(input.Password),"","");
        ModelState.SetModelValue(nameof(input.ConfirmPassword),"","");
        return View(input);
    }
    [HttpPost, Authorize(AuthenticationSchemes = "Identity.Application")] public async Task<IActionResult> Logout() {
        signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
        await signIn.SignOutAsync(); return RedirectToAction("Login");
    }
}
public sealed class SurveyEditor
{
    [Required(ErrorMessage = "Vui lòng nhập tiêu đề khảo sát."), StringLength(150)]
    public string Title { get; set; } = "";
    [StringLength(2000)]
    public string? Description { get; set; }
    public DateTime ClosesAt { get; set; } = DateTime.UtcNow.AddHours(7).AddDays(7);
    public Guid ConcurrencyToken { get; set; }
    public List<QuestionEditor> Questions { get; set; } = [new()];
    public SurveyDraft Draft() => new(Title,Description,new DateTimeOffset(DateTime.SpecifyKind(ClosesAt,DateTimeKind.Unspecified),TimeSpan.FromHours(7)).UtcDateTime,
        Questions.Select(q => new QuestionDraft(q.Text,q.Kind,q.IsRequired,
            (q.Kind is QuestionKind.SingleChoice or QuestionKind.MultipleChoice) ? (q.Options ?? "").Split('\n',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [])).ToArray());
}
public sealed class QuestionEditor
{
    [Required(ErrorMessage = "Vui lòng nhập nội dung câu hỏi."), StringLength(500)]
    public string Text { get; set; } = "";
    public QuestionKind Kind { get; set; }
    public bool IsRequired { get; set; } = true;
    public string Options { get; set; } = "";
}
// Dấu phẩy trong Roles là điều kiện HOẶC: nhân viên hoặc quản lý được vào nghiệp vụ CRM.
// Các action nhạy cảm có thêm [Authorize(Roles = Roles.Manager)] để chỉ quản lý thực hiện.
// Ẩn nút trong Razor giúp giao diện rõ ràng; Authorize mới là lớp chặn quyền ở server.
[Authorize(AuthenticationSchemes = "Identity.Application", Roles = Roles.Manager + "," + Roles.Staff), AutoValidateAntiforgeryToken]
public sealed class CrmController(CustomerService customers, FeedbackService feedback, SurveyService surveys,
    ReportService reports, CrmDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index() => View(User.IsInRole(Roles.Manager) ? await reports.Overview() : null);
    public async Task<IActionResult> Customers(string? search,bool archived = false,string? status = null) {
        var all = await customers.List(null,archived);
        ViewBag.CustomerTotals = new[] { all.Length, all.Count(x => !x.IsDisabled), all.Count(x => x.IsDisabled) };
        IEnumerable<CustomerRow> rows = all;
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(x => x.FullName.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)
            || x.Email.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase) || (x.Phone?.Contains(search.Trim()) ?? false));
        if (status == "active") rows = rows.Where(x => !x.IsDisabled);
        else if (status == "locked") rows = rows.Where(x => x.IsDisabled);
        return View(rows.ToArray());
    }
    // 1. Nhân viên tiếp xúc trực tiếp với khách nên được thêm hồ sơ; quản lý có cùng quyền.
    [HttpGet] public IActionResult CreateCustomer() => View(new CustomerCreateInput());
    [HttpPost] public async Task<IActionResult> CreateCustomer(CustomerCreateInput input) {
        if (ModelState.IsValid) {
            try {
                var id = await customers.Create(new(input.Email,input.FullName,input.InitialPassword),UserId);
                TempData["Message"] = "Đã tạo khách hàng. Bạn có thể bổ sung thông tin liên hệ và sở thích.";
                return RedirectToAction("EditCustomer",new { id });
            } catch (CrmException ex) { ModelState.AddModelError("",ex.Message); }
        }
        // Không đưa mật khẩu đã nhập trở lại HTML khi form có lỗi.
        input.InitialPassword = ""; ModelState.SetModelValue(nameof(input.InitialPassword),"","");
        return View(input);
    }
    [HttpGet] public async Task<IActionResult> EditCustomer(Guid id) {
        var c = await db.Customers.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new CrmException(404,"Khách hàng không tồn tại.");
        ViewBag.Preferences = await db.Preferences.ToListAsync(); ViewBag.TargetId = id;
        return View("Profile",await customers.Profile(c.UserId));
    }
    [HttpPost] public async Task<IActionResult> EditCustomer(Guid id,ProfileUpdate input) {
        try {
            Rules.Require(ModelState.IsValid,"Thông tin hồ sơ không hợp lệ. Kiểm tra ngày sinh và số điện thoại.");
            await customers.Update(id,input,UserId); TempData["Message"] = "Đã cập nhật hồ sơ khách hàng.";
            return RedirectToAction("Customers");
        } catch (CrmException ex) {
            ModelState.AddModelError("",ex.Message);
            var existing = await customers.List(null);
            var row = existing.SingleOrDefault(x => x.Id == id) ?? throw new CrmException(404,"Khách hàng không tồn tại.");
            ViewBag.Preferences = await db.Preferences.ToListAsync();
            return View("Profile",new ProfileDto(id,row.Email,input.FullName,input.Phone,input.BirthDate,input.Address,input.PreferenceIds ?? [],input.ConcurrencyToken));
        }
    }
    // 3. Chỉ quản lý khóa/mở khóa: thao tác này ngăn khách đăng nhập và thu hồi phiên đang dùng.
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> LockCustomer(Guid id,bool disabled) {
        try {
            Rules.Require(ModelState.IsValid,"Trạng thái khóa tài khoản không hợp lệ.");
            await customers.SetDisabled(id,disabled,UserId); TempData["Message"] = disabled ? "Đã khóa tài khoản khách hàng." : "Đã mở khóa. Khách hàng cần đăng nhập lại.";
        } catch (CrmException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Customers");
    }
    // 2. Xóa nghiệp vụ là lưu trữ: chỉ quản lý được thực hiện, giữ lại lịch sử phản hồi và khảo sát.
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> ArchiveCustomer(Guid id,Guid version) {
        try { await customers.Archive(id,version,UserId); TempData["Message"] = "Đã xóa khách hàng khỏi danh sách hoạt động và lưu trữ lịch sử."; }
        catch (CrmException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Customers");
    }
    public async Task<IActionResult> Feedback(FeedbackStatus? status,string? search) {
        var all = await feedback.List();
        ViewBag.FeedbackTotals = new[] { all.Length, all.Count(x => x.Status is FeedbackStatus.New or FeedbackStatus.InProgress),
            all.Count(x => x.Status is FeedbackStatus.Resolved or FeedbackStatus.Closed) };
        IEnumerable<FeedbackDto> rows = all;
        if (status != null) rows = rows.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(x => x.CustomerName.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase)
            || x.Content.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase));
        return View(rows.ToArray());
    }
    // 4. Nhân viên và quản lý đều tiếp nhận/trả lời, ghi nhận người xử lý và trạng thái phản hồi.
    [HttpPost] public async Task<IActionResult> Reply(Guid id,string content,FeedbackStatus status,Guid concurrencyToken) {
        try {
            Rules.Require(ModelState.IsValid,"Trạng thái phản hồi không hợp lệ.");
            await feedback.Reply(id,new(content,status,concurrencyToken),UserId); TempData["Message"] = "Đã gửi câu trả lời đến khách hàng.";
        } catch (CrmException ex) { TempData["Error"] = ex.Message; TempData["ReplyId"] = id.ToString(); TempData["ReplyContent"] = content; }
        return RedirectToAction("Feedback");
    }
    // 6. Quản lý quyết định nội dung, người nhận, thời điểm phát hành/đóng và xem thống kê khảo sát.
    [Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Surveys() => View(await surveys.List());
    [HttpGet, Authorize(Roles = Roles.Manager)] public IActionResult CreateSurvey() => View("SurveyEditor",new SurveyEditor());
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> CreateSurvey(SurveyEditor input) {
        if (ModelState.IsValid) {
            try { await surveys.Create(input.Draft(),UserId); TempData["Message"] = "Đã lưu bản nháp khảo sát. Chọn Gửi khảo sát để phát hành."; return RedirectToAction("Surveys"); }
            catch (CrmException ex) { ModelState.AddModelError("",ex.Message); }
        }
        return View("SurveyEditor",input);
    }
    [HttpGet, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> EditSurvey(Guid id) {
        var s = await surveys.Get(id); Rules.Require(s.Status == SurveyStatus.Draft,"Chỉ sửa bản nháp.",409);
        return View("SurveyEditor",new SurveyEditor { Title = s.Title, Description = s.Description,
            ClosesAt = s.ClosesAtUtc.AddHours(7),ConcurrencyToken = s.ConcurrencyToken,
            Questions = s.Questions.Select(q => new QuestionEditor { Text = q.Text,Kind = q.Kind,IsRequired = q.IsRequired,Options = string.Join('\n',q.Options.Select(o => o.Text)) }).ToList() });
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> EditSurvey(Guid id,SurveyEditor input) {
        if (ModelState.IsValid) {
            try { await surveys.Update(id,new(input.Draft(),input.ConcurrencyToken)); TempData["Message"] = "Đã cập nhật bản nháp khảo sát."; return RedirectToAction("Surveys"); }
            catch (CrmException ex) { ModelState.AddModelError("",ex.Message); }
        }
        return View("SurveyEditor",input);
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> DeleteSurvey(Guid id,Guid version) {
        try { await surveys.DeleteDraft(id,version); TempData["Message"] = "Đã xóa bản nháp khảo sát."; }
        catch (CrmException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Surveys");
    }
    [HttpGet, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> PublishSurvey(Guid id) => View(await PublishModel(id));
    private async Task<SurveyPublishViewModel> PublishModel(Guid id,string audience = "all",Guid[]? ids = null) {
        var survey = await surveys.Get(id);
        Rules.Require(survey.Status != SurveyStatus.Closed && survey.ClosesAtUtc > DateTime.UtcNow,"Khảo sát đã đóng hoặc hết hạn.",409);
        return new() { Survey = survey, Customers = (await customers.List(null)).Where(x => !x.IsDisabled).ToArray(), Audience = audience, SelectedIds = ids ?? [] };
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Publish(Guid id,Guid version,Guid[]? customerIds,string audience = "all") {
        try {
            Rules.Require(ModelState.IsValid && (audience is "all" or "selected"),"Lựa chọn người nhận không hợp lệ.");
            Rules.Require(audience != "selected" || customerIds is { Length: > 0 },"Chọn ít nhất một khách hàng để gửi.");
            // Gửi trong hộp khảo sát của tài khoản, không phải gửi email. Null nghĩa là tất cả khách hợp lệ.
            var sent = await surveys.Publish(id,new(audience == "all" ? null : customerIds,version));
            TempData["Message"] = sent > 0 ? $"Đã gửi {sent} lời mời đến hộp khảo sát của khách hàng." : "Những khách đã chọn đều đã nhận khảo sát này. Không gửi lời mời trùng.";
            return RedirectToAction("Surveys");
        } catch (CrmException ex) {
            ModelState.AddModelError("",ex.Message);
            var survey = await surveys.Get(id);
            if (survey.Status == SurveyStatus.Closed || survey.ClosesAtUtc <= DateTime.UtcNow) { TempData["Error"] = ex.Message; return RedirectToAction("Surveys"); }
            return View("PublishSurvey",await PublishModel(id,audience,customerIds));
        }
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> CloseSurvey(Guid id,Guid version) {
        try { await surveys.Close(id,version); TempData["Message"] = "Đã đóng khảo sát. Khách hàng không thể gửi thêm câu trả lời."; }
        catch (CrmException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Surveys");
    }
    [Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Results(Guid id) => View(await surveys.Results(id));
    // 5. Chỉ quản lý xem cơ cấu tuổi/sở thích để quyết định chăm sóc và phát triển sản phẩm.
    [Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Reports() => View(await reports.Overview());
}
// MVC phải yêu cầu cookie rõ ràng: khách chưa đăng nhập được chuyển tới Login kèm ReturnUrl.
// Nếu dùng scheme mặc định của Identity API, challenge có thể trả 401 Bearer trên trang web.
[Authorize(AuthenticationSchemes = "Identity.Application", Roles = Roles.Customer), AutoValidateAntiforgeryToken, ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class PortalController(CustomerService customers, FeedbackService feedback, SurveyService surveys,
    CrmDbContext db, UserManager<AppUser> users, SignInManager<AppUser> signIn) : Controller
{
    // Lấy UserId từ cookie đã được Identity xác thực, không nhận CustomerId từ form/query.
    // Mỗi chức năng dùng ID hồ sơ được tìm bằng UserId này để ngăn truy cập dữ liệu khách khác.
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet] public async Task<IActionResult> Index() {
        var profile = await customers.Profile(UserId);
        return View(new PortalOverview(profile,await surveys.Inbox(profile.Id),await feedback.List(profile.Id)));
    }
    [HttpGet] public async Task<IActionResult> Profile() {
        ViewBag.Preferences = await db.Preferences.ToListAsync();
        return View("~/Views/Crm/Profile.cshtml",await customers.Profile(UserId));
    }
    // POST -> Redirect -> GET khi thành công: tải lại trang không gửi lại thay đổi.
    // Khi lỗi giữ giá trị người nhập; email/ID vẫn lấy từ server và không thể sửa qua form.
    [HttpPost] public async Task<IActionResult> Update(ProfileUpdate input) {
        var profile = await customers.Profile(UserId);
        try {
            Rules.Require(ModelState.IsValid,"Thông tin hồ sơ không hợp lệ. Kiểm tra ngày sinh và số điện thoại.");
            await customers.Update(profile.Id,input,UserId); TempData["Message"] = "Đã lưu hồ sơ và sở thích của bạn."; return RedirectToAction("Profile");
        } catch (CrmException ex) {
            ModelState.AddModelError("",ex.Message); ViewBag.Preferences = await db.Preferences.ToListAsync();
            return View("~/Views/Crm/Profile.cshtml",new ProfileDto(profile.Id,profile.Email,input.FullName,input.Phone,input.BirthDate,input.Address,input.PreferenceIds ?? [],input.ConcurrencyToken));
        }
    }
    [HttpGet] public IActionResult ChangePassword() => View(new ChangePasswordInput());
    [HttpPost, EnableRateLimiting("login")] public async Task<IActionResult> ChangePassword(ChangePasswordInput input) {
        if (ModelState.IsValid && input.CurrentPassword == input.NewPassword)
            ModelState.AddModelError(nameof(input.NewPassword),"Mật khẩu mới phải khác mật khẩu hiện tại.");
        if (ModelState.IsValid) {
            var user = await users.GetUserAsync(User);
            if (user == null || user.IsDisabled) return Challenge(IdentityConstants.ApplicationScheme);
            await using var tx = await db.Database.BeginTransactionAsync();
            // Identity kiểm tra mật khẩu cũ, băm mật khẩu mới và đổi SecurityStamp.
            var result = await users.ChangePasswordAsync(user,input.CurrentPassword,input.NewPassword);
            if (result.Succeeded) {
                db.AuditLogs.Add(new() { ActorUserId = UserId, Action = "ChangePassword", Entity = "AppUser", EntityId = UserId });
                await db.SaveChangesAsync(); await tx.CommitAsync();
                // Giữ phiên hiện tại bằng cookie mới; cookie/token cũ ở thiết bị khác bị thu hồi.
                signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
                await signIn.RefreshSignInAsync(user);
                TempData["Message"] = "Đã đổi mật khẩu. Các phiên đăng nhập khác đã hết hiệu lực.";
                return RedirectToAction("Profile");
            }
            foreach (var error in result.Errors) ModelState.AddModelError("",error.Description);
        }
        input.CurrentPassword = input.NewPassword = input.ConfirmPassword = "";
        foreach (var field in new[] { nameof(input.CurrentPassword), nameof(input.NewPassword), nameof(input.ConfirmPassword) })
            ModelState.SetModelValue(field,"","");
        return View(input);
    }
    [HttpGet] public async Task<IActionResult> Feedback(FeedbackStatus? status = null) {
        ViewBag.Products = await db.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        var all = await feedback.List((await customers.ByUser(UserId)).Id);
        ViewBag.TotalFeedback = all.Length;
        ViewBag.FeedbackStatus = status;
        return View(status == null ? all : all.Where(x => x.Status == status).ToArray());
    }
    [HttpPost] public async Task<IActionResult> Feedback(int? productId,int rating,string content) {
        try {
            Rules.Require(ModelState.IsValid,"Sản phẩm hoặc điểm đánh giá không hợp lệ.");
            await feedback.Create((await customers.ByUser(UserId)).Id,new(productId,rating,content)); TempData["Message"] = "Đã gửi phản hồi. Cảm ơn bạn đã chia sẻ!";
            return RedirectToAction("Feedback");
        } catch (CrmException ex) {
            ModelState.AddModelError("",ex.Message); ViewBag.Products = await db.Products.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
            ViewBag.FeedbackContent = content; ViewBag.FeedbackRating = rating; ViewBag.FeedbackProduct = productId;
            return View(await feedback.List((await customers.ByUser(UserId)).Id));
        }
    }
    [HttpGet] public async Task<IActionResult> Surveys(string? status = null) {
        var all = await surveys.Inbox((await customers.ByUser(UserId)).Id);
        ViewBag.SurveyCounts = new[] { all.Length, all.Count(PortalLabels.Available), all.Count(x => x.HasResponded), all.Count(x => !x.HasResponded && !PortalLabels.Available(x)) };
        ViewBag.SurveyStatus = status;
        return View(status switch {
            "pending" => all.Where(PortalLabels.Available).ToArray(),
            "answered" => all.Where(x => x.HasResponded).ToArray(),
            "closed" => all.Where(x => !x.HasResponded && !PortalLabels.Available(x)).ToArray(),
            _ => all });
    }
    private async Task<SurveyDto> CustomerSurvey(Guid id,Guid customerId) {
        var survey = await surveys.ForCustomer(id,customerId);
        ViewBag.Response = await surveys.ResponseForCustomer(id,customerId);
        ViewBag.HasResponded = ViewBag.Response != null;
        return survey;
    }
    [HttpGet] public async Task<IActionResult> Survey(Guid id) => View(await CustomerSurvey(id,(await customers.ByUser(UserId)).Id));
    [HttpPost, ActionName("Survey")] public async Task<IActionResult> SubmitSurvey(Guid id) {
        var customerId = (await customers.ByUser(UserId)).Id;
        var survey = await surveys.ForCustomer(id,customerId); var answers = new List<AnswerInput>();
        try {
            // Chỉ nhận các trường câu hỏi có trong khảo sát, không nhận ID lạ do sửa HTML.
            Rules.Require(Request.Form.Keys.Where(k => k.StartsWith("q_",StringComparison.Ordinal))
                .All(k => survey.Questions.Any(q => k == $"q_{q.Id}")),"Câu hỏi không thuộc khảo sát này.");
            foreach (var q in survey.Questions) {
                var values = Request.Form[$"q_{q.Id}"].Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                if (values.Length == 0) continue;
                Rules.Require(q.Kind == QuestionKind.MultipleChoice || values.Length == 1,"Câu hỏi này chỉ nhận một câu trả lời.");
                answers.Add(q.Kind switch {
                    QuestionKind.Text => new(q.Id,values[0],null,[]),
                    QuestionKind.Rating => new(q.Id,null,int.TryParse(values[0],out var r) ? r : -1,[]),
                    _ => new(q.Id,null,null,values.Select(x => Guid.TryParse(x,out var g) ? g : Guid.Empty).ToArray()) });
            }
            await surveys.Submit(id,customerId,new(answers.ToArray())); TempData["Message"] = "Cảm ơn bạn đã trả lời khảo sát."; return RedirectToAction("Surveys");
        } catch (CrmException ex) {
            ModelState.AddModelError("",ex.Message);
            return View("Survey",await CustomerSurvey(id,customerId));
        }
    }
}
[Authorize(AuthenticationSchemes = "Identity.Application", Roles = Roles.Admin), AutoValidateAntiforgeryToken]
public sealed class AdminController(AdminService admin, CrmDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index(string? search,string? role,string? status) {
        var all = await admin.UserList(null);
        ViewBag.UserTotals = new[] { all.Length, all.Count(x => !x.IsDisabled),
            all.Count(x => x.Roles.Any(r => r != Roles.Customer)), all.Count(x => x.IsDisabled) };
        IEnumerable<UserRow> rows = all;
        if (!string.IsNullOrWhiteSpace(search)) rows = rows.Where(x => x.Email.Contains(search.Trim(),StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(role)) rows = rows.Where(x => x.Roles.Contains(role));
        if (status == "active") rows = rows.Where(x => !x.IsDisabled);
        else if (status == "locked") rows = rows.Where(x => x.IsDisabled);
        return View(rows.ToArray());
    }
    [HttpPost] public async Task<IActionResult> CreateUser(string email,string initialPassword,string role) {
        try {
            await admin.CreateUser(new(email,initialPassword,role),UserId);
            TempData["Message"] = "Đã tạo tài khoản thành công.";
        } catch (CrmException ex) {
            TempData["Error"] = ex.Message; TempData["CreateEmail"] = email; TempData["CreateRole"] = role;
        }
        return RedirectToAction("Index");
    }
    [HttpPost] public async Task<IActionResult> Role(string id,string role) {
        try { await admin.ChangeRole(id,role,UserId); TempData["Message"] = "Đã cập nhật vai trò của tài khoản."; }
        catch (CrmException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Index");
    }
    [HttpPost] public async Task<IActionResult> Lock(string id,bool disabled) {
        try { await admin.Disable(id,disabled,UserId); TempData["Message"] = disabled ? "Đã khóa tài khoản." : "Đã mở khóa tài khoản."; }
        catch (CrmException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction("Index");
    }
    public async Task<IActionResult> Products(string? search,int? categoryId,int? supplierId,decimal? minPrice,decimal? maxPrice,bool? active,string? sort) {
        ViewBag.Categories = await db.Categories.ToListAsync(); ViewBag.Suppliers = await db.Suppliers.ToListAsync();
        return View(await admin.Products(search,categoryId,supplierId,minPrice,maxPrice,active,sort));
    }
    [HttpGet] public async Task<IActionResult> ProductEditor(int? id) {
        ViewBag.Categories = await db.Categories.ToListAsync(); ViewBag.Suppliers = await db.Suppliers.ToListAsync();
        return View(id == null ? new Product { IsActive = true } : await db.Products.FindAsync(id));
    }
    [HttpPost] public async Task<IActionResult> ProductEditor(int? id,ProductInput input) { await admin.SaveProduct(id,input); return RedirectToAction("Products"); }
    public async Task<IActionResult> Suppliers(string? search,bool? active,string? sort) {
        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.Contains(search) || (x.Email != null && x.Email.Contains(search)) || (x.Phone != null && x.Phone.Contains(search)));
        if (active != null) q = q.Where(x => x.IsActive == active);
        return View(await (sort == "name_desc" ? q.OrderByDescending(x => x.Name) : q.OrderBy(x => x.Name)).ToArrayAsync());
    }
    [HttpGet] public async Task<IActionResult> SupplierEditor(int? id) => View(id == null ? new Supplier() : await db.Suppliers.FindAsync(id));
    [HttpPost] public async Task<IActionResult> SupplierEditor(int? id,SupplierInput input) { await admin.SaveSupplier(id,input); return RedirectToAction("Suppliers"); }
}
