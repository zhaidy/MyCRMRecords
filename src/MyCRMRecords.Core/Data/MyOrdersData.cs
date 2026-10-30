using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace MyCRMRecords.Data;

/// <summary>An OutSystems entity: the module it is in (<see cref="OsModule"/>) and its name.</summary>
public sealed record OsEntity(OsModule Module, string Name)
{
    public override string ToString() => Module + "." + Name;
}

/// <summary>The OutSystems modules MyCRMRecords reads (their names are settings: MyOrders, MyCRM, MyFiles and MyMachines).</summary>
public enum OsModule
{
    MyOrders,
    MyCRM,
    /// <summary>MyFiles' files (the PDFs MyFile keeps in MyFiles' tables), read only for the Files page.</summary>
    MyFiles,
    /// <summary>MyMachines' temporary access (TempAccess): read for who sees a company's machines; changed only when switched on.</summary>
    MyMachines,
}

/// <summary>
/// The entities MyCRMRecords reads, all read only: MyOrders' costing sheets with their items, statuses and
/// attachments, MyCRM's companies, contacts, addresses and users (for the names on a costing sheet and for MyCRM's
/// roles and teams, which decide who sees which costing sheets), and MyFiles' files (the Files page). Their tables are
/// found by module and entity name; their columns are the attribute names in upper case.
/// </summary>
public static class Entities
{
    public static readonly OsEntity CostingSheet = new(OsModule.MyOrders, "CostingSheet");
    public static readonly OsEntity CostingSheetItem = new(OsModule.MyOrders, "CostingSheetItem");
    public static readonly OsEntity CostingSheetSubItem = new(OsModule.MyOrders, "CostingSheetSubItem");
    public static readonly OsEntity CostingSheetComplexItem = new(OsModule.MyOrders, "CostingSheetComplexItem");
    public static readonly OsEntity CostingSheetAttachment = new(OsModule.MyOrders, "CostingSheetAttachment");
    public static readonly OsEntity CostingSheetStatus = new(OsModule.MyOrders, "CostingSheetStatus");
    public static readonly OsEntity Reference = new(OsModule.MyOrders, "Reference");
    public static readonly OsEntity Variable = new(OsModule.MyOrders, "Variable");
    public static readonly OsEntity VariableName = new(OsModule.MyOrders, "VariableName");
    public static readonly OsEntity BillType = new(OsModule.MyOrders, "BillType");
    public static readonly OsEntity TradeInTo = new(OsModule.MyOrders, "TradeInTo");
    public static readonly OsEntity TransactionType = new(OsModule.MyOrders, "TransactionType");
    public static readonly OsEntity ExternalFinanceCompany = new(OsModule.MyOrders, "ExternalFinanceCompany");
    public static readonly OsEntity Product = new(OsModule.MyOrders, "Product");
    public static readonly OsEntity ProductGroup = new(OsModule.MyOrders, "ProductGroup");
    public static readonly OsEntity PacketItem = new(OsModule.MyOrders, "PacketItem");
    public static readonly OsEntity RequestForm = new(OsModule.MyOrders, "RequestForm");
    public static readonly OsEntity RequestFormItem = new(OsModule.MyOrders, "RequestFormItem");
    public static readonly OsEntity RequestFormSubItem = new(OsModule.MyOrders, "RequestFormSubItem");
    public static readonly OsEntity RequestFormComplexItem = new(OsModule.MyOrders, "RequestFormComplexItem");
    public static readonly OsEntity RequestFormAttachment = new(OsModule.MyOrders, "RequestFormAttachment");
    public static readonly OsEntity RequestFormStatus = new(OsModule.MyOrders, "RequestFormStatus");
    public static readonly OsEntity RequestFormType = new(OsModule.MyOrders, "RequestFormType");
    public static readonly OsEntity CollectionType = new(OsModule.MyOrders, "CollectionType");
    public static readonly OsEntity AssetType = new(OsModule.MyOrders, "AssetType");
    public static readonly OsEntity Brand = new(OsModule.MyOrders, "Brand");
    /// <summary>The delivery form MyOrders printed last for a request form (its Print rebuilt it each time): one per delivery.</summary>
    public static readonly OsEntity DeliveryFormPrint = new(OsModule.MyOrders, "DeliveryFormPrint");
    /// <summary>The lines of a printed delivery form, with the page each was printed on.</summary>
    public static readonly OsEntity DeliveryFormPrintItem = new(OsModule.MyOrders, "DeliveryFormPrintItem");

