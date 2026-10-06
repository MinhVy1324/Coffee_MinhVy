using System.ComponentModel.DataAnnotations;
using CafeCrm.Contracts;

namespace CafeCrm.Server.Models;

// Model dành cho form MVC: kiểm tra dữ liệu trước khi gọi service chung của web và API.
public sealed class CustomerCreateInput
{
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100)]
    public string FullName { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu ban đầu."), MinLength(8, ErrorMessage = "Mật khẩu cần ít nhất 8 ký tự.")]
    public string InitialPassword { get; set; } = "";
}

public sealed class SurveyPublishViewModel
{
    public required SurveyDto Survey { get; init; }
    public required CustomerRow[] Customers { get; init; }
    public string Audience { get; init; } = "all";
    public Guid[] SelectedIds { get; init; } = [];
}

// Chỉ dịch nhãn hiển thị; tên enum và vai trò gửi về server vẫn giữ nguyên.
public static class CrmLabels
{
    public static string Feedback(FeedbackStatus value) => value switch {
        FeedbackStatus.New => "Chờ tiếp nhận", FeedbackStatus.InProgress => "Đang xử lý",
        FeedbackStatus.Resolved => "Đã giải quyết", FeedbackStatus.Closed => "Đã đóng", _ => "Không xác định"
    };
    public static string Survey(SurveyStatus value) => value switch {
        SurveyStatus.Draft => "Bản nháp", SurveyStatus.Published => "Đang phát hành",
        SurveyStatus.Closed => "Đã đóng", _ => "Không xác định"
    };
    public static string Question(QuestionKind value) => value switch {
        QuestionKind.SingleChoice => "Chọn một đáp án", QuestionKind.MultipleChoice => "Chọn nhiều đáp án",
        QuestionKind.Rating => "Chấm điểm từ 1 đến 5", QuestionKind.Text => "Trả lời bằng văn bản", _ => "Không xác định"
    };
}
