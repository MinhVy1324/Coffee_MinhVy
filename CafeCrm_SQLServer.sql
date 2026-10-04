IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [IsDisabled] bit NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AuditLogs] (
    [Id] bigint NOT NULL IDENTITY,
    [ActorUserId] nvarchar(max) NULL,
    [Action] nvarchar(max) NOT NULL,
    [Entity] nvarchar(max) NOT NULL,
    [EntityId] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
);

CREATE TABLE [Categories] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
);

CREATE TABLE [Preferences] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Preferences] PRIMARY KEY ([Id])
);

CREATE TABLE [Suppliers] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [Phone] nvarchar(max) NULL,
    [Email] nvarchar(max) NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Suppliers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Customers] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] nvarchar(450) NOT NULL,
    [FullName] nvarchar(100) NOT NULL,
    [Phone] nvarchar(10) NULL,
    [BirthDate] date NULL,
    [Address] nvarchar(250) NULL,
    [IsDeleted] bit NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ConcurrencyToken] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customers_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Surveys] (
    [Id] uniqueidentifier NOT NULL,
    [Title] nvarchar(150) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedByUserId] nvarchar(max) NOT NULL,
    [CreatedById] nvarchar(450) NULL,
    [Status] int NOT NULL,
    [ClosesAtUtc] datetime2 NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ConcurrencyToken] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Surveys] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Surveys_AspNetUsers_CreatedById] FOREIGN KEY ([CreatedById]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Products] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(150) NOT NULL,
    [CategoryId] int NOT NULL,
    [SupplierId] int NULL,
    [Price] decimal(12,2) NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Products_Suppliers_SupplierId] FOREIGN KEY ([SupplierId]) REFERENCES [Suppliers] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [CustomerPreferences] (
    [CustomerId] uniqueidentifier NOT NULL,
    [PreferenceId] int NOT NULL,
    CONSTRAINT [PK_CustomerPreferences] PRIMARY KEY ([CustomerId], [PreferenceId]),
    CONSTRAINT [FK_CustomerPreferences_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_CustomerPreferences_Preferences_PreferenceId] FOREIGN KEY ([PreferenceId]) REFERENCES [Preferences] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SurveyInvitations] (
    [Id] uniqueidentifier NOT NULL,
    [SurveyId] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [SentAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SurveyInvitations] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_SurveyInvitations_Id_SurveyId] UNIQUE ([Id], [SurveyId]),
    CONSTRAINT [FK_SurveyInvitations_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SurveyInvitations_Surveys_SurveyId] FOREIGN KEY ([SurveyId]) REFERENCES [Surveys] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SurveyQuestions] (
    [Id] uniqueidentifier NOT NULL,
    [SurveyId] uniqueidentifier NOT NULL,
    [Text] nvarchar(max) NOT NULL,
    [Kind] int NOT NULL,
    [IsRequired] bit NOT NULL,
    [Position] int NOT NULL,
    CONSTRAINT [PK_SurveyQuestions] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_SurveyQuestions_Id_SurveyId] UNIQUE ([Id], [SurveyId]),
    CONSTRAINT [FK_SurveyQuestions_Surveys_SurveyId] FOREIGN KEY ([SurveyId]) REFERENCES [Surveys] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [Feedbacks] (
    [Id] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [ProductId] int NULL,
    [Rating] int NOT NULL,
    [Content] nvarchar(2000) NOT NULL,
    [Status] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [ConcurrencyToken] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_Feedbacks] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Feedback_Rating] CHECK ([Rating] BETWEEN 1 AND 5),
    CONSTRAINT [FK_Feedbacks_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Feedbacks_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SurveyResponses] (
    [Id] uniqueidentifier NOT NULL,
    [InvitationId] uniqueidentifier NOT NULL,
    [SurveyId] uniqueidentifier NOT NULL,
    [SubmittedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_SurveyResponses] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_SurveyResponses_Id_SurveyId] UNIQUE ([Id], [SurveyId]),
    CONSTRAINT [FK_SurveyResponses_SurveyInvitations_InvitationId_SurveyId] FOREIGN KEY ([InvitationId], [SurveyId]) REFERENCES [SurveyInvitations] ([Id], [SurveyId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SurveyResponses_Surveys_SurveyId] FOREIGN KEY ([SurveyId]) REFERENCES [Surveys] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SurveyOptions] (
    [Id] uniqueidentifier NOT NULL,
    [QuestionId] uniqueidentifier NOT NULL,
    [Text] nvarchar(max) NOT NULL,
    [Position] int NOT NULL,
    CONSTRAINT [PK_SurveyOptions] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_SurveyOptions_Id_QuestionId] UNIQUE ([Id], [QuestionId]),
    CONSTRAINT [FK_SurveyOptions_SurveyQuestions_QuestionId] FOREIGN KEY ([QuestionId]) REFERENCES [SurveyQuestions] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [FeedbackReplies] (
    [Id] uniqueidentifier NOT NULL,
    [FeedbackId] uniqueidentifier NOT NULL,
    [StaffUserId] nvarchar(450) NOT NULL,
    [Content] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_FeedbackReplies] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_FeedbackReplies_AspNetUsers_StaffUserId] FOREIGN KEY ([StaffUserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_FeedbackReplies_Feedbacks_FeedbackId] FOREIGN KEY ([FeedbackId]) REFERENCES [Feedbacks] ([Id]) ON DELETE NO ACTION
);