    public static readonly OsEntity Company = new(OsModule.MyCRM, "Company");
    public static readonly OsEntity Contacts = new(OsModule.MyCRM, "Contacts");
    public static readonly OsEntity Address = new(OsModule.MyCRM, "Address");
    public static readonly OsEntity CrmUser = new(OsModule.MyCRM, "CUser");
    public static readonly OsEntity CrmRole = new(OsModule.MyCRM, "Role");
    public static readonly OsEntity Phone = new(OsModule.MyCRM, "Phone");
    public static readonly OsEntity CompanyGroup = new(OsModule.MyCRM, "CompanyGroup");
    /// <summary>MyCRM's event tags (a costing sheet's Event Tag); read when there is one, its name shown.</summary>
    public static readonly OsEntity EventTag = new(OsModule.MyCRM, "EventTag");

    /// <summary>MyFiles' files (the Files page): a PDF each, with its numbers and MyCRM company.</summary>
    public static readonly OsEntity File = new(OsModule.MyFiles, "File");
    /// <summary>MyFiles' static entity FileType (matched by label, never by Id).</summary>
    public static readonly OsEntity FileType = new(OsModule.MyFiles, "FileType");

    /// <summary>MyMachines' TempAccess: a person (OSSYS_USER) who may see the machines of a customer (by name or BRN).</summary>
    public static readonly OsEntity TempAccess = new(OsModule.MyMachines, "TempAccess");

