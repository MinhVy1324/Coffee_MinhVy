using System.ComponentModel.DataAnnotations;

namespace CafeCrm.Contracts;

public static class Roles
{
    // Nhân viên: tác nghiệp hằng ngày (thêm/sửa khách, phản hồi).
    // Quản lý: có quyền nhân viên và thêm khóa/xóa, báo cáo, khảo sát/kết quả.
    // Admin: quản trị tài khoản/danh mục; không mặc nhiên được xem nghiệp vụ CRM.
    // Khách hàng: chỉ thao tác trên hồ sơ, phản hồi và khảo sát được mời của chính mình.
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Staff = "Staff";
    public const string Customer = "Customer";
    public static readonly string[] All = [Admin, Manager, Staff, Customer];
}
public enum QuestionKind { SingleChoice, MultipleChoice, Rating, Text }
public enum SurveyStatus { Draft, Published, Closed }
public enum FeedbackStatus { New, InProgress, Resolved, Closed }
public sealed record RegisterRequest(
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)] string Email,
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu cần từ 8 đến 128 ký tự.")] string Password,
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100, ErrorMessage = "Họ tên không quá 100 ký tự.")] string FullName);
public sealed record LoginRequest([Required, EmailAddress, StringLength(256)] string Email, [Required, StringLength(128)] string Password);
public sealed record RefreshRequest([Required] string RefreshToken);
public sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn, string RefreshToken);
public sealed record ProfileDto(Guid Id, string Email, string FullName, string? Phone,
    DateOnly? BirthDate, string? Address, int[] PreferenceIds, Guid ConcurrencyToken);
public sealed record ProfileUpdate(
    // Không chọn checkbox thì trình duyệt không gửi PreferenceIds; null được service đổi thành [].
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100, ErrorMessage = "Họ tên không quá 100 ký tự.")] string FullName,
    [RegularExpression(@"^0[0-9]{9}$", ErrorMessage = "Số điện thoại gồm 10 chữ số và bắt đầu bằng 0.")] string? Phone,
    DateOnly? BirthDate, [StringLength(250, ErrorMessage = "Địa chỉ không quá 250 ký tự.")] string? Address,
    int[]? PreferenceIds, Guid ConcurrencyToken);
public sealed record CustomerCreate(
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)] string Email,
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100, ErrorMessage = "Họ tên không quá 100 ký tự.")] string FullName,
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu cần từ 8 đến 128 ký tự.")] string InitialPassword);
public sealed record CustomerRow(Guid Id, string Email, string FullName, string? Phone,
    DateOnly? BirthDate, bool IsDisabled, bool IsDeleted, Guid ConcurrencyToken);
public sealed record LookupDto(int Id, string Name);
public sealed record ProductDto(int Id, string Name, string Category, string? Supplier, decimal Price, bool IsActive);
public sealed record FeedbackCreate(int? ProductId, [Range(1,5, ErrorMessage = "Điểm đánh giá phải từ 1 đến 5.")] int Rating,
    [Required(ErrorMessage = "Vui lòng nhập nội dung phản hồi."), StringLength(2000, ErrorMessage = "Nội dung phản hồi không quá 2.000 ký tự.")] string Content);
public sealed record FeedbackDto(Guid Id, string CustomerName, string? ProductName, int Rating,
    string Content, FeedbackStatus Status, DateTime CreatedAtUtc, Guid ConcurrencyToken, ReplyDto[] Replies);
public sealed record ReplyDto(string Author, string Content, DateTime CreatedAtUtc);
public sealed record FeedbackReply([Required, StringLength(2000)] string Content,
    FeedbackStatus Status, Guid ConcurrencyToken);
public sealed record QuestionDraft([Required, StringLength(500)] string Text,
    QuestionKind Kind, bool IsRequired, string[] Options);
public sealed record SurveyDraft([Required, StringLength(150)] string Title,
    [StringLength(2000)] string? Description, DateTime ClosesAtUtc,
    QuestionDraft[] Questions);
public sealed record SurveyUpdate(SurveyDraft Draft, Guid ConcurrencyToken);
public sealed record OptionDto(Guid Id, string Text);
public sealed record QuestionDto(Guid Id, string Text, QuestionKind Kind, bool IsRequired, OptionDto[] Options);
public sealed record SurveyDto(Guid Id, string Title, string? Description, SurveyStatus Status,
    DateTime ClosesAtUtc, Guid ConcurrencyToken, QuestionDto[] Questions);
public sealed record InvitationDto(Guid Id, Guid SurveyId, string Title, DateTime ClosesAtUtc, bool HasResponded, bool IsClosed = false);
public sealed record PublishRequest(Guid[]? CustomerIds, Guid ConcurrencyToken);
public sealed record AnswerInput(Guid QuestionId, string? Text, int? Rating, Guid[] OptionIds);
public sealed record SubmitResponse(AnswerInput[] Answers);
// Bản trả lời riêng của khách: chỉ đọc, không sửa sau khi đã gửi.
public sealed record SurveyResponseDto(DateTime SubmittedAtUtc, AnswerInput[] Answers);
public sealed record SurveyResults(Guid SurveyId, string Title, int Invited, int Responded,
    decimal ResponseRate, QuestionResult[] Questions);
public sealed record QuestionResult(Guid QuestionId, string Text, QuestionKind Kind,
    int Answered, decimal? AverageRating, OptionResult[] Options, string[] TextAnswers);
public sealed record OptionResult(Guid OptionId, string Text, int Count, decimal Percentage);
public sealed record ReportDto(int ActiveCustomers, int DisabledCustomers, int FeedbackCount,
    decimal? AverageFeedbackRating, int Invited, int Responded, AgeGroupDto[] AgeGroups,
    PreferenceCountDto[] Preferences);
public sealed record AgeGroupDto(string Group, int Count, decimal Percentage);
public sealed record PreferenceCountDto(string Name, int Count);
public sealed record UserRow(string Id, string Email, bool IsDisabled, string[] Roles);
public sealed record UserCreate([Required, EmailAddress] string Email,
    [Required, MinLength(8)] string InitialPassword, [Required] string Role);
public sealed record RoleChange([Required] string Role);
