using System.Reflection;
using MyCRMRecords.Domain;

namespace MyCRMRecords.Data;

/// <summary>How a record's property is kept in MyCRM's table.</summary>
public enum CrmFieldKind
{
    /// <summary>The column holds the value as it is (text, number, yes/no, date and time).</summary>
    Plain,
    /// <summary>A date without its time.</summary>
    Day,
    /// <summary>A code of one of MyCRMRecords' lists; the column holds MyCRM's static record of that label.</summary>
    Static,
    /// <summary>A login (RSP\jsmith); the column holds the person's OutSystems user (OSSYS_USER).</summary>
    User,
    /// <summary>The Id of another of MyCRM's records (0 or none: NULL).</summary>
    Ref,
    /// <summary>Kept by MyCRMRecords only (dbo.CrRecordNote): MyCRM has no column for it.</summary>
    Side,
    /// <summary>A time of day ("HH:mm", "" for none); MyCRM keeps it as 1900-01-01 with that time (or a time column).</summary>
    Time,
}

/// <summary>A property of a MyCRM record and where MyCRM keeps it.</summary>
public sealed class CrmField(PropertyInfo property, CrmFieldKind kind, string column, ChoiceList? list = null, string staticDefault = "", bool required = false)
{
    public PropertyInfo Property { get; } = property;
    public string Name => Property.Name;
    public CrmFieldKind Kind { get; } = kind;
    /// <summary>MyCRM's column ("" for a <see cref="CrmFieldKind.Side"/> field).</summary>
    public string Column { get; } = column;
    public ChoiceList? List { get; } = list;
    /// <summary>The code read when MyCRM's row has none (a relationship with no type is a parent's).</summary>
    public string StaticDefault { get; } = staticDefault;
    /// <summary>A person MyCRM must know (an OutSystems user): writing anyone else is refused.</summary>
    public bool Required { get; } = required;
    /// <summary>The property's type, without Nullable.</summary>
    public Type Type { get; } = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
    public bool IsNullable { get; } = Nullable.GetUnderlyingType(property.PropertyType) is not null;
    /// <summary>The longest text the record keeps ([Len]); 0 when it has no limit or is not text.</summary>
    public int Length { get; } = property.GetCustomAttribute<LenAttribute>()?.Length ?? 0;

    public object? Get(object record) => Property.GetValue(record);
    public void Set(object record, object? value) => Property.SetValue(record, value);
}

/// <summary>Which of MyCRM's rows of an entity are MyCRMRecords' (the others are never read).</summary>
public enum CrmRows
{
    All,
    /// <summary>The permanent territories: the active ones and those MyCRMRecords keeps a note of (named, taken away here).</summary>
    PermanentTerritories,
    /// <summary>The active company links of active permanent territories.</summary>
    ActiveLinksOfPermanentTerritories,
    /// <summary>
    /// The temporary territories (MyCRM's projects', active or not): millions of rows, so they are only ever read by Id, by
    /// owner or as the active ones.
    /// </summary>
    TemporaryTerritories,
    /// <summary>The company links of temporary territories (read by territory).</summary>
    LinksOfTemporaryTerritories,
}

/// <summary>A record type of MyCRM's: its entity (table) and how each property is kept.</summary>
public sealed class CrmEntity
{
    public CrmEntity(Type type, string entity, IReadOnlyList<CrmField> fields, CrmRows rows, IReadOnlyDictionary<string, object?> fixedOnInsert, bool readOnly = false)
    {
        ReadOnly = readOnly;
        Type = type;
        Entity = entity;
        Fields = fields;
        Rows = rows;
        FixedOnInsert = fixedOnInsert;
        Stored = fields.Where(f => f.Kind != CrmFieldKind.Side).ToList();
        Side = fields.Where(f => f.Kind == CrmFieldKind.Side).ToList();
    }

    public Type Type { get; }
    /// <summary>MyCRM's entity name (its table is looked up by it: OSUSR_..._COMPANY).</summary>
    public string Entity { get; }
    /// <summary>Every property but Id.</summary>
    public IReadOnlyList<CrmField> Fields { get; }
    public IReadOnlyList<CrmField> Stored { get; }
    public IReadOnlyList<CrmField> Side { get; }
    public CrmRows Rows { get; }
    /// <summary>Only read: MyCRMRecords never adds or changes a row of it (a prospect).</summary>
    public bool ReadOnly { get; }
    /// <summary>Columns a new row gets whatever the record says (a territory made here is permanent).</summary>
    public IReadOnlyDictionary<string, object?> FixedOnInsert { get; }