    /// <summary>Every entity read, with the columns MyCRMRecords needs from it (the Connections tab's Test checks each).</summary>
    public static readonly IReadOnlyList<(OsEntity Entity, string[] Columns)> Checked =
        new List<(OsEntity, string[])>
        {
            (CostingSheet, new[] { "ID", "ISACTIVE", "SALESMANID", "COMPANYID", "CONTACTID", "ADDRESSID", "COSTINGSHEETSTATUSID", "REFERENCEID", "CREATEDAT",
                "CREATEDBY", "UPDATEDAT", "UPDATEDBY", "PRINTEDAT", "TOTALSTP", "TOTALACHIEVEDPRICE", "COMMISSIONABLEREVENUE", "TOTALMANAGERPRICE", "TOTALFOB",
                "PERCENTAGEABOVEMANAGERPRICE", "PERCENTAGEABOVEFOB", "ESTIMATEDCOMMISSION", "CONFIRMEDCOMMISSION", "INVOICENO", "INVOICEDATETEXT", "ORDERNO",
                "ORDERUPDATEDON", "ORDERUPDATEDBY", "ISOPOCHECKED", "OPOCHECKEDON", "OPOCHECKEDBY", "MDSSPECIALISTID", "PPSPECIALISTID", "MITSSPECIALISTID",
                "PENDINGMDS", "PENDINGPP", "PENDINGMITS", "APPROVEDAT", "APPROVEDBY", "HEADAPPROVEDAT", "PENDINGCREDITON", "CREDITENDORSEDON",
                "PENDINGINVOICINGON", "MININVOICEDATE", "RECALLAT", "DELETEDAT", "DELETEDBY", "TRANSFERREDON", "ORIGINALSALESMANID", "CUSTOMERNO", "ISZBA",
                "ISCONTRACTEXTENSION", "ISFIXEDTERMAGREEMENT", "ISVITALCASE", "GMAPRICING", "EVENTTAGID", "REQUESTFORMID", "PROPOSALID", "OPTIONID",
                "BILLTYPEID", "ISPPP", "FINANCEOUTRIGHTAMOUNT", "MONTHS", "INTERESTRATE", "DOWNPAYMENT", "FINALPERCENTAGE", "FINALPAYMENT", "MONTHLYLEASE",
                "PPPINTERNALBWRATE", "PPPINTERNALCOLOURRATE", "PPPCUSTOMERBWRATE", "PPPCUSTOMERCOLOURRATE", "PPPMONTHLYBWVOLUME", "PPPMONTHLYCOLOURVOLUME",
                "BILLTO", "LEASEAGREEMENTNUMBER", "TRANSACTIONTYPEID", "TRADEINTOID", "BILLINGADDRESS", "REMARKS", "CREDITTERMS", "TOTALAPPROVEDPRICE",
                "TOTALVARIABLECOST", "ISEPP", "ISNOVATION", "ISPOCOMPLETED", "ISRECEIPTCOMPLETED", "CONTACTNAME", "PHONE", "EMAIL", "FAX", "DELIVERTOSTREET",
                "DELIVERTOCITY", "DELIVERTOSTATEPROVINCE", "DELIVERTOCOUNTRY", "DELIVERTOPOSTALCODE" }),
            (CostingSheetItem, new[] { "ID", "COSTINGSHEETID", "ISACTIVE", "PRODUCTID", "PRODUCTNAME", "PRODUCTTYPE", "EDPCODE", "QUANTITY", "UNITPRICE", "TOTALSTP",
                "ACHIEVEDPRICE", "APPROVEDSPECIALUNITPRICE", "ISSUBSCRIPTION", "OAAPRICING", "ISPACKET", "PACKETID", "ISMDS", "ISSOL", "MODEL", "SERIAL", "ISFSA", "BELOWGMCPC", "METERBWONLY",
                "INITIALBWCOPIES", "INITIALBWPRICE", "INITIALCLRCOPIES", "INITIALCLRPRICE", "MONTHLYBWCOPIES", "MONTHLYBWPRICE", "MONTHLYCLRCOPIES",
                "MONTHLYCLRPRICE", "MONTHLYMONTHS", "THEREAFTERBWPRICE", "THEREAFTERCLRPRICE" }),
            (CostingSheetSubItem, new[] { "ID", "COSTINGSHEETITEMID", "ISACTIVE", "PRODUCTNAME", "PRODUCTTYPE", "EDPCODE", "QUANTITY", "UNITPRICE" }),
            (CostingSheetComplexItem, new[] { "ID", "COSTINGSHEETITEMID", "ISACTIVE", "PRODUCTID", "PRODUCTNAME", "PRODUCTTYPE", "EDPCODE", "MPGCODE", "QUANTITY", "STP", "ACHIEVEDPRICE" }),
            (CostingSheetAttachment, new[] { "ID", "COSTINGSHEETID", "BINARY", "FILENAME", "REMARKS", "UPLOADEDBY", "UPLOADEDON", "ISCOMPLEX" }),
            (CostingSheetStatus, new[] { "ID", "LABEL", "ORDER" }),
            (Reference, new[] { "ID", "REFERENCE" }),
            (Variable, new[] { "ID", "COSTINGSHEETID", "VARIABLENAMEID", "COST", "TRADEINITEM", "MANUFACTURER", "MODEL", "SERIALNO", "REMARKS" }),
            (VariableName, new[] { "ID", "LABEL" }),
            (BillType, new[] { "ID", "LABEL" }),
            (TradeInTo, new[] { "ID", "LABEL" }),
            (TransactionType, new[] { "ID", "LABEL" }),
            (ExternalFinanceCompany, new[] { "ID", "NAME" }),
            (Product, new[] { "ID", "NAME", "EDPCODE", "PRODUCTGROUPID" }),
            (ProductGroup, new[] { "ID", "NAME" }),
            (PacketItem, new[] { "ID", "PACKETID", "PRODUCTID", "PRODUCTNAME", "PRODUCTTYPE", "EDPCODE", "QUANTITY", "ISACTIVE" }),
            (RequestForm, new[] { "ID", "REQUESTFORMTYPEID", "PREVREQUESTFORMID", "PROSPECTID", "PROPOSALID", "OPTIONID", "COSTINGSHEETID", "REQUESTFORMSTATUSID",
                "REFERENCEID", "COMPANYID", "CONTACTID", "ADDRESSID", "SALESMANID", "CREATEDBY", "CREATEDAT", "UPDATEDBY", "UPDATEDAT", "ISACTIVE", "TOTALSTP",
                "PRINTEDAT", "APPROVEDAT", "APPROVEDBY", "ISSENT", "SENTAT", "SENTBY", "PICKINGSLIPNUMBER", "INVOICENUMBER", "SALESORDERNUMBER",
                "BACKORDEREDSALESORDERNUMBER", "WAREHOUSE", "MDSSPECIALISTID", "PPSPECIALISTID", "MITSSPECIALISTID", "PENDINGMDS", "PENDINGPP", "PENDINGMITS",
                "CREDITAPPROVEDBY", "CREDITAPPROVEDAT", "DELETEDBY", "DELETEDAT", "RECALLAT", "ISRECONBILLED", "ISEPP", "PENDINGCREDITON", "CREDITENDORSEDON",
                "ISOPOCHECKED", "OPOCHECKEDON", "OPOCHECKEDBY", "CUSTOMERNO", "DECLARED", "AGILITYRUNNINGNUMBER", "APPROVEDBYMANAGERID", "APPROVEDBYMANAGERON",
                "APPROVEDBYHEADID", "APPROVEDBYHEADON", "EVENTTAGID" }),
            (RequestFormItem, new[] { "ID", "REQUESTFORMID", "PRODUCTTYPE", "PRODUCTNAME", "EDPCODE", "QUANTITY", "UNITPRICE", "TOTALSTP", "ISACTIVE", "ADDRESSID",
                "CONTACTID", "PHONEID", "DATE", "REMARKS", "MODEL", "SERIAL", "DESCRIPTION", "BRANDID", "ASSETTYPEID", "COLLECTIONTYPEID", "SPECIALINSTRUCTIONS",
                "NOLIFT", "CALLBEFORECOLLECTION", "SECONDARYCONTACT", "SECONDARYCONTACTNO", "POSTALCODE", "PACKETID", "ISPACKET", "ISFSA", "HASFINISHER",
                "CONTRACTNO", "REASONOFTERMINATION", "REMOVALFEE", "INVOICENO", "BALANCETOINVOICE", "ROLLOVERAMOUNT" }),
            (RequestFormSubItem, new[] { "ID", "REQUESTFORMITEMID", "EDPCODE", "PRODUCTNAME", "QUANTITY", "UNITPRICE", "ISACTIVE" }),
            (RequestFormComplexItem, new[] { "ID", "REQUESTFORMITEMID", "PRODUCTID", "EDPCODE", "PRODUCTNAME", "QUANTITY", "ISACTIVE" }),
            (RequestFormAttachment, new[] { "ID", "REQUESTFORMID", "BINARY", "FILENAME", "REMARKS", "UPLOADEDBY", "UPLOADEDON", "ISCOMPLEX" }),
            (RequestFormStatus, new[] { "ID", "LABEL", "ORDER" }),
            (RequestFormType, new[] { "ID", "LABEL", "ORDER" }),
            (CollectionType, new[] { "ID", "LABEL" }),
            (AssetType, new[] { "ID", "LABEL" }),
            (Brand, new[] { "ID", "LABEL" }),
            (DeliveryFormPrint, new[] { "ID", "REQUESTFORMID", "ADDRESSID", "CONTACTID", "DATE", "NOLIFT", "REMARKS", "PHONEID" }),
            (DeliveryFormPrintItem, new[] { "ID", "DELIVERYFORMPRINTID", "REQUESTFORMID", "REQUESTFORMITEMID", "ITEMCODE", "NAME", "SERIALNO", "ALLOCATEDQUANTITY",
                "QUANTITY", "MODEL", "PRODUCTTYPE", "PAGE", "ISFSA" }),
            (Company, new[] { "ID", "COMPANY", "COMPANYGROUPID", "BRN", "INCCERTNO", "CREDITTERMS" }),
            (Contacts, new[] { "ID", "SALUTATION", "FIRSTNAME", "MIDDLENAME", "LASTNAME" }),
            (Phone, new[] { "ID", "CONTACTID", "PHONE", "ISACTIVE" }),
            (Address, new[] { "ID", "BLOCK", "STREET", "UNITNUMBER", "BUILDINGNAME", "POSTALCODE", "CITY", "STATEPROVINCE", "COUNTRY" }),
            (CompanyGroup, new[] { "ID", "LABEL" }),
            (CrmUser, new[] { "ID", "USERID", "ROLEID", "TEAMID", "ISACTIVE", "SALESMANCODE" }),
            (CrmRole, new[] { "ID", "LABEL" }),
            (File, new[] { "ID", "FILENAME", "INVOICENUMBER", "DONUMBER", "REFERENCENUMBER", "PPSNUMBER", "CONTRACTNUMBER", "SERVICENUMBER", "RMANUMBER",
                "PSDREFNUMBER", "MSDREFNUMBER", "COMPANYID", "COMPANYNAME", "BRN", "CREATEDBY", "CREATEDAT", "ISACTIVE", "FILEBINARY", "FILETYPEID" }),
            (FileType, new[] { "ID", "LABEL" }),
            (TempAccess, new[] { "ID", "USERID", "CUSTOMERNAME", "BRN", "DATETIME" }),
        };

