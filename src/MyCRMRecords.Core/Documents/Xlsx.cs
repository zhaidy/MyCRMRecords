using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MyCRMRecords.Documents;

/// <summary>
/// A one-sheet Excel workbook (.xlsx) written directly: a bold header row that stays on screen with filters on it,
/// then text, numbers and dates.
/// </summary>
public static class Xlsx
{
    /// <summary>A column: its heading, width in characters, and kind ("text", "money", "number" or "date").</summary>
    public sealed record ColumnSpec(string Heading, double Width, string Kind = "text");

    /// <summary>One sheet of a workbook: its name, columns and rows.</summary>
    public sealed record Sheet(string Name, IReadOnlyList<ColumnSpec> Columns, IEnumerable<object?[]> Rows);

    public static byte[] Create(string sheetName, IReadOnlyList<ColumnSpec> columns, IEnumerable<object?[]> rows) => Create([new Sheet(sheetName, columns, rows)]);

    /// <summary>A workbook of several sheets (names cut to Excel's 31 characters and made unique).</summary>
    public static byte[] Create(IReadOnlyList<Sheet> sheets)
    {
        if (sheets.Count == 0) throw new ArgumentException("A workbook needs a sheet.", nameof(sheets));
        var names = new List<string>();
        foreach (var sheet in sheets)
        {
            var name = new string(sheet.Name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
            if (name.Length == 0) name = "Sheet";
            if (name.Length > 31) name = name.Substring(0, 31);
            var unique = name;
            for (var n = 2; names.Contains(unique, StringComparer.OrdinalIgnoreCase); n++)
            {
                var suffix = " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
                unique = (name.Length + suffix.Length > 31 ? name.Substring(0, 31 - suffix.Length) : name) + suffix;
            }
            names.Add(unique);
        }
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Entry(string name, string content)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
            var overrides = string.Concat(sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
            Entry("[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>""" + overrides + """<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""");
            Entry("_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            var styleId = sheets.Count + 1;
            Entry("xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">"""
                + string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
                + $"<Relationship Id=\"rId{styleId}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Entry("xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>"""
                + string.Concat(names.Select((n, i) => $"<sheet name=\"{Escape(n)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets><definedNames>"
                + string.Concat(names.Select((n, i) => $"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{Escape(n.Replace("'", "''"))}'!$A$1:${ColumnName(Math.Max(sheets[i].Columns.Count, 1) - 1)}$1</definedName>"))
                + "</definedNames></workbook>");
            // Styles: 0 normal, 1 bold header, 2 date, 3 money (#,##0.00), 4 number.
            Entry("xl/styles.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="dd-mmm-yyyy"/></numFmts><fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE4E8FC"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");

            for (var s = 0; s < sheets.Count; s++)
            {
                var columns = sheets[s].Columns;
                var rows = sheets[s].Rows;
                var sheet = new StringBuilder();
                sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><cols>""");
                for (var i = 0; i < columns.Count; i++)
                    sheet.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{columns[i].Width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
                sheet.Append("</cols><sheetData><row r=\"1\">");
                for (var i = 0; i < columns.Count; i++) sheet.Append(TextCell(i, 1, columns[i].Heading, 1));
                sheet.Append("</row>");
                var rowNumber = 1;
                foreach (var row in rows)
                {
                    rowNumber++;
                    sheet.Append($"<row r=\"{rowNumber}\">");
                    for (var i = 0; i < columns.Count && i < row.Length; i++)
                    {
                        var value = row[i];
                        if (value is null) continue;
                        var reference = ColumnName(i) + rowNumber;
                        switch (value)
                        {
                            case DateTime date:
                                sheet.Append($"<c r=\"{reference}\" s=\"2\"><v>{date.ToOADate().ToString(CultureInfo.InvariantCulture)}</v></c>");
                                break;
                            case decimal number:
                                sheet.Append($"<c r=\"{reference}\" s=\"{(columns[i].Kind == "money" ? 3 : 4)}\"><v>{number.ToString(CultureInfo.InvariantCulture)}</v></c>");
                                break;
                            case int or long or double:
                                sheet.Append($"<c r=\"{reference}\" s=\"4\"><v>{Convert.ToString(value, CultureInfo.InvariantCulture)}</v></c>");
                                break;
                            default:
                                sheet.Append(TextCell(i, rowNumber, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", 0));
                                break;
                        }
                    }
                    sheet.Append("</row>");
                }
                sheet.Append($"</sheetData><autoFilter ref=\"A1:{ColumnName(columns.Count - 1)}{Math.Max(rowNumber, 1)}\"/></worksheet>");
                Entry($"xl/worksheets/sheet{s + 1}.xml", sheet.ToString());
            }
        }
        return output.ToArray();
    }

    private static string TextCell(int column, int row, string text, int style) =>
        $"<c r=\"{ColumnName(column)}{row}\" t=\"inlineStr\"{(style > 0 ? $" s=\"{style}\"" : "")}><is><t xml:space=\"preserve\">{Escape(Clean(text))}</t></is></c>";

    /// <summary>A, B, …, Z, AA, AB, …</summary>
    public static string ColumnName(int index)
    {
        var name = "";
        index++;
        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            name = (char)('A' + remainder) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    private static string Escape(string text) => SecurityElement.Escape(text) ?? "";

    /// <summary>Without the control characters XML cannot hold.</summary>
    private static string Clean(string text) =>
        new(text.Where(c => c == '\t' || c == '\n' || c == '\r' || c >= ' ' && c != '￾' && c != '￿').ToArray());

    /// <summary>
    /// The first sheet of a workbook as rows of text, by column heading (the first row): what OutSystems' Excel
    /// uploads read. Throws <see cref="InvalidDataException"/> when the file is not an .xlsx workbook.
    /// </summary>
    public static List<Dictionary<string, string>> Read(Stream file)
    {
        try
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            var shared = new List<string>();
            var strings = zip.GetEntry("xl/sharedStrings.xml");
            if (strings is not null)
            {
                using var stream = strings.Open();
                var document = System.Xml.Linq.XDocument.Load(stream);
                System.Xml.Linq.XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                foreach (var si in document.Root!.Elements(ns + "si"))
                    shared.Add(string.Concat(si.Descendants(ns + "t").Select(t => t.Value)));
            }
            var sheet = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.FullName.Length).ThenBy(e => e.FullName, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidDataException("The workbook has no sheet.");
            System.Xml.Linq.XDocument xml;
            using (var stream = sheet.Open()) xml = System.Xml.Linq.XDocument.Load(stream);
            System.Xml.Linq.XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var rows = new List<Dictionary<int, string>>();
            foreach (var row in xml.Descendants(m + "row"))
            {
                var cells = new Dictionary<int, string>();
                var next = 0;
                foreach (var c in row.Elements(m + "c"))
                {
                    var reference = (string?)c.Attribute("r") ?? "";
                    var column = reference.Length > 0 ? ColumnIndex(reference) : next;
                    next = column + 1;
                    var type = (string?)c.Attribute("t") ?? "";
                    var value = (string?)c.Element(m + "v") ?? "";
                    var text = type switch
                    {
                        "s" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < shared.Count ? shared[index] : "",
                        "inlineStr" => string.Concat(c.Descendants(m + "t").Select(t => t.Value)),
                        _ => value,
                    };
                    cells[column] = text.Trim();
                }
                rows.Add(cells);
            }
            if (rows.Count == 0) return new List<Dictionary<string, string>>();
            var headings = rows[0];
            return rows.Skip(1).Where(r => r.Values.Any(v => v.Length > 0))
                .Select(r => headings.Where(h => h.Value.Length > 0).GroupBy(h => h.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .ToDictionary(h => h.Value, h => r.TryGetValue(h.Key, out var v) ? v : "", StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            throw new InvalidDataException("The file is not an Excel workbook (.xlsx): " + ex.Message);
        }
    }

    /// <summary>"C7" → 2.</summary>
    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var ch in reference)
        {
            if (ch < 'A' || ch > 'Z') break;
            index = index * 26 + (ch - 'A' + 1);
        }
        return Math.Max(0, index - 1);
    }
}