CREATE TABLE [SurveyAnswers] (
    [Id] uniqueidentifier NOT NULL,
    [ResponseId] uniqueidentifier NOT NULL,
    [SurveyId] uniqueidentifier NOT NULL,
    [QuestionId] uniqueidentifier NOT NULL,
    [TextValue] nvarchar(max) NULL,
    [RatingValue] int NULL,
    CONSTRAINT [PK_SurveyAnswers] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_SurveyAnswers_Id_QuestionId] UNIQUE ([Id], [QuestionId]),
    CONSTRAINT [CK_Answer_Rating] CHECK ([RatingValue] IS NULL OR [RatingValue] BETWEEN 1 AND 5),
    CONSTRAINT [FK_SurveyAnswers_SurveyQuestions_QuestionId_SurveyId] FOREIGN KEY ([QuestionId], [SurveyId]) REFERENCES [SurveyQuestions] ([Id], [SurveyId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SurveyAnswers_SurveyResponses_ResponseId_SurveyId] FOREIGN KEY ([ResponseId], [SurveyId]) REFERENCES [SurveyResponses] ([Id], [SurveyId]) ON DELETE NO ACTION
);

CREATE TABLE [SurveyAnswerOptions] (
    [AnswerId] uniqueidentifier NOT NULL,
    [OptionId] uniqueidentifier NOT NULL,
    [QuestionId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_SurveyAnswerOptions] PRIMARY KEY ([AnswerId], [OptionId]),
    CONSTRAINT [FK_SurveyAnswerOptions_SurveyAnswers_AnswerId_QuestionId] FOREIGN KEY ([AnswerId], [QuestionId]) REFERENCES [SurveyAnswers] ([Id], [QuestionId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_SurveyAnswerOptions_SurveyOptions_OptionId_QuestionId] FOREIGN KEY ([OptionId], [QuestionId]) REFERENCES [SurveyOptions] ([Id], [QuestionId]) ON DELETE NO ACTION
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_CustomerPreferences_PreferenceId] ON [CustomerPreferences] ([PreferenceId]);

CREATE UNIQUE INDEX [IX_Customers_Phone] ON [Customers] ([Phone]) WHERE [Phone] IS NOT NULL AND [IsDeleted] = 0;

CREATE UNIQUE INDEX [IX_Customers_UserId] ON [Customers] ([UserId]);

CREATE INDEX [IX_FeedbackReplies_FeedbackId] ON [FeedbackReplies] ([FeedbackId]);

CREATE INDEX [IX_FeedbackReplies_StaffUserId] ON [FeedbackReplies] ([StaffUserId]);

CREATE INDEX [IX_Feedbacks_CustomerId] ON [Feedbacks] ([CustomerId]);

CREATE INDEX [IX_Feedbacks_ProductId] ON [Feedbacks] ([ProductId]);

CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);

CREATE INDEX [IX_Products_SupplierId] ON [Products] ([SupplierId]);

CREATE INDEX [IX_SurveyAnswerOptions_AnswerId_QuestionId] ON [SurveyAnswerOptions] ([AnswerId], [QuestionId]);

CREATE INDEX [IX_SurveyAnswerOptions_OptionId_QuestionId] ON [SurveyAnswerOptions] ([OptionId], [QuestionId]);

CREATE INDEX [IX_SurveyAnswers_QuestionId_SurveyId] ON [SurveyAnswers] ([QuestionId], [SurveyId]);

CREATE UNIQUE INDEX [IX_SurveyAnswers_ResponseId_QuestionId] ON [SurveyAnswers] ([ResponseId], [QuestionId]);

CREATE INDEX [IX_SurveyAnswers_ResponseId_SurveyId] ON [SurveyAnswers] ([ResponseId], [SurveyId]);

CREATE INDEX [IX_SurveyInvitations_CustomerId] ON [SurveyInvitations] ([CustomerId]);

CREATE UNIQUE INDEX [IX_SurveyInvitations_SurveyId_CustomerId] ON [SurveyInvitations] ([SurveyId], [CustomerId]);

CREATE INDEX [IX_SurveyOptions_QuestionId] ON [SurveyOptions] ([QuestionId]);

CREATE UNIQUE INDEX [IX_SurveyQuestions_SurveyId_Position] ON [SurveyQuestions] ([SurveyId], [Position]);

CREATE UNIQUE INDEX [IX_SurveyResponses_InvitationId] ON [SurveyResponses] ([InvitationId]);

CREATE INDEX [IX_SurveyResponses_InvitationId_SurveyId] ON [SurveyResponses] ([InvitationId], [SurveyId]);

CREATE INDEX [IX_SurveyResponses_SurveyId] ON [SurveyResponses] ([SurveyId]);

CREATE INDEX [IX_Surveys_CreatedById] ON [Surveys] ([CreatedById]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261001141659_InitialCreate', N'10.0.9');

COMMIT;
GO