    /// <summary>The name of a module, as set on the Connections tab.</summary>
    public static string ModuleName(ConnectionSettings connections, OsModule module) => (module switch
    {
        OsModule.MyOrders => connections.OrdersModule,
        OsModule.MyFiles => connections.FilesModule,
        OsModule.MyMachines => connections.MachinesModule,
        _ => connections.CrmModule,
    }).Trim();

    /// <summary>
    /// Whether a module is found even after it is retired in OutSystems (its tables kept): MyOrders and MyFiles, which
    /// MyCRMRecords only reads; never MyCRM or MyMachines, which it may write.
    /// </summary>
    public static bool ReadOnly(OsModule module) => module != OsModule.MyCRM && module != OsModule.MyMachines;
}

/// <summary>Values of an OutSystems row (column → value, case ignored): empty dates (1900-01-01) and Ids (0) read as null.</summary>
public static class RowValues
{
    public static object? Get(this IReadOnlyDictionary<string, object?> row, string column) => row.TryGetValue(column, out var value) ? value : null;

    public static string Text(this IReadOnlyDictionary<string, object?> row, string column) => OutSystemsValues.Text(row.Get(column));

    public static long? Id(this IReadOnlyDictionary<string, object?> row, string column) =>
        row.Get(column) is { } value && Convert.ToInt64(value, CultureInfo.InvariantCulture) is var id && id != 0 ? id : null;

