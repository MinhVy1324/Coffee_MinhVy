using Microsoft.AspNetCore.Identity;

namespace CafeCrm.Server.Services;

// Identity vẫn băm/kiểm tra mật khẩu; lớp này chỉ đổi câu báo lỗi sang tiếng Việt.
public sealed class VietnameseIdentityErrors : IdentityErrorDescriber
{
    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Email này đã được đăng ký. Vui lòng dùng email khác hoặc đăng nhập.");
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Email này đã được đăng ký. Vui lòng dùng email khác hoặc đăng nhập.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Email không hợp lệ.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Email chứa ký tự không được hỗ trợ.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Mật khẩu cần ít nhất {length} ký tự.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Mật khẩu cần có chữ số.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Mật khẩu cần có chữ thường.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Mật khẩu cần có chữ hoa.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Mật khẩu cần có ký tự đặc biệt.");
    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), "Mật khẩu hiện tại chưa đúng.");
    public override IdentityError ConcurrencyFailure() => Error(nameof(ConcurrencyFailure), "Tài khoản vừa được cập nhật. Vui lòng tải lại và thử lại.");
}
