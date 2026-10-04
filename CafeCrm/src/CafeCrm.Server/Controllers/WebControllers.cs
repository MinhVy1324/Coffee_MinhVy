using System.Security.Claims;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using CafeCrm.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
[AutoValidateAntiforgeryToken]
public sealed class AccountController(SignInManager<AppUser> signIn, UserManager<AppUser> users, CustomerService customers) : Controller
{
    [HttpGet] public IActionResult Login() => View();
    [HttpPost] public async Task<IActionResult> Login(string email,string password) {
        var user = await users.FindByEmailAsync(email ?? "");
        if (user != null && !user.IsDisabled) {
            signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
            if ((await signIn.PasswordSignInAsync(user,password ?? "",false,true)).Succeeded) return RedirectToAction("Index","Home");
        }
        ViewBag.Error = "Email, mật khẩu không đúng hoặc tài khoản bị khóa."; return View();
    }
    [HttpGet] public IActionResult Register() => View();
    [HttpPost] public async Task<IActionResult> Register(string email,string password,string fullName) {
        await customers.Register(new(email,password,fullName)); return RedirectToAction("Login");
    }
    [HttpPost, Authorize] public async Task<IActionResult> Logout() {
        signIn.AuthenticationScheme = IdentityConstants.ApplicationScheme;
        await signIn.SignOutAsync(); return RedirectToAction("Login");
    }
}
public sealed class SurveyEditor
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime ClosesAt { get; set; } = DateTime.UtcNow.AddHours(7).AddDays(7);
    public Guid ConcurrencyToken { get; set; }
    public List<QuestionEditor> Questions { get; set; } = [new()];
    public SurveyDraft Draft() => new(Title,Description,new DateTimeOffset(DateTime.SpecifyKind(ClosesAt,DateTimeKind.Unspecified),TimeSpan.FromHours(7)).UtcDateTime,
        Questions.Select(q => new QuestionDraft(q.Text,q.Kind,q.IsRequired,
            (q.Kind is QuestionKind.SingleChoice or QuestionKind.MultipleChoice) ? q.Options.Split('\n',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [])).ToArray());
}
public sealed class QuestionEditor
{
    public string Text { get; set; } = "";
    public QuestionKind Kind { get; set; }
    public bool IsRequired { get; set; } = true;
    public string Options { get; set; } = "";
}
[Authorize(Roles = Roles.Manager + "," + Roles.Staff), AutoValidateAntiforgeryToken]
public sealed class CrmController(CustomerService customers, FeedbackService feedback, SurveyService surveys,
    ReportService reports, CrmDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index() => View(User.IsInRole(Roles.Manager) ? await reports.Overview() : null);
    public async Task<IActionResult> Customers(string? search,bool archived = false) => View(await customers.List(search,archived));
    [HttpGet] public IActionResult CreateCustomer() => View();
    [HttpPost] public async Task<IActionResult> CreateCustomer(string email,string fullName,string initialPassword) {
        await customers.Create(new(email,fullName,initialPassword),UserId); return RedirectToAction("Customers");
    }
    [HttpGet] public async Task<IActionResult> EditCustomer(Guid id) {
        var c = await db.Customers.SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw new CrmException(404,"Khách hàng không tồn tại.");
        ViewBag.Preferences = await db.Preferences.ToListAsync(); ViewBag.TargetId = id;
        return View("Profile",await customers.Profile(c.UserId));
    }
    [HttpPost] public async Task<IActionResult> EditCustomer(Guid id,ProfileUpdate input) {
        await customers.Update(id,input,UserId); return RedirectToAction("Customers");
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> LockCustomer(Guid id,bool disabled) {
        await customers.SetDisabled(id,disabled,UserId); return RedirectToAction("Customers");
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> ArchiveCustomer(Guid id,Guid version) {
        await customers.Archive(id,version,UserId); return RedirectToAction("Customers");
    }
    public async Task<IActionResult> Feedback(FeedbackStatus? status) => View(await feedback.List(status:status));
    [HttpPost] public async Task<IActionResult> Reply(Guid id,string content,FeedbackStatus status,Guid concurrencyToken) {
        await feedback.Reply(id,new(content,status,concurrencyToken),UserId); return RedirectToAction("Feedback");
    }
    [Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Surveys() => View(await surveys.List());
    [HttpGet, Authorize(Roles = Roles.Manager)] public IActionResult CreateSurvey() => View("SurveyEditor",new SurveyEditor());
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> CreateSurvey(SurveyEditor input) {
        await surveys.Create(input.Draft(),UserId); return RedirectToAction("Surveys");
    }
    [HttpGet, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> EditSurvey(Guid id) {
        var s = await surveys.Get(id); Rules.Require(s.Status == SurveyStatus.Draft,"Chỉ sửa bản nháp.",409);
        return View("SurveyEditor",new SurveyEditor { Title = s.Title, Description = s.Description,
            ClosesAt = s.ClosesAtUtc.AddHours(7),ConcurrencyToken = s.ConcurrencyToken,
            Questions = s.Questions.Select(q => new QuestionEditor { Text = q.Text,Kind = q.Kind,IsRequired = q.IsRequired,Options = string.Join('\n',q.Options.Select(o => o.Text)) }).ToList() });
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> EditSurvey(Guid id,SurveyEditor input) {
        await surveys.Update(id,new(input.Draft(),input.ConcurrencyToken)); return RedirectToAction("Surveys");
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> DeleteSurvey(Guid id,Guid version) {
        await surveys.DeleteDraft(id,version); return RedirectToAction("Surveys");
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Publish(Guid id,Guid version,Guid[]? customerIds) {
        var sent = await surveys.Publish(id,new(customerIds,version)); TempData["Message"] = $"Đã gửi {sent} lời mời vào hộp khảo sát của khách."; return RedirectToAction("Surveys");
    }
    [HttpPost, Authorize(Roles = Roles.Manager)] public async Task<IActionResult> CloseSurvey(Guid id,Guid version) {
        await surveys.Close(id,version); return RedirectToAction("Surveys");
    }
    [Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Results(Guid id) => View(await surveys.Results(id));
    [Authorize(Roles = Roles.Manager)] public async Task<IActionResult> Reports() => View(await reports.Overview());
}
[Authorize(Roles = Roles.Customer), AutoValidateAntiforgeryToken]
public sealed class PortalController(CustomerService customers, FeedbackService feedback, SurveyService surveys, CrmDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index() {
        ViewBag.Preferences = await db.Preferences.ToListAsync();
        return View("~/Views/Crm/Profile.cshtml",await customers.Profile(UserId));
    }
    [HttpPost] public async Task<IActionResult> Update(ProfileUpdate input) {
        await customers.Update((await customers.ByUser(UserId)).Id,input,UserId); TempData["Message"] = "Đã lưu hồ sơ."; return RedirectToAction("Index");
    }
    public async Task<IActionResult> Feedback() {
        ViewBag.Products = await db.Products.Where(x => x.IsActive).ToListAsync();
        return View(await feedback.List((await customers.ByUser(UserId)).Id));
    }
    [HttpPost] public async Task<IActionResult> Feedback(int? productId,int rating,string content) {
        await feedback.Create((await customers.ByUser(UserId)).Id,new(productId,rating,content)); return RedirectToAction("Feedback");
    }
    public async Task<IActionResult> Surveys() => View(await surveys.Inbox((await customers.ByUser(UserId)).Id));
    [HttpGet] public async Task<IActionResult> Survey(Guid id) => View(await surveys.ForCustomer(id,(await customers.ByUser(UserId)).Id));
    [HttpPost, ActionName("Survey")] public async Task<IActionResult> SubmitSurvey(Guid id) {
        var customerId = (await customers.ByUser(UserId)).Id;
        var survey = await surveys.ForCustomer(id,customerId); var answers = new List<AnswerInput>();
        foreach (var q in survey.Questions) {
            var values = Request.Form[$"q_{q.Id}"].Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
            if (values.Length == 0) continue;
            answers.Add(q.Kind switch {
                QuestionKind.Text => new(q.Id,values[0],null,[]),
                QuestionKind.Rating => new(q.Id,null,int.TryParse(values[0],out var r) ? r : -1,[]),
                _ => new(q.Id,null,null,values.Select(x => Guid.TryParse(x,out var g) ? g : Guid.Empty).ToArray()) });
        }
        await surveys.Submit(id,customerId,new(answers.ToArray())); TempData["Message"] = "Cảm ơn ông đã trả lời khảo sát."; return RedirectToAction("Surveys");
    }
}
[Authorize(Roles = Roles.Admin), AutoValidateAntiforgeryToken]
public sealed class AdminController(AdminService admin, CrmDbContext db) : Controller
{
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    public async Task<IActionResult> Index(string? search) => View(await admin.UserList(search));
    [HttpPost] public async Task<IActionResult> CreateUser(string email,string initialPassword,string role) {
        await admin.CreateUser(new(email,initialPassword,role),UserId); return RedirectToAction("Index");
    }
    [HttpPost] public async Task<IActionResult> Role(string id,string role) { await admin.ChangeRole(id,role,UserId); return RedirectToAction("Index"); }
    [HttpPost] public async Task<IActionResult> Lock(string id,bool disabled) { await admin.Disable(id,disabled,UserId); return RedirectToAction("Index"); }
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
