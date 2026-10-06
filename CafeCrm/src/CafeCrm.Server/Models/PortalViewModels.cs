using CafeCrm.Contracts;

namespace CafeCrm.Server.Models;

// Dữ liệu tổng quan được giới hạn theo tài khoản đăng nhập tại controller.
public sealed record PortalOverview(ProfileDto Profile, InvitationDto[] Invitations, FeedbackDto[] Feedback);

public static class PortalLabels
{
    public static bool Available(InvitationDto invitation) => !invitation.HasResponded && !invitation.IsClosed && invitation.ClosesAtUtc > DateTime.UtcNow;
}
