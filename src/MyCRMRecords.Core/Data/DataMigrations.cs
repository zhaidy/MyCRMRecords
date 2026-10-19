namespace MyCRMRecords.Data;

/// <summary>
/// A change to the database: its T-SQL (batches separated by GO lines), run with its record in dbo.CrMigrations in
/// one transaction. Ids start with the date and never change once deployed; a deployed migration is never edited or
/// removed: a new one is added instead (CLAUDE.md).
/// </summary>
public sealed record DataMigration(string Id, string Title, string Description, string Sql);

/// <summary>
/// The data migrations, in order. An administrator runs them on the admin page (switching to a new SQL Server database
/// runs them all); nobody pastes SQL into SQL Server Management Studio.
/// </summary>
public static class DataMigrations
{
    public const string Table = "dbo.CrMigrations";

    /// <summary>Records which migrations have run; created before the first one runs.</summary>
    public const string CreateTableSql = """
        IF OBJECT_ID(N'dbo.CrMigrations', N'U') IS NULL
        CREATE TABLE dbo.CrMigrations (
            [Id] nvarchar(128) NOT NULL CONSTRAINT PK_CvMigrations PRIMARY KEY,
            [Title] nvarchar(400) NOT NULL,
            [AppliedBy] nvarchar(400) NOT NULL,
            [AppliedAtUtc] datetime2(3) NOT NULL
        );
        """;

    public static readonly IReadOnlyList<DataMigration> All =
    [
        new("2026-10-19-create-tables", "Create the tables",
            "Creates MyCRMRecords's tables: the feedbacks with their tasks and files; the divisions, feedback categories and mediums; the emails sent and the scheduled task runs; people, roles and the audit log; and the tables of the two-way sync with MyCRM (which record here is which row there, every sync, and every value the sync wrote in MyCRM with the value it replaced). Nothing is changed in MyCRM.",
            CreateTables),
    ];

    public static DataMigration? Find(string id) => All.FirstOrDefault(m => m.Id == id);