    public static long Long(this IReadOnlyDictionary<string, object?> row, string column) =>
        row.Get(column) is { } value ? Convert.ToInt64(value, CultureInfo.InvariantCulture) : 0;

    public static int Int(this IReadOnlyDictionary<string, object?> row, string column) =>
        row.Get(column) is { } value ? Convert.ToInt32(value, CultureInfo.InvariantCulture) : 0;

    /// <summary>A number without the trailing zeros of OutSystems' decimal(37,8) columns (12000.00000000 is 12000).</summary>
    public static decimal Decimal(this IReadOnlyDictionary<string, object?> row, string column) =>
        row.Get(column) is { } value ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) / 1.0000000000000000000000000000m : 0m;

    public static bool Bool(this IReadOnlyDictionary<string, object?> row, string column) =>
        row.Get(column) is { } value && Convert.ToBoolean(value, CultureInfo.InvariantCulture);

    public static DateTime? Date(this IReadOnlyDictionary<string, object?> row, string column) => OutSystemsValues.Date(row.Get(column));
}

/// <summary>What the costing sheets list asks for: its filters (MyOrders' CostingSheets screen), whose costing sheets, a page.</summary>
public sealed class CostingSheetSearch
{
    /// <summary>Part of the number, status, reference, company, invoice or order number.</summary>
    public string Keyword { get; set; } = "";
    public long? StatusId { get; set; }
    /// <summary>The Sales Rep chosen: their OutSystems users (a person can have two, OrdersAccess.SamePerson); null for anyone.</summary>
    public IReadOnlyCollection<long>? SalesRepIds { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
    /// <summary>The MDS, PP or MITS specialist chosen: their OutSystems users; null for anyone.</summary>
    public IReadOnlyCollection<long>? SpecialistIds { get; set; }
    public DateTime? HeadApprovedFrom { get; set; }
    public DateTime? HeadApprovedTo { get; set; }
    /// <summary>"Yes": an MDS or MITS specialist; "No": neither; "": both.</summary>
    public string HasSpecialist { get; set; } = "";
    public DateTime? InvoiceFrom { get; set; }
    public DateTime? InvoiceTo { get; set; }
    /// <summary>One company's costing sheets (MyCRM's company Id: a company's page links to them); null for any.</summary>
    public long? CompanyId { get; set; }

    /// <summary>The salespeople (OutSystems users) whose costing sheets the person sees; null for everyone's.</summary>
    public IReadOnlyCollection<long>? SalesmanIds { get; set; }
    /// <summary>Only companies whose name contains this (MyOrders' Service Delivery Managers see DBS's only); null for any.</summary>
    public string? CompanyContains { get; set; }

    public string Sort { get; set; } = "id";
    public bool Descending { get; set; } = true;
    public int Skip { get; set; }
    public int Take { get; set; } = 50;

    public static readonly string[] Sorts = ["id", "salesman", "reference", "company", "contact", "created", "updated", "printed", "achieved", "status", "invoice", "opo"];
}

/// <summary>A costing sheet as the list shows it.</summary>
public sealed record CostingSheetRow
{
    public long Id { get; init; }
    public long? SalesmanId { get; init; }
    public string Salesman { get; init; } = "";
    public string Reference { get; init; } = "";
    public long? CompanyId { get; init; }
    public string Company { get; init; } = "";
    public string Contact { get; init; } = "";
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? PrintedAt { get; init; }
    public DateTime? HeadApprovedAt { get; init; }
    public DateTime? MinInvoiceDate { get; init; }
    public decimal TotalAchievedPrice { get; init; }
    public string Status { get; init; } = "";
    public string InvoiceNo { get; init; } = "";
    public string OrderNo { get; init; } = "";
    public bool IsOpoChecked { get; init; }
    /// <summary>Whether MyOrders let the person print it (set by CostingSheets.List).</summary>
    public bool CanPrint { get; init; }
}

public sealed record CostingSheetPage(int Total, IReadOnlyList<CostingSheetRow> Rows);

/// <summary>
/// MyOrders' and MyCRM's tables, read only: MyCRMRecords never writes to them (there is no method that could). Rows
/// are column → value with the columns in upper case; a file column (Binary Data) holds the file's length in bytes, never
/// its bytes, which only <see cref="File"/> reads.
/// </summary>
public interface IMyOrdersData
{
    /// <summary>Whether the OutSystems database is set up on the Connections tab.</summary>
    bool Available { get; }
    /// <summary>The rows whose <paramref name="column"/> is one of <paramref name="values"/>.</summary>
    IReadOnlyList<Dictionary<string, object?>> Rows(OsEntity entity, string column, IReadOnlyCollection<long> values);
    /// <summary>Every row of a small table (statuses, roles, MyCRM's users).</summary>
    IReadOnlyList<Dictionary<string, object?>> All(OsEntity entity);
    /// <summary>OutSystems' users (OSSYS_USER).</summary>
    IReadOnlyList<OldUser> Users();
    /// <summary>The bytes of one row's file, or null when the row or file is not there.</summary>
    byte[]? File(OsEntity entity, string column, long id);
    /// <summary>A page of active costing sheets matching the search, and how many match.</summary>
    CostingSheetPage Search(CostingSheetSearch search);
    /// <summary>The OutSystems users that are the salesperson or a specialist of an active costing sheet (for the filters).</summary>
    CostingSheetPeople People();
    /// <summary>A page of active request forms matching the search, and how many match.</summary>
    RequestFormPage SearchRequestForms(RequestFormSearch search);
    /// <summary>The OutSystems users that are the salesperson of an active request form (for the Sales Rep filter).</summary>
    IReadOnlyCollection<long> RequestFormSalesmen();
}

/// <summary>The OutSystems users that are the salesperson, or a specialist, of an active costing sheet.</summary>
public sealed record CostingSheetPeople(IReadOnlyCollection<long> Salesmen, IReadOnlyCollection<long> Specialists);

/// <summary>
/// <see cref="IMyOrdersData"/> over the OutSystems database set on the Connections tab. Small tables (users, roles,
/// statuses) are kept for two minutes; costing sheets are read when asked.
/// </summary>
public sealed partial class SqlMyOrdersData(ExternalDatabases databases, Func<ConnectionSettings> connections, string domain) : IMyOrdersData
{
    private static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, (object Value, DateTime At)> _cache = new();

