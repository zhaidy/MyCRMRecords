using System.Globalization;
using System.IO.Compression;
using MyCRMRecords.Data;

namespace MyCRMRecords.Domain;

/// <summary>
/// MyFiles' files (the PDFs MyFile keeps: costing sheets, request forms, FSAs, leasing papers, DOs…), read only: the
/// Files page, a company's Files tab, and one file's PDF (shown or downloaded) or several as a zip. Nothing here writes.
/// Who sees which is MyFile's rule with MyCRMRecords' company rule (<see cref="Access"/>): active files only; the roles
/// that open every company see every file; everyone else the files of the companies they may open and the files of no
/// company. (MyFile's department rule is not applied: MyCRMRecords does not know a person's department.)
/// </summary>
public sealed class MyFiles(IMyFilesData data, Access access, IRecordStore store, Func<DateTime> clock)
{
    public const int PageSize = 50;
    /// <summary>At most this many files in one zip (a page of the list).</summary>
    public const int ZipMax = 50;

    public IMyFilesData Data => data;

    /// <summary>A page of the files the person may see, matching the search, newest first.</summary>
    public FilePage List(Actor actor, FileSearch search)
    {
        search.Companies = access.CompanyIds(actor);
        search.Skip = Math.Max(0, search.Skip);
        search.Take = PageSize;
        return data.Search(search);
    }

    /// <summary>Whether the person's list is only some companies' files (and those of no company).</summary>
    public static bool Limited(Actor actor) => !actor.SeesAllCompanies;

    /// <summary>Whether the person may open the MyCRM company (and so see its files).</summary>
    public bool MayOpenCompany(Actor actor, long companyId) =>
        actor.SeesAllCompanies || (store.Get<Company>(companyId) is { } company && access.CanOpen(actor, company));

    /// <summary>Whether the person may see the file: active, and of no company or of a company they may open.</summary>
    public bool MaySee(Actor actor, FileRow file) => file.IsActive && (file.CompanyId is not { } company || MayOpenCompany(actor, company));

    /// <summary>An active file, or null; whether the person may see it is <see cref="MaySee"/>.</summary>
    public FileRow? Get(long id) => id > 0 && data.Get(id) is { IsActive: true } file ? file : null;

    /// <summary>The PDF of a file, or null.</summary>
    public byte[]? Bytes(FileRow file) => data.Bytes(file.Id);

    /// <summary>The name a file is saved under: its own, or "file 12.pdf" when it has none.</summary>
    public static string NameOf(FileRow file) => file.FileName.Trim().Length > 0 ? file.FileName.Trim() : "file " + file.Id.ToString(CultureInfo.InvariantCulture) + ".pdf";

    /// <summary>Several files as one zip, each under its own name: "MyFiles yyyy-MM-dd HH-mm-ss.zip", as MyFile names it.</summary>
    public (string Name, byte[] Zip) Zip(IReadOnlyList<FileRow> files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                if (Bytes(file) is not { } bytes) continue;
                var name = NameOf(file);
                // Two files of the same name: the second gets its Id, so neither is lost.
                if (!names.Add(name)) name = Path.GetFileNameWithoutExtension(name) + " (" + file.Id.ToString(CultureInfo.InvariantCulture) + ")" + Path.GetExtension(name);
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = clock();
                using var stream = entry.Open();
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return ("MyFiles " + clock().ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) + ".zip", memory.ToArray());
    }
}