    public CrmField? Field(string name) =>
        string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase) ? null
        : Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
          ?? throw new ArgumentException($"{Type.Name} has no field {name}.");

    /// <summary>MyCRM's columns MyCRMRecords reads and writes (with ID), for the connection test.</summary>
    public string[] Columns => new[] { "ID" }.Concat(Stored.Select(f => f.Column)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}

/// <summary>
/// The Users, Territory, Company, Activity and Project parts of MyCRM as MyCRMRecords reads and writes it in MyCRM's own tables (the
/// OutSystems database): each record type, its entity, and where each property is. A property not named here is kept
/// in the column of its name in upper case (Brn: BRN). Static entities are matched by label, people by their OutSystems
/// user, and what MyCRM has no column for is kept by MyCRMRecords (<see cref="RecordNote"/>).
/// </summary>
public static class MyCrmMap
{
    public const string GroupNames = "CompanyGroupName";
    public const string Teams = "Team";
    public const string Users = "CUser";
    public const string Companies = "Company";
    public const string Caretakes = "CompanyCaretake";
    public const string Relationships = "CompanyRelationship";
    public const string Addresses = "Address";
    public const string AddressLinks = "CompanyAddressLink";
    public const string Contacts = "Contacts";
    public const string ContactLinks = "CompanyContactLink";
    public const string Phones = "Phone";
    public const string Emails = "Email";
    public const string Territories = "Territory";
    public const string CompanyTerritories = "CompanyTerritory";
    public const string Movements = "TerritoryMovement";
    public const string Activities = "CompanyActivity";
    public const string ActivityTypeLinks = "ActivityTypeLink";
    public const string ActivityParticipants = "ActivityParticipants";
    public const string ActivityContacts = "CompanyActivityContactLink";
    public const string ActivityProspects = "CompanyActivityProspectLink";
    public const string ProjectLinks = "ProjectLink";
    public const string Projects = "Project";
    public const string ProjectCompanies = "ProjectCompany";
    public const string ProjectMembers = "ProjectMembers";
    public const string Prospects = "Prospect";

    /// <summary>Describes one entity: properties not named are <see cref="CrmFieldKind.Plain"/> in the column of their name.</summary>
    private sealed class Builder<T>(string entity) where T : class, IRecord, new()
    {
        private readonly Dictionary<string, CrmField> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, object?> _fixed = new(StringComparer.OrdinalIgnoreCase);
        private CrmRows _rows = CrmRows.All;
        private bool _readOnly;

        private static PropertyInfo P(string name) => typeof(T).GetProperty(name) ?? throw new ArgumentException($"{typeof(T).Name} has no {name}.");

        public Builder<T> Column(string property, string column) { _fields[property] = new CrmField(P(property), CrmFieldKind.Plain, column); return this; }
        public Builder<T> Day(params string[] properties) { foreach (var p in properties) _fields[p] = new CrmField(P(p), CrmFieldKind.Day, p.ToUpperInvariant()); return this; }
        public Builder<T> Ref(params string[] properties) { foreach (var p in properties) _fields[p] = new CrmField(P(p), CrmFieldKind.Ref, p.ToUpperInvariant()); return this; }
        public Builder<T> RefAt(string property, string column) { _fields[property] = new CrmField(P(property), CrmFieldKind.Ref, column); return this; }
        public Builder<T> Time(params string[] properties) { foreach (var p in properties) _fields[p] = new CrmField(P(p), CrmFieldKind.Time, p.ToUpperInvariant()); return this; }
        public Builder<T> ReadOnly() { _readOnly = true; return this; }
        public Builder<T> User(params string[] properties) { foreach (var p in properties) _fields[p] = new CrmField(P(p), CrmFieldKind.User, p.ToUpperInvariant()); return this; }
        public Builder<T> Person(string property, string column, bool required = false) { _fields[property] = new CrmField(P(property), CrmFieldKind.User, column, required: required); return this; }
        public Builder<T> Static(string property, string column, ChoiceList list, string fallback = "") { _fields[property] = new CrmField(P(property), CrmFieldKind.Static, column, list, fallback); return this; }
        public Builder<T> Side(params string[] properties) { foreach (var p in properties) _fields[p] = new CrmField(P(p), CrmFieldKind.Side, ""); return this; }
        public Builder<T> Fixed(string column, object? value) { _fixed[column] = value; return this; }
        public Builder<T> Only(CrmRows rows) { _rows = rows; return this; }