    public bool Available => databases.IsConfigured(ExternalNames.OutSystems);
    /// <summary>The databases, for the parts of this class in other files (a primary constructor's parameter is seen only in its own part).</summary>
    private ExternalDatabases Databases => databases;

    public string TableOf(DbConnection connection, OsEntity entity) =>
        databases.EntityTable(connection, Entities.ModuleName(connections(), entity.Module), entity.Name, Entities.ReadOnly(entity.Module));

    private T Cached<T>(string key, Func<T> read) where T : class
    {
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < Keep) return (T)hit.Value;
        var value = read();
        _cache[key] = (value, DateTime.UtcNow);
        return value;
    }

    /// <summary>The select list of a table: every column, a file column as its length (DATALENGTH, which does not read the file).</summary>
    private string Columns(DbConnection connection, string table, string alias = "") =>
        Cached("columns|" + connection.ConnectionString + "|" + table, () =>
        {
            var columns = OutSystemsTables.ColumnsOf(connection, null, table).Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            return string.Join(", ", columns.Select(c => c.SqlType is "varbinary" or "image" or "binary"
                ? $"CAST(DATALENGTH({alias}[{c.Name}]) AS bigint) AS [{c.Name}]"
                : $"{alias}[{c.Name}]"));
        });

    public IReadOnlyList<Dictionary<string, object?>> Rows(OsEntity entity, string column, IReadOnlyCollection<long> values) =>
        databases.Use(ExternalNames.OutSystems, connection =>
        {
            var rows = new List<Dictionary<string, object?>>();
            var table = TableOf(connection, entity);
            var select = Columns(connection, table);
            // Numbers written into the statement, 1000 at a time (a list of parameters has a limit).
            foreach (var chunk in values.Distinct().Select((v, i) => (v, i)).GroupBy(x => x.i / 1000, x => x.v))
                rows.AddRange(Sql.Rows(connection, null,
                    $"SELECT {select} FROM {table} WHERE {Sql.Name(column)} IN ({string.Join(", ", chunk.Select(v => v.ToString(CultureInfo.InvariantCulture)))}) ORDER BY [ID]"));
            return (IReadOnlyList<Dictionary<string, object?>>)rows;
        });

    public IReadOnlyList<Dictionary<string, object?>> All(OsEntity entity) =>
        Cached("all|" + entity, () => databases.Use(ExternalNames.OutSystems, connection =>
        {
            var table = TableOf(connection, entity);
            return (IReadOnlyList<Dictionary<string, object?>>)Sql.Rows(connection, null, $"SELECT {Columns(connection, table)} FROM {table} ORDER BY [ID]");
        }));

