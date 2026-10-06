using System.ComponentModel.DataAnnotations;

namespace CafeCrm.Server.Models;

// ViewModel chỉ nhận các trường được phép trên form; người đăng ký không thể gửi thêm Role.
// Setter Trim chạy trước validation MVC. Mật khẩu giữ nguyên, tuyệt đối không Trim mật khẩu.
public sealed class LoginInput
{
    private string email = "";
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)]
    public string Email { get => email; set => email = value?.Trim() ?? ""; }
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(128)]
    public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterInput
{
    private string email = "", fullName = "";
    [Required(ErrorMessage = "Vui lòng nhập họ tên."), StringLength(100, ErrorMessage = "Họ tên không quá 100 ký tự.")]
    public string FullName { get => fullName; set => fullName = value?.Trim() ?? ""; }
    [Required(ErrorMessage = "Vui lòng nhập email."), EmailAddress(ErrorMessage = "Email không hợp lệ."), StringLength(256)]
    public string Email { get => email; set => email = value?.Trim() ?? ""; }
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu."), StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu cần từ 8 đến 128 ký tự.")]
    public string Password { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng nhập lại mật khẩu."), Compare(nameof(Password), ErrorMessage = "Hai mật khẩu chưa trùng nhau.")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class ChangePasswordInput
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu hiện tại."), StringLength(128)]
    public string CurrentPassword { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới."), StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu cần từ 8 đến 128 ký tự.")]
    public string NewPassword { get; set; } = "";
    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới."), Compare(nameof(NewPassword), ErrorMessage = "Hai mật khẩu mới chưa trùng nhau.")]
    public string ConfirmPassword { get; set; } = "";
}