        public CrmEntity Build()
        {
            var fields = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite && p.Name != "Id")
                .OrderBy(p => p.MetadataToken)
                .Select(p => _fields.TryGetValue(p.Name, out var f) ? f : new CrmField(p, CrmFieldKind.Plain, p.Name.ToUpperInvariant()))
                .ToList();
            return new CrmEntity(typeof(T), entity, fields, _rows, _fixed, _readOnly);
        }
    }

    public static readonly IReadOnlyList<CrmEntity> All =
    [
        new Builder<CompanyGroupName>(GroupNames).Side("IsActive", "RemovedOn", "RemovedBy").Build(),
        new Builder<CrmTeam>(Teams).Build(),
        new Builder<CrmUser>(Users)
            .Person("Login", "USERID", required: true).Static("Role", "ROLEID", CrmRoles.List).Ref("TeamId", "ProfileId").User("CreatedBy", "UpdatedBy")
            .Side("FormerTeamId", "RemovedOn", "RemovedBy").Build(),
        new Builder<Company>(Companies)
            .Column("Name", "COMPANY").Static("CompanyGroup", "COMPANYGROUPID", CompanyGroups.List).Ref("CompanyGroupNameId", "PreCompanyGroupNameId")
            .Day("CreatedDate", "UpdatedDate").Static("CompAccountType", "COMPACCOUNTTYPEID", CompAccountTypes.List)
            .Static("CompOwnership", "COMPOWNERSHIPID", CompOwnerships.List).User("UpdatedBy", "CreatedBy")
            .Static("CompanyIndustry", "COMPANYINDUSTRYID", CompanyIndustries.List).Column("PreName", "PRECOMPANYNAME")
            .Static("PreCompanyGroup", "PRECOMPANYGROUPID", CompanyGroups.List).Static("PreCompAccountType", "PRECOMPACCOUNTTYPEID", CompAccountTypes.List)
            .Static("PreCompOwnership", "PRECOMPOWNERSHIPID", CompOwnerships.List).Static("PreCompanyIndustry", "PRECOMPANYINDUSTRYID", CompanyIndustries.List)
            .Static("CustomerType", "CUSTOMERTYPEID", CustomerTypes.List).Static("AdvanceCustomerType", "ADVANCECUSTOMERTYPEID", AdvanceCustomerTypes.List)
            .Build(),
        new Builder<CompanyCaretake>(Caretakes).Ref("CompanyId").Day("UpdatedDate", "DateCreated").User("CreatedBy", "UpdatedBy").Build(),
        new Builder<CompanyRelationship>(Relationships)
            .Ref("CompanyId", "RelatedCompanyId").Static("RelationshipType", "COMPANYRELATIONSHIPTYPEID", RelationshipTypes.List, RelationshipTypes.Parent)
            .Side("RemovedOn", "RemovedBy").Build(),
        new Builder<Address>(Addresses)
            .Static("AddressType", "ADDRESSTYPEID", AddressTypes.List).Static("PreAddressType", "PREADDRESSTYPEID", AddressTypes.List).User("CreatedBy", "UpdatedBy")
            .Build(),
        new Builder<CompanyAddressLink>(AddressLinks).Ref("CompanyId", "AddressId").Side("IsActive", "RemovedOn", "RemovedBy").Build(),
        new Builder<Contact>(Contacts).User("CreatedBy", "UpdatedBy").Build(),
        new Builder<CompanyContactLink>(ContactLinks).Ref("CompanyId", "ContactId").Build(),
        new Builder<Phone>(Phones)
            .Ref("CompanyId", "ContactId").Static("PhoneType", "PHONETYPEID", PhoneTypes.List).Static("PrePhoneType", "PREPHONETYPEID", PhoneTypes.List)
            .Column("Number", "PHONE").User("CreatedBy", "UpdatedBy").Build(),
        new Builder<ContactEmail>(Emails).Column("Address", "EMAIL").Ref("CompanyId", "ContactId").User("CreatedBy", "UpdatedBy").Build(),
        new Builder<Territory>(Territories)
            .Column("Number", "TERRITORY").Person("Owner", "OWNERID", required: true).Side("Name", "CreatedOn", "CreatedBy", "RemovedOn", "RemovedBy")
            .Fixed("ISTEMPORARY", false).Only(CrmRows.PermanentTerritories).Build(),
        new Builder<CompanyTerritory>(CompanyTerritories)
            .Ref("TerritoryId", "CompanyId").User("CreatedBy").Side("CreatedOn", "RemovedOn", "RemovedBy").Only(CrmRows.ActiveLinksOfPermanentTerritories).Build(),
        new Builder<TerritoryMovement>(Movements).Ref("CompanyId").Person("FromUser", "FROMUSERID").Person("ToUser", "TOUSERID").User("UpdatedBy").Build(),
        // Activities, projects and their temporary territories; prospects are only read.
        new Builder<Activity>(Activities)
            .Day("VisitDate", "UpdatedDate", "CreatedDate", "ToDate", "ReminderDate").Time("StartTime", "EndTime").Ref("CompanyId").RefAt("AttendeeId", "ATTENDEES")
            .Person("Owner", "OWNERID", required: true).Column("CrmCompanyActivityId", "CRMCOMPANYACTIVITYID")
            .Static("Action", "COMPANYACTIVITYACTIONID", ActivityActions.List).Static("Status", "COMPANYACTIVITYSTATUSID", ActivityStatuses.List).User("UpdatedBy").Build(),
        new Builder<ActivityTypeLink>(ActivityTypeLinks).Ref("CompanyActivityId").Static("ActivityType", "ACTIVITYTYPEID", ActivityTypes.List).Build(),
        new Builder<ActivityParticipant>(ActivityParticipants)
            .Ref("CompanyEventId").Person("User", "USERID", required: true).Column("CrmCommunicationId", "CRMCOMMUNICATIONID").Column("CrmCommLinkId", "CRMCOMMLINKID").Build(),
        new Builder<ActivityContact>(ActivityContacts).Ref("CompanyActivityId", "ContactId").Column("CrmActivityContactLink", "CRMACTIVITYCONTACTLINK").Build(),
        new Builder<ActivityProspect>(ActivityProspects).Ref("ProspectId", "CompanyActivityId").Build(),
        new Builder<ProjectLink>(ProjectLinks).Ref("CompanyActivityId", "ProjectId").Build(),
        new Builder<Project>(Projects)
            .Static("Status", "PROJECTSTATUSID", ProjectStatuses.List).Ref("CompanyId").Day("StartDate", "EndDate", "DateCreated", "UpdatedDate")
            .Person("Owner", "OWNERID", required: true).User("UpdatedBy").Build(),
        new Builder<ProjectCompany>(ProjectCompanies).Ref("ProjectId", "CompanyId").Build(),
        new Builder<ProjectMember>(ProjectMembers)
            .Ref("CompanyId", "ProjectId", "ContactId", "TerritoryId").Person("User", "USERID", required: true).Static("Role", "PROJECTROLEID", ProjectRoles.List).Build(),
        new Builder<TemporaryTerritory>(Territories)
            .Column("Number", "TERRITORY").Person("Owner", "OWNERID", required: true).Fixed("ISTEMPORARY", true).Only(CrmRows.TemporaryTerritories).Build(),
        new Builder<TemporaryCompanyTerritory>(CompanyTerritories).Ref("TerritoryId", "CompanyId").User("CreatedBy").Only(CrmRows.LinksOfTemporaryTerritories).Build(),
        new Builder<Prospect>(Prospects)
            .Static("Stage", "STAGEID", ProspectStages.List).Day("EstimatedOrderDate", "DateCreated", "DateClosed", "UpdatedDate").Ref("CompanyId").Person("Owner", "OWNERID").User("UpdatedBy")
            .Static("ProspectType", "PROSPECTTYPE", ProspectTypes.List).Static("ProspectStatus", "PROSPECTSTATUSID", ProspectStatuses.List).ReadOnly().Build(),
    ];

    private static readonly Dictionary<Type, CrmEntity> ByType = All.ToDictionary(e => e.Type);

    /// <summary>The MyCRM entity of a record type; null for MyCRMRecords' own records.</summary>
    public static CrmEntity? Of(Type type) => ByType.TryGetValue(type, out var entity) ? entity : null;

    public static CrmEntity? Of<T>() => Of(typeof(T));

    /// <summary>MyCRM's static entities MyCRMRecords matches by label.</summary>
    public static readonly ChoiceList[] StaticLists =
    [
        CrmRoles.List, CompanyGroups.List, CompAccountTypes.List, CompOwnerships.List, CompanyIndustries.List, CustomerTypes.List, AdvanceCustomerTypes.List,
        AddressTypes.List, PhoneTypes.List, RelationshipTypes.List, ActivityActions.List, ActivityTypes.List, ActivityStatuses.List, ProjectStatuses.List,
        ProjectRoles.List, ProspectStages.List, ProspectStatuses.List, ProspectTypes.List,
    ];

    /// <summary>Every table and column of MyCRM MyCRMRecords reads and writes (the Connections tab's Test checks each).</summary>
    public static readonly IReadOnlyList<(string Entity, string[] Columns)> Checked =
        All.GroupBy(e => e.Entity, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.SelectMany(e => e.Rows is CrmRows.PermanentTerritories or CrmRows.TemporaryTerritories ? e.Columns.Concat(new[] { "ISTEMPORARY" }) : e.Columns)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Concat(StaticLists.Select(l => (l.OldEntity, new[] { "ID", "LABEL" }))).ToList();
}