    public IReadOnlyList<OldUser> Users() =>
        Cached("users", () => databases.Use(ExternalNames.OutSystems, connection =>
            (IReadOnlyList<OldUser>)Sql.Rows(connection, null, "SELECT [ID], [USERNAME], [NAME], [EMAIL] FROM dbo.OSSYS_USER ORDER BY [ID]")
                .Select(r => new OldUser(Convert.ToInt64(r["ID"], CultureInfo.InvariantCulture), OutSystemsImport.LoginOf(OutSystemsValues.Text(r["USERNAME"]), domain),
                    OutSystemsValues.Text(r["NAME"]), OutSystemsValues.Text(r["EMAIL"]))).ToList()));

    public byte[]? File(OsEntity entity, string column, long id) => databases.Use(ExternalNames.OutSystems, connection =>
    {
        var rows = Sql.Rows(connection, null, $"SELECT {Sql.Name(column)} AS [FILE] FROM {TableOf(connection, entity)} WHERE [ID] = @id", ("@id", id));
        return rows.Count == 1 ? rows[0]["FILE"] as byte[] : null;
    });

    public CostingSheetPeople People() =>
        Cached("people", () => databases.Use(ExternalNames.OutSystems, connection =>
        {
            var table = TableOf(connection, Entities.CostingSheet);
            IReadOnlyCollection<long> Ids(string column) =>
                Sql.Rows(connection, null, $"SELECT DISTINCT [{column}] AS [ID] FROM {table} WHERE [ISACTIVE] = 1 AND [{column}] IS NOT NULL AND [{column}] <> 0")
                    .Select(r => Convert.ToInt64(r["ID"], CultureInfo.InvariantCulture)).ToList();
            var specialists = Ids("MDSSPECIALISTID").Concat(Ids("PPSPECIALISTID")).Concat(Ids("MITSSPECIALISTID")).Distinct().ToList();
            return new CostingSheetPeople(Ids("SALESMANID"), specialists);
        }));

