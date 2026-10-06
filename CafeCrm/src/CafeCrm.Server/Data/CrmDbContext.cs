using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CafeCrm.Server.Data;

public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Preference> Preferences => Set<Preference>();
    public DbSet<CustomerPreference> CustomerPreferences => Set<CustomerPreference>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<FeedbackReplyEntity> FeedbackReplies => Set<FeedbackReplyEntity>();
    public DbSet<Survey> Surveys => Set<Survey>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyOption> SurveyOptions => Set<SurveyOption>();
    public DbSet<SurveyInvitation> SurveyInvitations => Set<SurveyInvitation>();
    public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
    public DbSet<SurveyAnswer> SurveyAnswers => Set<SurveyAnswer>();
    public DbSet<SurveyAnswerOption> SurveyAnswerOptions => Set<SurveyAnswerOption>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        // Unique index và FK là lớp kiểm tra cuối tại database, kể cả request đến cùng lúc.
        // Một tài khoản chỉ có một hồ sơ; một điện thoại chỉ thuộc một hồ sơ chưa lưu trữ.
        b.Entity<Customer>().HasIndex(x => x.UserId).IsUnique();
        b.Entity<Customer>().HasIndex(x => x.Phone).IsUnique().HasFilter("[Phone] IS NOT NULL AND [IsDeleted] = 0");
        b.Entity<Customer>().Property(x => x.Phone).HasMaxLength(10);
        b.Entity<Customer>().Property(x => x.FullName).HasMaxLength(100);
        b.Entity<Customer>().Property(x => x.Address).HasMaxLength(250);
        b.Entity<Customer>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.Entity<CustomerPreference>().HasKey(x => new { x.CustomerId, x.PreferenceId });
        b.Entity<Product>().Property(x => x.Price).HasPrecision(12,2);
        b.Entity<Product>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<Supplier>().Property(x => x.Name).HasMaxLength(150);
        b.Entity<Feedback>().Property(x => x.Content).HasMaxLength(2000);
        b.Entity<Feedback>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.Entity<Feedback>().ToTable(t => t.HasCheckConstraint("CK_Feedback_Rating", "[Rating] BETWEEN 1 AND 5"));
        b.Entity<Survey>().Property(x => x.Title).HasMaxLength(150);
        b.Entity<Survey>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.Entity<SurveyQuestion>().HasAlternateKey(x => new { x.Id, x.SurveyId });
        b.Entity<SurveyQuestion>().HasIndex(x => new { x.SurveyId, x.Position }).IsUnique();
        b.Entity<SurveyOption>().HasAlternateKey(x => new { x.Id, x.QuestionId });
        b.Entity<SurveyInvitation>().HasIndex(x => new { x.SurveyId, x.CustomerId }).IsUnique();
        // Không mời trùng một khách trong một khảo sát và không trả lời trùng một lời mời.
        b.Entity<SurveyInvitation>().HasAlternateKey(x => new { x.Id, x.SurveyId });
        b.Entity<SurveyResponse>().HasIndex(x => x.InvitationId).IsUnique();
        b.Entity<SurveyResponse>().HasAlternateKey(x => new { x.Id, x.SurveyId });
        b.Entity<SurveyResponse>().HasOne(x => x.Invitation).WithMany()
            .HasForeignKey(x => new { x.InvitationId, x.SurveyId }).HasPrincipalKey(x => new { x.Id, x.SurveyId });
        b.Entity<SurveyAnswer>().HasIndex(x => new { x.ResponseId, x.QuestionId }).IsUnique();
        b.Entity<SurveyAnswer>().HasAlternateKey(x => new { x.Id, x.QuestionId });
        b.Entity<SurveyAnswer>().HasOne(x => x.Response).WithMany(x => x.Answers)
            .HasForeignKey(x => new { x.ResponseId, x.SurveyId }).HasPrincipalKey(x => new { x.Id, x.SurveyId });
        b.Entity<SurveyAnswer>().HasOne(x => x.Question).WithMany()
            .HasForeignKey(x => new { x.QuestionId, x.SurveyId }).HasPrincipalKey(x => new { x.Id, x.SurveyId });
        b.Entity<SurveyAnswer>().ToTable(t => t.HasCheckConstraint("CK_Answer_Rating", "[RatingValue] IS NULL OR [RatingValue] BETWEEN 1 AND 5"));
        b.Entity<SurveyAnswerOption>().HasKey(x => new { x.AnswerId, x.OptionId });
        b.Entity<SurveyAnswerOption>().HasOne(x => x.Answer).WithMany(x => x.SelectedOptions)
            .HasForeignKey(x => new { x.AnswerId, x.QuestionId }).HasPrincipalKey(x => new { x.Id, x.QuestionId });
        b.Entity<SurveyAnswerOption>().HasOne(x => x.Option).WithMany()
            .HasForeignKey(x => new { x.OptionId, x.QuestionId }).HasPrincipalKey(x => new { x.Id, x.QuestionId });
        // FK ghép Id + SurveyId/QuestionId ngăn đáp án thuộc khảo sát hoặc câu hỏi khác.
        // Giữ lịch sử CRM: Restrict chặn xóa dây chuyền; nghiệp vụ khách dùng xóa mềm.
        foreach (var entity in b.Model.GetEntityTypes().Where(x => !x.GetTableName()!.StartsWith("AspNet")))
            foreach (var fk in entity.GetForeignKeys()) fk.DeleteBehavior = DeleteBehavior.Restrict;
    }
}
