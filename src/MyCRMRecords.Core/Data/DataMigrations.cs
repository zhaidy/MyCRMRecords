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
            "Creates MyCRMRecords's tables: the teams and CRM users; the companies with their group names, caretake remarks, relationships, addresses, contacts, phones and emails; the territories with their companies and movements; the emails sent and the scheduled task runs; people, roles and the audit log; and the tables of the two-way sync with MyCRM (which record here is which row there, every sync, and every value the sync wrote in MyCRM with the value it replaced). Nothing is changed in MyCRM.",
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
            CONSTRAINT PK_CrEmail PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrEmail_At ON dbo.CrEmail ([At]);
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
            CONSTRAINT PK_CrJobRun PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrJobRun_Job ON dbo.CrJobRun ([Job]);
        CREATE INDEX IX_CrJobRun_StartedAt ON dbo.CrJobRun ([StartedAt]);
        GO
        CREATE TABLE dbo.CrUser (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Login] nvarchar(256) NOT NULL,
            [DisplayName] nvarchar(256) NOT NULL,
            [Email] nvarchar(256) NOT NULL,
            [LastSeenAt] datetime2(3) NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            CONSTRAINT PK_CrUser PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrUser_Login ON dbo.CrUser ([Login]);
        GO
        CREATE TABLE dbo.CrUserRole (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Login] nvarchar(256) NOT NULL,
            [Role] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            CONSTRAINT PK_CrUserRole PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrUserRole_Login ON dbo.CrUserRole ([Login]);
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
            CONSTRAINT PK_CrAudit PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrAudit_At ON dbo.CrAudit ([At]);
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
            CONSTRAINT PK_CrSyncLink PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrSyncLink_Entity ON dbo.CrSyncLink ([Entity]);
        CREATE INDEX IX_CrSyncLink_LocalId ON dbo.CrSyncLink ([LocalId]);
        CREATE INDEX IX_CrSyncLink_OldKey ON dbo.CrSyncLink ([OldKey]);
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
            CONSTRAINT PK_CrSyncRun PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrSyncRun_StartedOn ON dbo.CrSyncRun ([StartedOn]);
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
            CONSTRAINT PK_CrSyncChange PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrSyncChange_RunId ON dbo.CrSyncChange ([RunId]);
        CREATE INDEX IX_CrSyncChange_ChangedOn ON dbo.CrSyncChange ([ChangedOn]);
        CREATE INDEX IX_CrSyncChange_OldKey ON dbo.CrSyncChange ([OldKey]);
        GO
        CREATE TABLE dbo.CrTeam (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Name] nvarchar(255) NOT NULL,
            [ShortName] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrTeam PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrTeam_OutSystemsId ON dbo.CrTeam ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCrmUser (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Login] nvarchar(256) NOT NULL,
            [Role] nvarchar(50) NOT NULL,
            [TeamId] bigint NULL,
            [IsActive] bit NOT NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [CreatedAt] datetime2(3) NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [UpdatedAt] datetime2(3) NULL,
            [Designation] nvarchar(50) NOT NULL,
            [PhoneNumber] nvarchar(20) NOT NULL,
            [SalesmanCode] nvarchar(50) NOT NULL,
            [CId] int NOT NULL,
            [ProfileId] bigint NULL,
            [TerritoryCode] nvarchar(50) NOT NULL,
            [FormerTeamId] bigint NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCrmUser PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCrmUser_Login ON dbo.CrCrmUser ([Login]);
        CREATE INDEX IX_CrCrmUser_OutSystemsId ON dbo.CrCrmUser ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompanyGroupName (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [GroupName] nvarchar(255) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompanyGroupName PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompanyGroupName_OutSystemsId ON dbo.CrCompanyGroupName ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompany (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Name] nvarchar(250) NOT NULL,
            [Brn] nvarchar(50) NOT NULL,
            [Website] nvarchar(100) NOT NULL,
            [IsActive] bit NOT NULL,
            [IncCertNo] nvarchar(50) NOT NULL,
            [NoOfEmployees] int NOT NULL,
            [PaidUpCapital] int NOT NULL,
            [CompanyGroup] nvarchar(50) NOT NULL,
            [CompanyGroupNameId] bigint NULL,
            [CrmCompanyId] bigint NOT NULL,
            [CreatedDate] datetime2(3) NULL,
            [UpdatedDate] datetime2(3) NULL,
            [CompAccountType] nvarchar(50) NOT NULL,
            [CompOwnership] nvarchar(50) NOT NULL,
            [IsCaretake] bit NOT NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [Remarks] nvarchar(max) NOT NULL,
            [IsChannelPartner] bit NOT NULL,
            [Phone] nvarchar(20) NOT NULL,
            [Fax] nvarchar(20) NOT NULL,
            [IsCreatedByMsd] bit NOT NULL,
            [MifPotential] int NOT NULL,
            [Revenue] decimal(37, 8) NOT NULL,
            [CompanyIndustry] nvarchar(50) NOT NULL,
            [PreName] nvarchar(250) NOT NULL,
            [PreBrn] nvarchar(50) NOT NULL,
            [PreWebsite] nvarchar(100) NOT NULL,
            [PreIsActive] bit NOT NULL,
            [PreIncCertNo] nvarchar(50) NOT NULL,
            [PreNoOfEmployees] int NOT NULL,
            [PrePaidUpCapital] int NOT NULL,
            [PreCompanyGroup] nvarchar(50) NOT NULL,
            [PreCompanyGroupNameId] bigint NULL,
            [PreCompAccountType] nvarchar(50) NOT NULL,
            [PreCompOwnership] nvarchar(50) NOT NULL,
            [PreIsCaretake] bit NOT NULL,
            [PreRemarks] nvarchar(max) NOT NULL,
            [PreIsChannelPartner] bit NOT NULL,
            [PrePhone] nvarchar(20) NOT NULL,
            [PreFax] nvarchar(20) NOT NULL,
            [PreMifPotential] int NOT NULL,
            [PreRevenue] decimal(37, 8) NOT NULL,
            [PreCompanyIndustry] nvarchar(50) NOT NULL,
            [CustomerType] nvarchar(50) NOT NULL,
            [IsChannelPartnerForVss] bit NOT NULL,
            [IsChannelPartnerForCs] bit NOT NULL,
            [CustomerNo] nvarchar(50) NOT NULL,
            [CreditTerms] nvarchar(50) NOT NULL,
            [SalesRemarks] nvarchar(max) NOT NULL,
            [AdvanceCustomerType] nvarchar(50) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompany PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompany_Brn ON dbo.CrCompany ([Brn]);
        CREATE INDEX IX_CrCompany_OutSystemsId ON dbo.CrCompany ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompanyCaretake (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [CompanyId] bigint NOT NULL,
            [CaretakeAccountRemarks] nvarchar(max) NOT NULL,
            [UpdatedDate] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [DateCreated] datetime2(3) NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompanyCaretake PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompanyCaretake_CompanyId ON dbo.CrCompanyCaretake ([CompanyId]);
        CREATE INDEX IX_CrCompanyCaretake_OutSystemsId ON dbo.CrCompanyCaretake ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompanyRelationship (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [CompanyId] bigint NOT NULL,
            [RelatedCompanyId] bigint NOT NULL,
            [RelationshipType] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompanyRelationship PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompanyRelationship_CompanyId ON dbo.CrCompanyRelationship ([CompanyId]);
        CREATE INDEX IX_CrCompanyRelationship_RelatedCompanyId ON dbo.CrCompanyRelationship ([RelatedCompanyId]);
        CREATE INDEX IX_CrCompanyRelationship_OutSystemsId ON dbo.CrCompanyRelationship ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrAddress (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [AddressType] nvarchar(50) NOT NULL,
            [UnitNumber] nvarchar(2000) NOT NULL,
            [Block] nvarchar(2000) NOT NULL,
            [Street] nvarchar(2000) NOT NULL,
            [BuildingName] nvarchar(2000) NOT NULL,
            [PostalCode] nvarchar(50) NOT NULL,
            [IsActive] bit NOT NULL,
            [CrmAddressId] bigint NOT NULL,
            [IsDefault] bit NOT NULL,
            [Country] nvarchar(255) NOT NULL,
            [City] nvarchar(255) NOT NULL,
            [StateProvince] nvarchar(255) NOT NULL,
            [PreAddressType] nvarchar(50) NOT NULL,
            [PreUnitNumber] nvarchar(2000) NOT NULL,
            [PreBlock] nvarchar(2000) NOT NULL,
            [PreStreet] nvarchar(2000) NOT NULL,
            [PreBuildingName] nvarchar(2000) NOT NULL,
            [PrePostalCode] nvarchar(50) NOT NULL,
            [PreCountry] nvarchar(255) NOT NULL,
            [PreCity] nvarchar(255) NOT NULL,
            [PreStateProvince] nvarchar(255) NOT NULL,
            [PreIsActive] bit NOT NULL,
            [CreatedAt] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [UpdatedAt] datetime2(3) NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrAddress PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrAddress_OutSystemsId ON dbo.CrAddress ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompanyAddressLink (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [CompanyId] bigint NOT NULL,
            [AddressId] bigint NOT NULL,
            [CrmAddressLink] bigint NOT NULL,
            [IsActive] bit NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompanyAddressLink PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompanyAddressLink_CompanyId ON dbo.CrCompanyAddressLink ([CompanyId]);
        CREATE INDEX IX_CrCompanyAddressLink_AddressId ON dbo.CrCompanyAddressLink ([AddressId]);
        CREATE INDEX IX_CrCompanyAddressLink_OutSystemsId ON dbo.CrCompanyAddressLink ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrContact (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [FirstName] nvarchar(250) NOT NULL,
            [LastName] nvarchar(250) NOT NULL,
            [IsActive] bit NOT NULL,
            [Department] nvarchar(50) NOT NULL,
            [CId] bigint NOT NULL,
            [MiddleName] nvarchar(250) NOT NULL,
            [CrmContactLinkId] bigint NOT NULL,
            [Designation] nvarchar(250) NOT NULL,
            [Salutation] nvarchar(250) NOT NULL,
            [CreatedAt] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [UpdatedAt] datetime2(3) NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [PreFirstName] nvarchar(250) NOT NULL,
            [PreLastName] nvarchar(250) NOT NULL,
            [PreIsActive] bit NOT NULL,
            [PreDepartment] nvarchar(50) NOT NULL,
            [PreMiddleName] nvarchar(250) NOT NULL,
            [PreDesignation] nvarchar(250) NOT NULL,
            [PreSalutation] nvarchar(250) NOT NULL,
            [IsLinkedIn] bit NOT NULL,
            [IsEqList] bit NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrContact PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrContact_OutSystemsId ON dbo.CrContact ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompanyContactLink (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [CompanyId] bigint NOT NULL,
            [ContactId] bigint NOT NULL,
            [CId] bigint NOT NULL,
            [IsDefault] bit NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompanyContactLink PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompanyContactLink_CompanyId ON dbo.CrCompanyContactLink ([CompanyId]);
        CREATE INDEX IX_CrCompanyContactLink_ContactId ON dbo.CrCompanyContactLink ([ContactId]);
        CREATE INDEX IX_CrCompanyContactLink_OutSystemsId ON dbo.CrCompanyContactLink ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrPhone (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [CompanyId] bigint NULL,
            [ContactId] bigint NULL,
            [PhoneType] nvarchar(50) NOT NULL,
            [Number] nvarchar(20) NOT NULL,
            [IsActive] bit NOT NULL,
            [CrmPhoneId] bigint NOT NULL,
            [CrmPhoneLinkId] bigint NOT NULL,
            [CreatedAt] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [UpdatedAt] datetime2(3) NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [PrePhone] nvarchar(20) NOT NULL,
            [PrePhoneType] nvarchar(50) NOT NULL,
            [PreIsActive] bit NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrPhone PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrPhone_ContactId ON dbo.CrPhone ([ContactId]);
        CREATE INDEX IX_CrPhone_OutSystemsId ON dbo.CrPhone ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrContactEmail (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Address] nvarchar(200) NOT NULL,
            [CompanyId] bigint NULL,
            [ContactId] bigint NULL,
            [IsActive] bit NOT NULL,
            [Cid] bigint NOT NULL,
            [CrmEmailLinkId] bigint NOT NULL,
            [CreatedAt] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [UpdatedAt] datetime2(3) NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [PreEmail] nvarchar(250) NOT NULL,
            [PreIsActive] bit NOT NULL,
            [ConsentToMarketingInfo] bit NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrContactEmail PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrContactEmail_ContactId ON dbo.CrContactEmail ([ContactId]);
        CREATE INDEX IX_CrContactEmail_OutSystemsId ON dbo.CrContactEmail ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrTerritory (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [Name] nvarchar(100) NOT NULL,
            [Number] bigint NOT NULL,
            [Owner] nvarchar(256) NOT NULL,
            [IsActive] bit NOT NULL,
            [IsOwner] bit NOT NULL,
            [CreatedOn] datetime2(3) NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrTerritory PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrTerritory_Owner ON dbo.CrTerritory ([Owner]);
        CREATE INDEX IX_CrTerritory_OutSystemsId ON dbo.CrTerritory ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrCompanyTerritory (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [TerritoryId] bigint NOT NULL,
            [CompanyId] bigint NOT NULL,
            [IsActive] bit NOT NULL,
            [CreatedBy] nvarchar(256) NOT NULL,
            [CreatedOn] datetime2(3) NULL,
            [RemovedOn] datetime2(3) NULL,
            [RemovedBy] nvarchar(600) NOT NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrCompanyTerritory PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrCompanyTerritory_TerritoryId ON dbo.CrCompanyTerritory ([TerritoryId]);
        CREATE INDEX IX_CrCompanyTerritory_CompanyId ON dbo.CrCompanyTerritory ([CompanyId]);
        CREATE INDEX IX_CrCompanyTerritory_OutSystemsId ON dbo.CrCompanyTerritory ([OutSystemsId]);
        GO
        CREATE TABLE dbo.CrTerritoryMovement (
            [Id] bigint IDENTITY(1,1) NOT NULL,
            [CompanyId] bigint NOT NULL,
            [FromUser] nvarchar(256) NOT NULL,
            [ToUser] nvarchar(256) NOT NULL,
            [UpdatedBy] nvarchar(256) NOT NULL,
            [UpdatedAt] datetime2(3) NULL,
            [OutSystemsId] bigint NULL,
            CONSTRAINT PK_CrTerritoryMovement PRIMARY KEY ([Id])
        );
        CREATE INDEX IX_CrTerritoryMovement_CompanyId ON dbo.CrTerritoryMovement ([CompanyId]);
        CREATE INDEX IX_CrTerritoryMovement_UpdatedAt ON dbo.CrTerritoryMovement ([UpdatedAt]);
        CREATE INDEX IX_CrTerritoryMovement_OutSystemsId ON dbo.CrTerritoryMovement ([OutSystemsId]);
        """;
}