    private const string CreateTables = """
        CREATE TABLE dbo.CrEmail (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [At] datetime2(3) NOT NULL,
            [RequestKind] nvarchar(20) NOT NULL,
            [RequestId] bigint NOT NULL,
            [To] nvarchar(4000) NOT NULL,
            [Cc] nvarchar(4000) NOT NULL,
            [Subject] nvarchar(500) NOT NULL,
            [Sent] bit NOT NULL,
            [Error] nvarchar(4000) NOT NULL,
            CONSTRAINT PK_CvEmail PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvEmail_At ON dbo.CrEmail ([At]);
        GO
        CREATE TABLE dbo.CrJobRun (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Job] nvarchar(50) NOT NULL,
            [StartedAt] datetime2(3) NOT NULL,
            [FinishedAt] datetime2(3) NULL,
            [Outcome] nvarchar(20) NOT NULL,
            [By] nvarchar(600) NOT NULL,
            [Processed] int NOT NULL,
            [Summary] nvarchar(max) NOT NULL,
            [Watermark] datetime2(3) NULL,
            CONSTRAINT PK_CvJobRun PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvJobRun_Job ON dbo.CrJobRun ([Job]);
        CREATE INDEX IX_CvJobRun_StartedAt ON dbo.CrJobRun ([StartedAt]);
        GO
        CREATE TABLE dbo.CrUser (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Login] nvarchar(256) NOT NULL,
            [DisplayName] nvarchar(256) NOT NULL,
            [Email] nvarchar(256) NOT NULL,
            [LastSeenAt] datetime2(3) NULL,
            CONSTRAINT PK_CvUser PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvUser_Login ON dbo.CrUser ([Login]);
        GO
        CREATE TABLE dbo.CrUserRole (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Login] nvarchar(256) NOT NULL,
            [Role] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            CONSTRAINT PK_CvUserRole PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvUserRole_Login ON dbo.CrUserRole ([Login]);
        GO
        CREATE TABLE dbo.CrAudit (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [At] datetime2(3) NOT NULL,
            [Login] nvarchar(256) NOT NULL,
            [DisplayName] nvarchar(256) NOT NULL,
            [ImpersonatorLogin] nvarchar(256) NOT NULL,
            [ImpersonatorName] nvarchar(256) NOT NULL,
            [Action] nvarchar(200) NOT NULL,
            [Method] nvarchar(10) NOT NULL,
            [Path] nvarchar(500) NOT NULL,
            [Target] nvarchar(500) NOT NULL,
            [Status] int NOT NULL,
            [Succeeded] bit NOT NULL,
            [Message] nvarchar(4000) NOT NULL,
            [Details] nvarchar(max) NOT NULL,
            [Address] nvarchar(100) NOT NULL,
            CONSTRAINT PK_CvAudit PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvAudit_At ON dbo.CrAudit ([At]);
        GO
        CREATE TABLE dbo.CrSyncLink (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Entity] nvarchar(50) NOT NULL,
            [LocalId] bigint NOT NULL,
            [OldKey] nvarchar(400) NOT NULL,
            [LocalHash] nvarchar(64) NOT NULL,
            [OldHash] nvarchar(64) NOT NULL,
            [RemovedHere] bit NOT NULL,
            [RemovedThere] bit NOT NULL,
            [Pending] bit NOT NULL,
            [SyncedOn] datetime2(3) NOT NULL,
            [Unpaired] bit NOT NULL,
            CONSTRAINT PK_CvSyncLink PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvSyncLink_Entity ON dbo.CrSyncLink ([Entity]);
        CREATE INDEX IX_CvSyncLink_LocalId ON dbo.CrSyncLink ([LocalId]);
        CREATE INDEX IX_CvSyncLink_OldKey ON dbo.CrSyncLink ([OldKey]);
        GO
        CREATE TABLE dbo.CrSyncRun (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [StartedOn] datetime2(3) NOT NULL,
            [FinishedOn] datetime2(3) NULL,
            [By] nvarchar(256) NOT NULL,
            [Ok] bit NOT NULL,
            [Message] nvarchar(1000) NOT NULL,
            [ToOldApp] int NOT NULL,
            [ToHere] int NOT NULL,
            [Details] nvarchar(max) NOT NULL,
            CONSTRAINT PK_CvSyncRun PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvSyncRun_StartedOn ON dbo.CrSyncRun ([StartedOn]);
        GO
        CREATE TABLE dbo.CrSyncChange (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [RunId] bigint NOT NULL,
            [ChangedOn] datetime2(3) NOT NULL,
            [OldTable] nvarchar(100) NOT NULL,
            [OldKey] nvarchar(400) NOT NULL,
            [Record] nvarchar(200) NOT NULL,
            [Action] nvarchar(20) NOT NULL,
            [Column] nvarchar(128) NOT NULL,
            [Before] nvarchar(max) NOT NULL,
            [After] nvarchar(max) NOT NULL,
            CONSTRAINT PK_CvSyncChange PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvSyncChange_RunId ON dbo.CrSyncChange ([RunId]);
        CREATE INDEX IX_CvSyncChange_ChangedOn ON dbo.CrSyncChange ([ChangedOn]);
        CREATE INDEX IX_CvSyncChange_OldKey ON dbo.CrSyncChange ([OldKey]);
        GO
        CREATE TABLE dbo.CrFeedbackCategory (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Label] nvarchar(50) NOT NULL,
            [SortOrder] int NOT NULL,
            [IsActive] bit NOT NULL,
            [Removed] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CvFeedbackCategory PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvFeedbackCategory_OutSystemsId ON dbo.CrFeedbackCategory ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrFeedbackMedium (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Label] nvarchar(50) NOT NULL,
            [SortOrder] int NOT NULL,
            [IsActive] bit NOT NULL,
            [Removed] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CvFeedbackMedium PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvFeedbackMedium_OutSystemsId ON dbo.CrFeedbackMedium ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrFeedbackDivision (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Name] nvarchar(50) NOT NULL,
            [PersonInCharge] nvarchar(256) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CvFeedbackDivision PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvFeedbackDivision_OutSystemsId ON dbo.CrFeedbackDivision ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrFeedback (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [FeedbackGroup] nvarchar(50) NOT NULL,
            [Status] nvarchar(50) NOT NULL,
            [PersonResponsible] nvarchar(256) NOT NULL,
            [PersonResponsibleDivision] nvarchar(50) NOT NULL,
            [DivisionId] bigint NULL,
            [CompanyName] nvarchar(500) NOT NULL,
            [CompanyAddress] nvarchar(max) NOT NULL,
            [PostalCode] nvarchar(50) NOT NULL,
            [CompanyPhone] nvarchar(20) NOT NULL,
            [CompanyFax] nvarchar(20) NOT NULL,
            [CompanyEmail] nvarchar(250) NOT NULL,
            [ContactName] nvarchar(50) NOT NULL,
            [MediumId] bigint NULL,
            [DateReceived] datetime2(3) NULL,
            [TimeReceived] nvarchar(8) NOT NULL,
            [FeedbackType] nvarchar(50) NOT NULL,
            [CategoryId] bigint NULL,
            [Description] nvarchar(max) NOT NULL,
            [StaffResponsibleForProblem] nvarchar(256) NOT NULL,
            [Cc] nvarchar(max) NOT NULL,
            [IsAcknowledged] bit NOT NULL,
            [AcknowledgedType] nvarchar(50) NOT NULL,
            [AcknowledgedDate] datetime2(3) NULL,
            [AcknowledgedTime] nvarchar(8) NOT NULL,
            [SignOff] nvarchar(256) NOT NULL,
            [FirstLookAtFeedback] nvarchar(max) NOT NULL,
            [RootCause] nvarchar(max) NOT NULL,
            [CorrectiveAction] nvarchar(max) NOT NULL,
            [StaffResponsibleForSolution] nvarchar(256) NOT NULL,
            [SatisfactionLevel] nvarchar(50) NOT NULL,
            [CreatedOn] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [CreatedByUserName] nvarchar(500) NOT NULL,
            [UpdatedOn] datetime2(3) NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [UpdatedByUserName] nvarchar(500) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [SessionId] nvarchar(50) NOT NULL,
            [AcknowledgedHours] int NOT NULL,
            [StartResolutionDays] int NOT NULL,
            [CloseCaseDays] int NOT NULL,
            [StartedResolutionOn] datetime2(3) NULL,
            [CompletedResolutionOn] datetime2(3) NULL,
            [IsCompleted] bit NOT NULL,
            [CreatorHasSolution] bit NOT NULL,
            [SolutionDetail] nvarchar(max) NOT NULL,
            [ExpectedOutcome] nvarchar(max) NOT NULL,
            [SuggestionMeetCriteria] nvarchar(500) NOT NULL,
            [RicohWayValue] nvarchar(500) NOT NULL,
            [HrAccepted] nvarchar(50) NOT NULL,
            [HrRemarks] nvarchar(max) NOT NULL,
            [HrUpdatedOn] datetime2(3) NULL,
            [HrUpdatedBy] nvarchar(256) NOT NULL,
            [PreventiveAction] nvarchar(max) NOT NULL,
            [MightCauseCustomerComplaint] bit NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CvFeedback PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvFeedback_Status ON dbo.CrFeedback ([Status]);
        CREATE INDEX IX_CvFeedback_PersonResponsible ON dbo.CrFeedback ([PersonResponsible]);
        CREATE INDEX IX_CvFeedback_CreatedBy ON dbo.CrFeedback ([CreatedBy]);
        CREATE INDEX IX_CvFeedback_OutSystemsId ON dbo.CrFeedback ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrTask (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [FeedbackId] bigint NOT NULL,
            [ActionPlan] nvarchar(max) NOT NULL,
            [Remarks] nvarchar(max) NOT NULL,
            [PersonInCharge] nvarchar(256) NOT NULL,
            [DueDate] datetime2(3) NULL,
            [SlaExpectedCompletionDate] datetime2(3) NULL,
            [IsCompleted] bit NOT NULL,
            [CreatedOn] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [SessionId] nvarchar(50) NOT NULL,
            [TaskType] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CvTask PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvTask_FeedbackId ON dbo.CrTask ([FeedbackId]);
        CREATE INDEX IX_CvTask_PersonInCharge ON dbo.CrTask ([PersonInCharge]);
        CREATE INDEX IX_CvTask_OutSystemsId ON dbo.CrTask ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrFeedbackFile (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [FeedbackId] bigint NOT NULL,
            [FileName] nvarchar(500) NOT NULL,
            [Content] varbinary(max) NOT NULL,
            [Kind] nvarchar(50) NOT NULL,
            [UploadedBy] nvarchar(256) NOT NULL,
            [UploadedOn] datetime2(3) NULL,
            [SessionId] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CvFeedbackFile PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CvFeedbackFile_FeedbackId ON dbo.CrFeedbackFile ([FeedbackId]);
        CREATE INDEX IX_CvFeedbackFile_OutSystemsId ON dbo.CrFeedbackFile ([OutSystemsId]);
        """;
}