    public CostingSheetPage Search(CostingSheetSearch search) => databases.Use(ExternalNames.OutSystems, connection =>
    {
        var cs = TableOf(connection, Entities.CostingSheet);
        var from = new StringBuilder()
            .Append($" FROM {cs} cs")
            .Append($" LEFT JOIN {TableOf(connection, Entities.CostingSheetStatus)} st ON st.[ID] = cs.[COSTINGSHEETSTATUSID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.Reference)} rf ON rf.[ID] = cs.[REFERENCEID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.Company)} co ON co.[ID] = cs.[COMPANYID]")
            .Append($" LEFT JOIN {TableOf(connection, Entities.Contacts)} ct ON ct.[ID] = cs.[CONTACTID]")
            .Append(" LEFT JOIN dbo.OSSYS_USER u ON u.[ID] = cs.[SALESMANID]")
            .ToString();
        var where = new List<string> { "cs.[ISACTIVE] = 1" };
        var parameters = new List<(string, object?)>();
        string P(object value)
        {
            var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
            parameters.Add((name, value));
            return name;
        }
        string Ids(IEnumerable<long> ids) => string.Join(", ", ids.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));
        const string NoOne = "IS NULL";

        if (search.SalesmanIds is { } allowed)
            where.Add(allowed.Count == 0 ? "1 = 0" : $"cs.[SALESMANID] IN ({Ids(allowed)})");
        if (search.CompanyContains is { } part)
            where.Add($"co.[COMPANY] LIKE {P("%" + Sql.EscapeLike(part) + "%")} ESCAPE '\\'");
        var keyword = search.Keyword.Trim();
        if (keyword.Length > 0)
        {
            var like = P("%" + Sql.EscapeLike(keyword) + "%");
            where.Add($"(st.[LABEL] LIKE {like} ESCAPE '\\' OR rf.[REFERENCE] LIKE {like} ESCAPE '\\' OR co.[COMPANY] LIKE {like} ESCAPE '\\' " +
                $"OR CAST(cs.[ID] AS nvarchar(20)) LIKE {like} ESCAPE '\\' OR cs.[INVOICENO] LIKE {like} ESCAPE '\\' OR cs.[ORDERNO] LIKE {like} ESCAPE '\\')");
        }
        if (search.StatusId is { } status) where.Add($"cs.[COSTINGSHEETSTATUSID] = {P(status)}");
        if (search.CompanyId is { } companyId) where.Add($"cs.[COMPANYID] = {P(companyId)}");
        if (search.SalesRepIds is { } reps) where.Add(reps.Count == 0 ? "1 = 0" : $"cs.[SALESMANID] IN ({Ids(reps)})");
        if (search.SpecialistIds is { } specialists)
        {
            var s = Ids(specialists);
            where.Add(specialists.Count == 0 ? "1 = 0" : $"(cs.[MDSSPECIALISTID] IN ({s}) OR cs.[PPSPECIALISTID] IN ({s}) OR cs.[MITSSPECIALISTID] IN ({s}))");
        }
        void Between(string column, DateTime? fromDay, DateTime? toDay)
        {
            if (fromDay is { } f) where.Add($"cs.[{column}] >= {P(f.Date)}");
            if (toDay is { } t) where.Add($"cs.[{column}] < {P(t.Date.AddDays(1))}");
        }
        Between("CREATEDAT", search.CreatedFrom, search.CreatedTo);
        Between("HEADAPPROVEDAT", search.HeadApprovedFrom, search.HeadApprovedTo);
        Between("MININVOICEDATE", search.InvoiceFrom, search.InvoiceTo);
        string None(string column) => $"(cs.[{column}] {NoOne} OR cs.[{column}] = 0)";
        if (search.HasSpecialist == "Yes") where.Add($"NOT ({None("MITSSPECIALISTID")} AND {None("MDSSPECIALISTID")})");
        if (search.HasSpecialist == "No") where.Add($"({None("MITSSPECIALISTID")} AND {None("MDSSPECIALISTID")})");

        var whereSql = " WHERE " + string.Join(" AND ", where);
        var args = parameters.ToArray();
        var total = Convert.ToInt32(Sql.Rows(connection, null, "SELECT COUNT(*) AS [N]" + from + whereSql, args)[0]["N"], CultureInfo.InvariantCulture);
        var order = (search.Sort switch
        {
            "salesman" => "u.[NAME]",
            "reference" => "rf.[REFERENCE]",
            "company" => "co.[COMPANY]",
            "contact" => "ct.[FIRSTNAME]",
            "created" => "cs.[CREATEDAT]",
            "updated" => "cs.[UPDATEDAT]",
            "printed" => "cs.[PRINTEDAT]",
            "achieved" => "cs.[TOTALACHIEVEDPRICE]",
            "status" => "st.[LABEL]",
            "invoice" => "cs.[INVOICENO]",
            "opo" => "cs.[ISOPOCHECKED]",
            _ => "cs.[ID]",
        }) + (search.Descending ? " DESC" : " ASC") + (search.Sort is "id" or "" ? "" : ", cs.[ID] DESC");
        var take = Math.Max(1, Math.Min(search.Take, 500));
        var rows = Sql.Rows(connection, null,
            "SELECT cs.[ID], cs.[SALESMANID], u.[NAME] AS [SALESMAN], rf.[REFERENCE] AS [REFERENCE], cs.[COMPANYID], co.[COMPANY] AS [COMPANY], " +
            "ct.[FIRSTNAME] AS [FIRSTNAME], ct.[LASTNAME] AS [LASTNAME], cs.[CREATEDAT], cs.[UPDATEDAT], cs.[PRINTEDAT], cs.[HEADAPPROVEDAT], cs.[MININVOICEDATE], " +
            "cs.[TOTALACHIEVEDPRICE], st.[LABEL] AS [STATUS], cs.[INVOICENO], cs.[ORDERNO], cs.[ISOPOCHECKED]" + from + whereSql +
            $" ORDER BY {order} OFFSET {Math.Max(0, search.Skip).ToString(CultureInfo.InvariantCulture)} ROWS FETCH NEXT {take.ToString(CultureInfo.InvariantCulture)} ROWS ONLY",
            args);
        return new CostingSheetPage(total, rows.Select(ListRow).ToList());
    });

    /// <summary>A row of the list query (or of the fake's join) as the list shows it.</summary>
    public static CostingSheetRow ListRow(IReadOnlyDictionary<string, object?> r) => new()
    {
        Id = r.Long("ID"),
        SalesmanId = r.Id("SALESMANID"),
        Salesman = r.Text("SALESMAN"),
        Reference = r.Text("REFERENCE"),
        CompanyId = r.Id("COMPANYID"),
        Company = r.Text("COMPANY"),
        Contact = (r.Text("FIRSTNAME") + " " + r.Text("LASTNAME")).Trim(),
        CreatedAt = r.Date("CREATEDAT"),
        UpdatedAt = r.Date("UPDATEDAT"),
        PrintedAt = r.Date("PRINTEDAT"),
        HeadApprovedAt = r.Date("HEADAPPROVEDAT"),
        MinInvoiceDate = r.Date("MININVOICEDATE"),
        TotalAchievedPrice = r.Decimal("TOTALACHIEVEDPRICE"),
        Status = r.Text("STATUS"),
        InvoiceNo = r.Text("INVOICENO"),
        OrderNo = r.Text("ORDERNO"),
        IsOpoChecked = r.Bool("ISOPOCHECKED"),
    };
}
