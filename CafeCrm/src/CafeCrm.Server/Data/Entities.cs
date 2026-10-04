using CafeCrm.Contracts;
using Microsoft.AspNetCore.Identity;

namespace CafeCrm.Server.Data;

public sealed class AppUser : IdentityUser
{
    public bool IsDisabled { get; set; }
}
public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Address { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public List<CustomerPreference> Preferences { get; set; } = [];
}
public sealed class Preference { public int Id { get; set; } public string Name { get; set; } = ""; }
public sealed class CustomerPreference
{
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int PreferenceId { get; set; }
    public Preference Preference { get; set; } = null!;
}
public sealed class Category { public int Id { get; set; } public string Name { get; set; } = ""; }
public sealed class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
}
public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}
public sealed class Feedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    public int Rating { get; set; }
    public string Content { get; set; } = "";
    public FeedbackStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public List<FeedbackReplyEntity> Replies { get; set; } = [];
}
public sealed class FeedbackReplyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FeedbackId { get; set; }
    public Feedback Feedback { get; set; } = null!;
    public string StaffUserId { get; set; } = "";
    public AppUser StaffUser { get; set; } = null!;
    public string Content { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class Survey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public AppUser CreatedBy { get; set; } = null!;
    public SurveyStatus Status { get; set; }
    public DateTime ClosesAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public List<SurveyQuestion> Questions { get; set; } = [];
}
public sealed class SurveyQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyId { get; set; }
    public Survey Survey { get; set; } = null!;
    public string Text { get; set; } = "";
    public QuestionKind Kind { get; set; }
    public bool IsRequired { get; set; }
    public int Position { get; set; }
    public List<SurveyOption> Options { get; set; } = [];
}
public sealed class SurveyOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public SurveyQuestion Question { get; set; } = null!;
    public string Text { get; set; } = "";
    public int Position { get; set; }
}
public sealed class SurveyInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SurveyId { get; set; }
    public Survey Survey { get; set; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
}
public sealed class SurveyResponse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvitationId { get; set; }
    public SurveyInvitation Invitation { get; set; } = null!;
    public Guid SurveyId { get; set; }
    public Survey Survey { get; set; } = null!;
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SurveyAnswer> Answers { get; set; } = [];
}
public sealed class SurveyAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ResponseId { get; set; }
    public SurveyResponse Response { get; set; } = null!;
    public Guid SurveyId { get; set; }
    public Guid QuestionId { get; set; }
    public SurveyQuestion Question { get; set; } = null!;
    public string? TextValue { get; set; }
    public int? RatingValue { get; set; }
    public List<SurveyAnswerOption> SelectedOptions { get; set; } = [];
}
public sealed class SurveyAnswerOption
{
    public Guid AnswerId { get; set; }
    public SurveyAnswer Answer { get; set; } = null!;
    public Guid OptionId { get; set; }
    public SurveyOption Option { get; set; } = null!;
    public Guid QuestionId { get; set; }
}
public sealed class AuditLog
{
    public long Id { get; set; }
    public string? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
