using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Kanbada.Api;

public sealed class ExportWorkbook(int maxRowsPerSheet = 1_048_576) : IDisposable
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Namespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private readonly List<SheetFile> sheets = [];
    private readonly Dictionary<string, SheetFile> current = [];

    private sealed class SheetFile(string name, string[] columns) : IDisposable
    {
        public string Name { get; } = name;
        public string[] Columns { get; } = columns;
        public FileStream File { get; } = TemporaryFile();
        public XmlWriter Writer { get; set; } = null!;
        public int Rows { get; set; }
        public bool Finished { get; set; }
        public void Dispose() { Writer?.Dispose(); File.Dispose(); }
    }

    private static FileStream TemporaryFile()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(Path.Combine(Path.GetTempPath(), $"kanbada-export-sheet-{Guid.NewGuid():N}.tmp"), options);
    }

    public void Table(string name, params string[] columns)
    {
        if (maxRowsPerSheet is < 2 or > 1_048_576) throw new ArgumentOutOfRangeException(nameof(maxRowsPerSheet));
        if (current.ContainsKey(name)) return;
        NewSheet(name, columns);
    }

    private SheetFile NewSheet(string name, string[] columns)
    {
        var number = sheets.Count(s => s.Name == name || s.Name.StartsWith(name + " (", StringComparison.Ordinal)) + 1;
        var sheet = new SheetFile(number == 1 ? name : $"{name} ({number})", columns);
        sheets.Add(sheet);
        current[name] = sheet;
        var xml = sheet.Writer = XmlWriter.Create(sheet.File, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false, NewLineHandling = NewLineHandling.Entitize });
        xml.WriteStartDocument();
        xml.WriteStartElement("worksheet", Namespace);
        xml.WriteStartElement("sheetViews", Namespace);
        xml.WriteStartElement("sheetView", Namespace);
        xml.WriteAttributeString("workbookViewId", "0");
        xml.WriteStartElement("pane", Namespace);
        xml.WriteAttributeString("ySplit", "1");
        xml.WriteAttributeString("topLeftCell", "A2");
        xml.WriteAttributeString("activePane", "bottomLeft");
        xml.WriteAttributeString("state", "frozen");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteStartElement("cols", Namespace);
        for (var i = 0; i < columns.Length; i++)
        {
            xml.WriteStartElement("col", Namespace);
            xml.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("width", columns[i] is "title" or "description" or "text" ? "55" : "24");
            xml.WriteAttributeString("customWidth", "1");
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteStartElement("sheetData", Namespace);
        WriteRow(sheet, columns.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray(), header: true);
        return sheet;
    }

    public void Row(string name, params JsonNode?[] values)
    {
        var sheet = current[name];
        if (values.Length != sheet.Columns.Length) throw new ArgumentException("Row does not match workbook columns.");
        if (sheet.Rows >= maxRowsPerSheet)
        {
            Finish(sheet);
            sheet = NewSheet(name, sheet.Columns);
        }
        var cells = values.ToArray();
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length <= 32767) continue;
            // Excel limits cell text to 32,767 UTF-16 units. Preserve the complete value in ordered parts.
            Table("Long text", "sheet", "row", "column", "part", "text");
            var part = 0;
            for (var offset = 0; offset < text.Length;)
            {
                var length = Math.Min(32767, text.Length - offset);
                if (offset + length < text.Length && char.IsHighSurrogate(text[offset + length - 1])) length--;
                var chunk = text.Substring(offset, length);
                if (part == 0) cells[i] = JsonValue.Create(chunk);
                Row("Long text", JsonValue.Create(sheet.Name), JsonValue.Create(sheet.Rows + 1), JsonValue.Create(sheet.Columns[i]), JsonValue.Create(++part), JsonValue.Create(chunk));
                offset += length;
            }
        }
        WriteRow(sheet, cells);
    }

    public void Object(string name, JsonNode? value, params JsonNode?[] prefix) =>
        Row(name, prefix.Concat(current[name].Columns.Skip(prefix.Length).Select(column => value?[column]?.DeepClone())).ToArray());

    private static string Column(int index)
    {
        var result = "";
        for (index++; index > 0; index = (index - 1) / 26) result = (char)('A' + (index - 1) % 26) + result;
        return result;
    }

    private static string EscapeText(string text)
    {
        text = Regex.Replace(text, "_x[0-9A-Fa-f]{4}_", match => "_x005F_" + match.Value[1..]);
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                result.Append(ch).Append(text[++i]);
            else if (XmlConvert.IsXmlChar(ch)) result.Append(ch);
            else result.Append("_x").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture)).Append('_');
        }
        return result.ToString();
    }

    private static void WriteRow(SheetFile sheet, JsonNode?[] values, bool header = false)
    {
        var xml = sheet.Writer;
        xml.WriteStartElement("row", Namespace);
        xml.WriteAttributeString("r", (++sheet.Rows).ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < values.Length; i++)
        {
            xml.WriteStartElement("c", Namespace);
            xml.WriteAttributeString("r", Column(i) + sheet.Rows);
            xml.WriteAttributeString("s", header ? "1" : "0");
            if (values[i] is JsonValue value && value.TryGetValue<bool>(out var boolean))
            {
                xml.WriteAttributeString("t", "b");
                xml.WriteElementString("v", Namespace, boolean ? "1" : "0");
            }
            else if (values[i] is JsonValue number && number.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            {
                xml.WriteAttributeString("t", "n");
                xml.WriteElementString("v", Namespace, number.ToJsonString());
            }
            else
            {
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is", Namespace);
                xml.WriteStartElement("t", Namespace);
                xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                xml.WriteString(EscapeText(values[i]?.ToString() ?? ""));
                xml.WriteEndElement();
                xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    private static void Finish(SheetFile sheet)
    {
        if (sheet.Finished) return;
        sheet.Writer.WriteEndElement();
        sheet.Writer.WriteStartElement("autoFilter", Namespace);
        sheet.Writer.WriteAttributeString("ref", "A1:" + Column(sheet.Columns.Length - 1) + sheet.Rows);
        sheet.Writer.WriteEndElement();
        sheet.Writer.WriteEndElement();
        sheet.Writer.WriteEndDocument();
        sheet.Writer.Flush();
        sheet.Finished = true;
        sheet.File.Position = 0;
    }

    public async Task Save(Stream output, CancellationToken ct)
    {
        // Create-mode ZIP streams entries directly; read/write package mode can buffer entire worksheets.
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string documentRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
        var contentTypes = new XElement(types + "Types",
            new XElement(types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(types + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
        var links = new XElement(relationships + "Relationships",
            new XElement(relationships + "Relationship", new XAttribute("Id", "styles"), new XAttribute("Type", documentRelationships + "styles"), new XAttribute("Target", "styles.xml")));
        void Xml(string name, XElement root)
        {
            using var stream = archive.CreateEntry(name).Open();
            new XDocument(root).Save(stream);
        }
        Xml("_rels/.rels", new XElement(relationships + "Relationships",
            new XElement(relationships + "Relationship", new XAttribute("Id", "workbook"), new XAttribute("Type", documentRelationships + "officeDocument"), new XAttribute("Target", "xl/workbook.xml"))));
        var workbook = new Workbook(new Sheets());
        var styles = new Stylesheet(
            new Fonts(new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }), new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
            new Borders(new Border()), new CellStyleFormats(new CellFormat()),
            new CellFormats(new CellFormat(), new CellFormat { FontId = 1, ApplyFont = true }));
        using (var stream = archive.CreateEntry("xl/styles.xml").Open()) styles.Save(stream);
        uint id = 0;
        foreach (var sheet in sheets)
        {
            ct.ThrowIfCancellationRequested();
            Finish(sheet);
            var path = $"worksheets/sheet{++id}.xml";
            using (var stream = archive.CreateEntry("xl/" + path).Open()) await sheet.File.CopyToAsync(stream, ct);
            workbook.GetFirstChild<Sheets>()!.Append(new Sheet { Name = sheet.Name, SheetId = id, Id = "sheet" + id });
            links.Add(new XElement(relationships + "Relationship", new XAttribute("Id", "sheet" + id), new XAttribute("Type", documentRelationships + "worksheet"), new XAttribute("Target", path)));
            contentTypes.Add(new XElement(types + "Override", new XAttribute("PartName", "/xl/" + path), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }
        using (var stream = archive.CreateEntry("xl/workbook.xml").Open()) workbook.Save(stream);
        Xml("xl/_rels/workbook.xml.rels", links);
        Xml("[Content_Types].xml", contentTypes);
    }

    public void Metadata(JsonObject state, string? project)
    {
        Table("Cards", "id", "project", "title", "description", "status", "priority", "due", "bucket", "swimlane", "readOnly", "cover");
        Table("Card labels", "cardId", "position", "label");
        Table("Assignees", "cardId", "position", "assignee");
        Table("Comments", "cardId", "position", "text");
        Table("Checklist", "cardId", "position", "text", "done");
        Table("History", "cardId", "id", "at", "actor");
        Table("History changes", "cardId", "historyId", "position", "text");
        Table("Attachments", "cardId", "id", "name", "size", "type", "addedAt");
        Table("Projects", "id", "name", "description", "color", "archived", "system");
        foreach (var row in WorkspaceJson.Items(state, "projects").Where(p => project is null || WorkspaceJson.Text(p, "id") == project)) Object("Projects", row);
        if (project is not null) return;
        Table("Workspace", "id", "name", "personal", "ownerId", "canManage", "icon", "banner", "bannerPosition");
        Object("Workspace", state["workspace"]);
        Table("Snapshot", "version");
        Object("Snapshot", state);
        Table("Members", "userId", "name", "email", "initials", "color", "photo");
        foreach (var member in WorkspaceJson.Items(state, "members")) Object("Members", member);
        foreach (var name in new[] { "statuses", "buckets", "labels", "swimlanes" })
        {
            var title = char.ToUpperInvariant(name[0]) + name[1..];
            Table(title, name == "swimlanes" ? ["id", "name", "color", "complete", "project"] : ["id", "name", "color", "complete"]);
            foreach (var row in WorkspaceJson.Items(state, name)) Object(title, row);
        }
        Table("Activity", "text");
        foreach (var text in WorkspaceJson.Items(state, "activity")) Row("Activity", text?.DeepClone());
        Table("Notifications", "id", "message", "at", "cardId");
    }

    public void Card(JsonNode card)
    {
        Object("Cards", card);
        JsonNode Id() => card["id"]!.DeepClone();
        foreach (var (key, name) in new[] { ("labels", "Card labels"), ("assignees", "Assignees"), ("comments", "Comments") })
        {
            var rows = WorkspaceJson.Items(card, key);
            for (var i = 0; i < rows.Count; i++) Row(name, Id(), JsonValue.Create(i + 1), rows[i]?.DeepClone());
        }
        var checklist = WorkspaceJson.Items(card, "checklist");
        for (var i = 0; i < checklist.Count; i++) Object("Checklist", checklist[i], Id(), JsonValue.Create(i + 1));
        foreach (var entry in WorkspaceJson.Items(card, "history"))
        {
            Object("History", entry, Id());
            var changes = WorkspaceJson.Items(entry, "changes");
            for (var i = 0; i < changes.Count; i++) Row("History changes", Id(), entry!["id"]!.DeepClone(), JsonValue.Create(i + 1), changes[i]?.DeepClone());
        }
        foreach (var attachment in WorkspaceJson.Items(card, "attachments")) Object("Attachments", attachment, Id());
    }

    public void Dispose()
    {
        foreach (var sheet in sheets) sheet.Dispose();
    }
}
