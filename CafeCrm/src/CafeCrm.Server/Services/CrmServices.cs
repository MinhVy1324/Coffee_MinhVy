using System.ComponentModel.DataAnnotations;
using System.Data;
using CafeCrm.Contracts;
using CafeCrm.Server.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CafeCrm.Server.Services;

public sealed class CrmException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
public static class Rules
{
    public static void Require(bool condition, string message, int status = 400)
    { if (!condition) throw new CrmException(status, message); }
    public static void Validate(object value)
    {
        var errors = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(value, new ValidationContext(value), errors, true);
        // DTO record đặt DataAnnotations trên tham số constructor. MVC hiểu metadata này,
        // nhưng TryValidateObject chỉ đọc attribute trên property, nên service phải đọc thêm.
        // Nếu bỏ bước này, web gọi service trực tiếp có thể lọt rating/độ dài không hợp lệ.
        var type = value.GetType();
        var constructor = type.GetConstructors().FirstOrDefault(c => c.GetParameters().Length > 0
            && c.GetParameters().All(p => p.Name != null && type.GetProperty(p.Name) != null));
        if (constructor != null) foreach (var parameter in constructor.GetParameters()) {
            var attributes = parameter.GetCustomAttributes(typeof(ValidationAttribute),true).Cast<ValidationAttribute>();
            var context = new ValidationContext(value) { MemberName = parameter.Name, DisplayName = parameter.Name! };
            valid = Validator.TryValidateValue(type.GetProperty(parameter.Name!)!.GetValue(value),context,errors,attributes) && valid;
        }
        Require(valid,string.Join(" ", errors.Select(x => x.ErrorMessage).Distinct()));
    }
    public static void Version(Guid actual, Guid expected) => Require(actual == expected,
        "Dữ liệu đã được sửa. Hãy tải lại trước khi lưu.", 409);
    public static decimal Percent(int part, int total) => total == 0 ? 0 : Math.Round(100m * part / total, 2);
}
public sealed class CustomerService(CrmDbContext db, UserManager<AppUser> users)
{
    public async Task<Customer> ByUser(string userId) => await db.Customers.Include(x => x.User)
        .Include(x => x.Preferences).SingleOrDefaultAsync(x => x.UserId == userId && !x.IsDeleted)
        ?? throw new CrmException(404, "Không tìm thấy hồ sơ khách hàng.");
    public async Task<ProfileDto> Profile(string userId)
    {
        var c = await ByUser(userId);
        return new(c.Id, c.User.Email!, c.FullName, c.Phone, c.BirthDate, c.Address,
            c.Preferences.Select(x => x.PreferenceId).ToArray(), c.ConcurrencyToken);
    }
    public async Task Register(RegisterRequest request)
    {
        // Dùng cùng Create với nhân viên: tài khoản + hồ sơ + quyền phải cùng thành công.
        // Transaction rollback nếu một bước lỗi, không để tài khoản thiếu hồ sơ.
        await Create(new(request.Email, request.FullName, request.Password), null);
    }
    public async Task<Guid> Create(CustomerCreate request, string? actor)
    {
        request = request with { Email = request.Email?.Trim() ?? "", FullName = request.FullName?.Trim() ?? "" };
        Rules.Validate(request);
        await using var tx = await db.Database.BeginTransactionAsync();
        var user = new AppUser { UserName = request.Email.Trim(), Email = request.Email.Trim() };
        var result = await users.CreateAsync(user, request.InitialPassword);
        Rules.Require(result.Succeeded, string.Join(" ", result.Errors.Select(x => x.Description).Distinct()));
        var roleResult = await users.AddToRoleAsync(user, Roles.Customer);
        Rules.Require(roleResult.Succeeded, "Không thể cấp quyền khách hàng.");
        var customer = new Customer { UserId = user.Id, FullName = request.FullName.Trim() };
        db.Customers.Add(customer);
        Audit(actor, "Create", "Customer", customer.Id.ToString());
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return customer.Id;
    }
    public async Task<CustomerRow[]> List(string? search, bool archived = false)
    {
        var q = db.Customers.AsNoTracking().Include(x => x.User).Where(x => x.IsDeleted == archived);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(x => x.FullName.Contains(search) || x.User.Email!.Contains(search) || (x.Phone != null && x.Phone.Contains(search)));
        return await q.OrderBy(x => x.FullName).Select(x => new CustomerRow(x.Id, x.User.Email!,
            x.FullName, x.Phone, x.BirthDate, x.User.IsDisabled, x.IsDeleted, x.ConcurrencyToken)).ToArrayAsync();
    }
    public async Task Update(Guid id, ProfileUpdate request, string actor)
    {
        // ConcurrencyToken từ lần đọc hồ sơ: từ chối ghi đè nếu phiên khác vừa sửa.
        // Sở thích phải tồn tại; điện thoại không trùng hồ sơ hiện tại khác.
        Rules.Validate(request);
        var c = await db.Customers.Include(x => x.Preferences).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new CrmException(404, "Khách hàng không tồn tại.");
        Rules.Version(c.ConcurrencyToken, request.ConcurrencyToken);
        Rules.Require(!string.IsNullOrWhiteSpace(request.FullName), "Tên không được để trống.");
        Rules.Require(request.BirthDate == null || request.BirthDate <= DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), "Ngày sinh không hợp lệ.");
        var ids = (request.PreferenceIds ?? []).Distinct().ToArray();
        Rules.Require(await db.Preferences.CountAsync(x => ids.Contains(x.Id)) == ids.Length, "Sở thích không hợp lệ.");
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        Rules.Require(phone == null || !await db.Customers.AnyAsync(x => x.Id != id && !x.IsDeleted && x.Phone == phone), "Số điện thoại đã được dùng.", 409);
        c.FullName = request.FullName.Trim(); c.Phone = phone; c.BirthDate = request.BirthDate;
        c.Address = request.Address?.Trim(); c.ConcurrencyToken = Guid.NewGuid();
        db.CustomerPreferences.RemoveRange(c.Preferences.Where(x => !ids.Contains(x.PreferenceId)));
        foreach (var p in ids.Where(p => c.Preferences.All(x => x.PreferenceId != p)))
            db.CustomerPreferences.Add(new() { CustomerId = id, PreferenceId = p });
        Audit(actor, "Update", "Customer", id.ToString()); await db.SaveChangesAsync();
    }
    public async Task SetDisabled(Guid id, bool disabled, string actor)
    {
        // IsDisabled chặn lần đăng nhập mới; đổi SecurityStamp thu hồi cookie/token đang tồn tại.
        // Quyền Manager được kiểm tra tại cả MVC controller và API controller trước khi gọi service.
        var c = await db.Customers.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new CrmException(404, "Khách hàng không tồn tại.");
        c.User.IsDisabled = disabled; c.ConcurrencyToken = Guid.NewGuid();
        await users.UpdateSecurityStampAsync(c.User);
        Audit(actor, disabled ? "Lock" : "Unlock", "Customer", id.ToString()); await db.SaveChangesAsync();
    }
    public async Task Archive(Guid id, Guid version, string actor)
    {
        // Xóa mềm: không Remove(Customer), vì phản hồi/lời mời/câu trả lời còn tham chiếu hồ sơ.
        // IsDeleted loại khách khỏi danh sách hoạt động, báo cáo cơ cấu và người nhận khảo sát mới.
        await using var tx = await db.Database.BeginTransactionAsync();
        var c = await db.Customers.Include(x => x.User).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw new CrmException(404, "Khách hàng không tồn tại.");
        Rules.Version(c.ConcurrencyToken, version);
        c.IsDeleted = true; c.User.IsDisabled = true; c.ConcurrencyToken = Guid.NewGuid();
        await users.UpdateSecurityStampAsync(c.User);
        Audit(actor, "Archive", "Customer", id.ToString()); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private void Audit(string? actor, string action, string entity, string id) =>
        db.AuditLogs.Add(new() { ActorUserId = actor, Action = action, Entity = entity, EntityId = id });
}
public sealed class FeedbackService(CrmDbContext db)
{
    public async Task<Guid> Create(Guid customerId, FeedbackCreate request)
    {
        // CustomerId do controller lấy từ phiên; khách không được chọn chủ phản hồi.
        // ProductId=null là dịch vụ chung; sản phẩm cụ thể phải còn hoạt động khi gửi.
        Rules.Validate(request);
        Rules.Require(!string.IsNullOrWhiteSpace(request.Content), "Nội dung không được để trống.");
        Rules.Require(request.ProductId == null || await db.Products.AnyAsync(x => x.Id == request.ProductId && x.IsActive), "Sản phẩm không tồn tại hoặc ngừng phục vụ.");
        var f = new Feedback { CustomerId = customerId, ProductId = request.ProductId,
            Rating = request.Rating, Content = request.Content.Trim() };
        db.Feedbacks.Add(f); await db.SaveChangesAsync(); return f.Id;
    }
    public async Task<FeedbackDto[]> List(Guid? customerId = null, FeedbackStatus? status = null)
    {
        var q = db.Feedbacks.AsNoTracking().Include(x => x.Customer).Include(x => x.Product)
            .Include(x => x.Replies).ThenInclude(x => x.StaffUser).AsQueryable();
        if (customerId != null) q = q.Where(x => x.CustomerId == customerId);
        if (status != null) q = q.Where(x => x.Status == status);
        var rows = await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
        return rows.Select(x => new FeedbackDto(x.Id, x.Customer.FullName, x.Product?.Name,
            x.Rating, x.Content, x.Status, x.CreatedAtUtc, x.ConcurrencyToken,
            x.Replies.OrderBy(r => r.CreatedAtUtc).Select(r => new ReplyDto(r.StaffUser.UserName!, r.Content, r.CreatedAtUtc)).ToArray())).ToArray();
    }
    public async Task Reply(Guid id, FeedbackReply request, string staffId)
    {
        // Nhân viên hoặc quản lý cùng xử lý. Lưu staffId để biết ai đã trả lời khách.
        // ConcurrencyToken ngăn hai người xử lý cùng lúc ghi đè trạng thái của nhau.
        Rules.Validate(request);
        var f = await db.Feedbacks.FindAsync(id) ?? throw new CrmException(404, "Phản hồi không tồn tại.");
        Rules.Version(f.ConcurrencyToken, request.ConcurrencyToken);
        Rules.Require(Enum.IsDefined(request.Status), "Trạng thái không hợp lệ.");
        Rules.Require(f.Status != FeedbackStatus.Closed, "Phản hồi đã đóng.", 409);
        Rules.Require(request.Status != FeedbackStatus.New, "Không thể đưa phản hồi trở lại trạng thái mới.");
        Rules.Require(!string.IsNullOrWhiteSpace(request.Content), "Nội dung trả lời không được để trống.");
        db.FeedbackReplies.Add(new() { FeedbackId = id, StaffUserId = staffId, Content = request.Content.Trim() });
        f.Status = request.Status; f.ConcurrencyToken = Guid.NewGuid();
        db.AuditLogs.Add(new() { ActorUserId = staffId, Action = "Reply", Entity = "Feedback", EntityId = id.ToString() });
        await db.SaveChangesAsync();
    }
}
public sealed class SurveyService(CrmDbContext db)
{
    private static void ValidateDraft(SurveyDraft draft)
    {
        Rules.Validate(draft);
        Rules.Require(!string.IsNullOrWhiteSpace(draft.Title), "Tiêu đề không được để trống.");
        Rules.Require(draft.ClosesAtUtc > DateTime.UtcNow, "Hạn khảo sát phải ở tương lai.");
        Rules.Require(draft.Questions is { Length: > 0 and <= 30 }, "Khảo sát cần từ 1 đến 30 câu hỏi.");
        foreach (var q in draft.Questions)
        {
            Rules.Validate(q);
            Rules.Require(!string.IsNullOrWhiteSpace(q.Text) && Enum.IsDefined(q.Kind), "Câu hỏi không hợp lệ.");
            var options = q.Options ?? [];
            var choice = q.Kind is QuestionKind.SingleChoice or QuestionKind.MultipleChoice;
            Rules.Require(choice ? options.Length is >= 2 and <= 10 : options.Length == 0, "Câu chọn cần 2 đến 10 lựa chọn; câu điểm hoặc văn bản không có lựa chọn.");
            Rules.Require(options.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 200) && options.Select(x => x.Trim()).Distinct().Count() == options.Length,
                "Lựa chọn trống, trùng hoặc quá dài.");
        }
    }
    private static List<SurveyQuestion> Questions(SurveyDraft d) => d.Questions.Select((q, i) =>
        new SurveyQuestion { Text = q.Text.Trim(), Kind = q.Kind, IsRequired = q.IsRequired, Position = i,
            Options = (q.Options ?? []).Select((o,j) => new SurveyOption { Text = o.Trim(), Position = j }).ToList() }).ToList();
    public async Task<Guid> Create(SurveyDraft draft, string actor)
    {
        ValidateDraft(draft);
        var s = new Survey { Title = draft.Title.Trim(), Description = draft.Description?.Trim(),
            ClosesAtUtc = draft.ClosesAtUtc.ToUniversalTime(), CreatedByUserId = actor, Questions = Questions(draft) };
        db.Surveys.Add(s); await db.SaveChangesAsync(); return s.Id;
    }
    public async Task<SurveyDto> Get(Guid id)
    {
        var s = await Load(id);
        return ToDto(s);
    }
    public async Task<SurveyDto[]> List() => (await db.Surveys.AsNoTracking().Include(x => x.Questions)
        .ThenInclude(x => x.Options).OrderByDescending(x => x.CreatedAtUtc).ToListAsync()).Select(ToDto).ToArray();
    private Task<Survey?> Find(Guid id) => db.Surveys.Include(x => x.Questions).ThenInclude(x => x.Options).SingleOrDefaultAsync(x => x.Id == id);
    private async Task<Survey> Load(Guid id) => await Find(id) ?? throw new CrmException(404, "Khảo sát không tồn tại.");
    private static SurveyDto ToDto(Survey s) => new(s.Id, s.Title, s.Description, s.Status, s.ClosesAtUtc,
        s.ConcurrencyToken, s.Questions.OrderBy(x => x.Position).Select(q => new QuestionDto(q.Id, q.Text,
            q.Kind, q.IsRequired, q.Options.OrderBy(o => o.Position).Select(o => new OptionDto(o.Id, o.Text)).ToArray())).ToArray());
    public async Task Update(Guid id, SurveyUpdate update)
    {
        ValidateDraft(update.Draft);
        await using var tx = await db.Database.BeginTransactionAsync();
        var s = await Load(id); Rules.Version(s.ConcurrencyToken, update.ConcurrencyToken);
        Rules.Require(s.Status == SurveyStatus.Draft, "Chỉ được sửa bản nháp.", 409);
        db.SurveyOptions.RemoveRange(s.Questions.SelectMany(x => x.Options));
        db.SurveyQuestions.RemoveRange(s.Questions); await db.SaveChangesAsync();
        s.Title = update.Draft.Title.Trim(); s.Description = update.Draft.Description?.Trim();
        s.ClosesAtUtc = update.Draft.ClosesAtUtc.ToUniversalTime(); s.Questions = Questions(update.Draft);
        s.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task DeleteDraft(Guid id, Guid version)
    {
        var s = await Load(id); Rules.Version(s.ConcurrencyToken, version);
        Rules.Require(s.Status == SurveyStatus.Draft, "Chỉ được xóa bản nháp.", 409);
        db.SurveyOptions.RemoveRange(s.Questions.SelectMany(x => x.Options));
        db.SurveyQuestions.RemoveRange(s.Questions); db.Surveys.Remove(s); await db.SaveChangesAsync();
    }
    public async Task<int> Publish(Guid id, PublishRequest request)
    {
        // Null = gửi tất cả khách hoạt động. Mảng rỗng = chưa chọn ai, không được hiểu là gửi tất cả.
        // Sau phát hành, cấu trúc câu hỏi bị khóa; có thể gửi thêm khách chưa được mời.
        Rules.Require(request.CustomerIds == null || request.CustomerIds.Length > 0,"Chọn ít nhất một khách hàng để gửi.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var s = await Load(id); Rules.Version(s.ConcurrencyToken, request.ConcurrencyToken);
        Rules.Require(s.Status != SurveyStatus.Closed && s.ClosesAtUtc > DateTime.UtcNow, "Khảo sát đã đóng hoặc hết hạn.", 409);
        var eligible = db.Customers.Where(x => !x.IsDeleted && !x.User.IsDisabled);
        if (request.CustomerIds is { Length: > 0 }) eligible = eligible.Where(x => request.CustomerIds.Contains(x.Id));
        var recipients = await eligible.Select(x => x.Id).ToListAsync();
        if (request.CustomerIds is { Length: > 0 })
            Rules.Require(recipients.Count == request.CustomerIds.Distinct().Count(), "Danh sách có khách bị khóa, đã xóa hoặc không tồn tại.");
        Rules.Require(recipients.Count > 0, "Chưa có khách hàng hợp lệ để gửi.");
        var existing = await db.SurveyInvitations.Where(x => x.SurveyId == id).Select(x => x.CustomerId).ToListAsync();
        var fresh = recipients.Except(existing).ToArray();
        foreach (var c in fresh) db.SurveyInvitations.Add(new() { SurveyId = id, CustomerId = c });
        s.Status = SurveyStatus.Published; s.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(); await tx.CommitAsync(); return fresh.Length;
    }
    public async Task Close(Guid id, Guid version)
    {
        var s = await Load(id); Rules.Version(s.ConcurrencyToken, version);
        Rules.Require(s.Status == SurveyStatus.Published, "Chỉ đóng khảo sát đang phát hành.", 409);
        s.Status = SurveyStatus.Closed; s.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();
    }
    public async Task<InvitationDto[]> Inbox(Guid customerId) => await db.SurveyInvitations.AsNoTracking()
        .Where(x => x.CustomerId == customerId).OrderByDescending(x => x.SentAtUtc)
        .Select(x => new InvitationDto(x.Id, x.SurveyId, x.Survey.Title, x.Survey.ClosesAtUtc,
            db.SurveyResponses.Any(r => r.InvitationId == x.Id), x.Survey.Status == SurveyStatus.Closed)).ToArrayAsync();
    public async Task<SurveyDto> ForCustomer(Guid surveyId, Guid customerId)
    {
        Rules.Require(await db.SurveyInvitations.AnyAsync(x => x.SurveyId == surveyId && x.CustomerId == customerId), "Không có quyền xem khảo sát này.", 404);
        return await Get(surveyId);
    }
    public async Task<SurveyResponseDto?> ResponseForCustomer(Guid surveyId, Guid customerId)
    {
        // Điều kiện sở hữu nằm trong query, không lấy câu trả lời rồi mới lọc trên trình duyệt.
        Rules.Require(await db.SurveyInvitations.AnyAsync(x => x.SurveyId == surveyId && x.CustomerId == customerId), "Không có quyền xem khảo sát này.", 404);
        var response = await db.SurveyResponses.AsNoTracking().Include(x => x.Answers).ThenInclude(x => x.SelectedOptions)
            .SingleOrDefaultAsync(x => x.SurveyId == surveyId && x.Invitation.CustomerId == customerId);
        return response == null ? null : new(response.SubmittedAtUtc, response.Answers.Select(a =>
            new AnswerInput(a.QuestionId,a.TextValue,a.RatingValue,a.SelectedOptions.Select(o => o.OptionId).ToArray())).ToArray());
    }
    public async Task<Guid> Submit(Guid surveyId, Guid customerId, SubmitResponse input)
    {
        // Chỉ một lần mỗi lời mời, đúng hạn/kiểu câu hỏi và đủ câu bắt buộc.
        // Transaction + unique index InvitationId chặn hai request gửi đồng thời.
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var s = await Load(surveyId);
        Rules.Require(s.Status == SurveyStatus.Published && s.ClosesAtUtc > DateTime.UtcNow, "Khảo sát đã đóng hoặc hết hạn.", 409);
        var invitation = await db.SurveyInvitations.SingleOrDefaultAsync(x => x.SurveyId == surveyId && x.CustomerId == customerId)
            ?? throw new CrmException(404, "Bạn chưa được mời tham gia khảo sát này.");
        Rules.Require(!await db.SurveyResponses.AnyAsync(x => x.InvitationId == invitation.Id), "Khảo sát đã được trả lời.", 409);
        Rules.Require(input.Answers != null, "Câu trả lời không hợp lệ.");
        var answers = input.Answers!;
        Rules.Require(answers.All(x => x != null), "Câu trả lời không hợp lệ.");
        Rules.Require(answers.Select(x => x.QuestionId).Distinct().Count() == answers.Length, "Câu hỏi bị lặp.");
        Rules.Require(answers.All(a => s.Questions.Any(q => q.Id == a.QuestionId)), "Câu hỏi thuộc khảo sát khác.");
        var response = new SurveyResponse { InvitationId = invitation.Id, SurveyId = surveyId };
        foreach (var q in s.Questions)
        {
            var a = answers.SingleOrDefault(x => x.QuestionId == q.Id);
            var selected = a?.OptionIds ?? [];
            var hasValue = a != null && (!string.IsNullOrWhiteSpace(a.Text) || a.Rating != null || selected.Length > 0);
            Rules.Require(!q.IsRequired || hasValue, $"Thiếu câu bắt buộc: {q.Text}");
            if (!hasValue) continue;
            Rules.Require(selected.Distinct().Count() == selected.Length && selected.All(x => q.Options.Any(o => o.Id == x)), "Lựa chọn không thuộc câu hỏi hoặc bị lặp.");
            var valid = q.Kind switch {
                QuestionKind.SingleChoice => selected.Length == 1 && a!.Rating == null && string.IsNullOrWhiteSpace(a.Text),
                QuestionKind.MultipleChoice => selected.Length >= 1 && a!.Rating == null && string.IsNullOrWhiteSpace(a.Text),
                QuestionKind.Rating => a!.Rating is >= 1 and <= 5 && selected.Length == 0 && string.IsNullOrWhiteSpace(a.Text),
                QuestionKind.Text => !string.IsNullOrWhiteSpace(a!.Text) && a.Text.Length <= 2000 && a.Rating == null && selected.Length == 0,
                _ => false };
            Rules.Require(valid, $"Sai kiểu câu trả lời: {q.Text}");
            response.Answers.Add(new() { SurveyId = surveyId, QuestionId = q.Id, TextValue = a!.Text?.Trim(), RatingValue = a.Rating,
                SelectedOptions = selected.Select(o => new SurveyAnswerOption { OptionId = o, QuestionId = q.Id }).ToList() });
        }
        db.SurveyResponses.Add(response); await db.SaveChangesAsync(); await tx.CommitAsync(); return response.Id;
    }
    public async Task<SurveyResults> Results(Guid id)
    {
        // Tỷ lệ tham gia = số người trả lời / số lời mời.
        // Tỷ lệ đáp án = số lượt chọn / số người trả lời CÂU ĐÓ (có thể khác số người trả lời khảo sát).
        // Câu chọn nhiều đáp án có thể có tổng tỷ lệ >100%; không cộng rồi ép về 100%.
        var s = await Load(id);
        var invited = await db.SurveyInvitations.CountAsync(x => x.SurveyId == id);
        var responses = await db.SurveyResponses.Include(x => x.Answers).ThenInclude(x => x.SelectedOptions).Where(x => x.SurveyId == id).ToListAsync();
        var results = s.Questions.OrderBy(x => x.Position).Select(q => {
            var aa = responses.SelectMany(x => x.Answers).Where(x => x.QuestionId == q.Id).ToList();
            var ratings = aa.Where(x => x.RatingValue != null).Select(x => x.RatingValue!.Value).ToList();
            return new QuestionResult(q.Id, q.Text, q.Kind, aa.Count,
                ratings.Count == 0 ? null : Math.Round((decimal)ratings.Average(),2),
                q.Options.OrderBy(x => x.Position).Select(o => { var count = aa.Count(a => a.SelectedOptions.Any(x => x.OptionId == o.Id));
                    return new OptionResult(o.Id, o.Text, count, Rules.Percent(count, aa.Count)); }).ToArray(),
                aa.Where(x => x.TextValue != null).Select(x => x.TextValue!).ToArray());
        }).ToArray();
        return new(id, s.Title, invited, responses.Count, Rules.Percent(responses.Count, invited), results);
    }
}
public sealed class ReportService(CrmDbContext db)
{
    public async Task<ReportDto> Overview()
    {
        var customers = await db.Customers.AsNoTracking().Include(x => x.Preferences).ThenInclude(x => x.Preference)
            .Include(x => x.User).Where(x => !x.IsDeleted).ToListAsync();
        var active = customers.Where(x => !x.User.IsDisabled).ToList();
        // Báo cáo tuổi/sở thích chỉ tính khách chưa xóa và không bị khóa.
        // Tính tuổi theo ngày Việt Nam, trừ một tuổi nếu năm nay chưa đến sinh nhật.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var groups = new Dictionary<string,int> { ["Dưới 18"] = 0, ["18 đến 24"] = 0, ["25 đến 34"] = 0, ["35 đến 44"] = 0, ["Từ 45"] = 0, ["Chưa có ngày sinh"] = 0 };
        foreach (var c in active) {
            if (c.BirthDate == null) { groups["Chưa có ngày sinh"]++; continue; }
            var birth = c.BirthDate.Value; var age = today.Year - birth.Year;
            if (birth > today.AddYears(-age)) age--;
            groups[age < 18 ? "Dưới 18" : age < 25 ? "18 đến 24" : age < 35 ? "25 đến 34" : age < 45 ? "35 đến 44" : "Từ 45"]++;
        }
        var ratings = await db.Feedbacks.Select(x => x.Rating).ToListAsync();
        // Phản hồi và khảo sát giữ toàn bộ lịch sử; khách đã lưu trữ vẫn có đóng góp trong các tổng này.
        var prefs = await db.Preferences.AsNoTracking().ToListAsync();
        return new(active.Count, customers.Count(x => x.User.IsDisabled), ratings.Count,
            ratings.Count == 0 ? null : Math.Round((decimal)ratings.Average(),2),
            await db.SurveyInvitations.CountAsync(), await db.SurveyResponses.CountAsync(),
            groups.Select(x => new AgeGroupDto(x.Key, x.Value, Rules.Percent(x.Value, active.Count))).ToArray(),
            prefs.Select(p => new PreferenceCountDto(p.Name, active.Count(c => c.Preferences.Any(x => x.PreferenceId == p.Id)))).ToArray());
    }
}
