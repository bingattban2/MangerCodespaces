using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using AlTamam.Application.Excel;

namespace AlTamam.Infrastructure.Excel;

/// <summary>Offline SpreadsheetML writer. Only explicit strings and trusted integers; no formulas, external relationships or macros.</summary>
public sealed class SafeXlsxWriter : IWorkbookWriter
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Relations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRelations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static string Column(int index)
    {
        var text = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) text = (char)('A' + (n - 1) % 26) + text;
        return text;
    }
    private static XmlWriter Entry(ZipArchive zip, string path) => XmlWriter.Create(zip.CreateEntry(path, CompressionLevel.Optimal).Open(),
        new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true, CheckCharacters = true });
    private static void Empty(XmlWriter x, string name, params (string, string)[] attributes)
    {
        x.WriteStartElement(name, Main); foreach (var (key, value) in attributes) x.WriteAttributeString(key, value); x.WriteEndElement();
    }
    private static string SheetName(string proposed, HashSet<string> used)
    {
        var cleaned = new string(proposed.Where(c => !char.IsControl(c) && !"[]:*?/\\".Contains(c)).ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0) cleaned = "كشف";
        // Do not split a UTF-16 surrogate pair at Excel's 31-character boundary.
        static string Truncate(string s, int length) => s.Length <= length ? s : s[..(char.IsHighSurrogate(s[length - 1]) ? length - 1 : length)];
        var name = Truncate(cleaned, 31).Trim('\''); var suffix = 1;
        while (!used.Add(name)) { var end = " (" + (++suffix).ToString(CultureInfo.InvariantCulture) + ")"; name = Truncate(cleaned, 31 - end.Length) + end; }
        return name;
    }
    public void Write(Stream destination, IReadOnlyList<WorkbookSheet> sheets, Action<long>? rowWritten = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(sheets);
        if (!destination.CanWrite || sheets.Count is < 1 or > 256) throw new ArgumentException("A writable stream and 1–256 sheets are required.");
        foreach (var sheet in sheets)
            if (sheet.Headers.Count is < 1 or > 16384 || sheet.FrozenColumns < 0 || sheet.FrozenColumns > Math.Min(sheet.Headers.Count, 16383))
                throw new ArgumentException("Invalid worksheet columns.");
        cancellationToken.ThrowIfCancellationRequested();
        using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        using (var x = Entry(zip, "[Content_Types].xml"))
        {
            const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
            x.WriteStartElement("Types", ns);
            void Type(string element, string key, string value, string content) { x.WriteStartElement(element, ns); x.WriteAttributeString(key, value); x.WriteAttributeString("ContentType", content); x.WriteEndElement(); }
            Type("Default", "Extension", "rels", "application/vnd.openxmlformats-package.relationships+xml");
            Type("Default", "Extension", "xml", "application/xml");
            Type("Override", "PartName", "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            Type("Override", "PartName", "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            for (var i = 1; i <= sheets.Count; i++) Type("Override", "PartName", $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            x.WriteEndElement();
        }
        static void Relationship(XmlWriter x, string id, string type, string target)
        {
            x.WriteStartElement("Relationship", Relations); x.WriteAttributeString("Id", id); x.WriteAttributeString("Type", OfficeRelations + "/" + type); x.WriteAttributeString("Target", target); x.WriteEndElement();
        }
        using (var x = Entry(zip, "_rels/.rels"))
        { x.WriteStartElement("Relationships", Relations); Relationship(x, "rId1", "officeDocument", "xl/workbook.xml"); x.WriteEndElement(); }
        using (var x = Entry(zip, "xl/_rels/workbook.xml.rels"))
        {
            x.WriteStartElement("Relationships", Relations);
            for (var i = 1; i <= sheets.Count; i++) Relationship(x, "rId" + i, "worksheet", $"worksheets/sheet{i}.xml");
            Relationship(x, "styles", "styles", "styles.xml"); x.WriteEndElement();
        }
        using (var x = Entry(zip, "xl/workbook.xml"))
        {
            x.WriteStartElement("workbook", Main); x.WriteAttributeString("xmlns", "r", null, OfficeRelations);
            x.WriteStartElement("bookViews", Main); Empty(x, "workbookView"); x.WriteEndElement();
            x.WriteStartElement("sheets", Main); var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sheets.Count; i++)
            {
                x.WriteStartElement("sheet", Main); x.WriteAttributeString("name", SheetName(sheets[i].Name, used));
                x.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture)); x.WriteAttributeString("r", "id", OfficeRelations, "rId" + (i + 1)); x.WriteEndElement();
            }
            x.WriteEndElement(); x.WriteEndElement();
        }
        using (var x = Entry(zip, "xl/styles.xml"))
        {
            x.WriteStartElement("styleSheet", Main);
            x.WriteStartElement("fonts", Main); x.WriteAttributeString("count", "2");
            for (var i = 0; i < 2; i++) { x.WriteStartElement("font", Main); Empty(x, "sz", ("val", "11")); Empty(x, "name", ("val", "Arial")); if (i == 1) Empty(x, "b"); x.WriteEndElement(); } x.WriteEndElement();
            x.WriteStartElement("fills", Main); x.WriteAttributeString("count", "2");
            foreach (var pattern in new[] { "none", "gray125" }) { x.WriteStartElement("fill", Main); Empty(x, "patternFill", ("patternType", pattern)); x.WriteEndElement(); } x.WriteEndElement();
            x.WriteStartElement("borders", Main); x.WriteAttributeString("count", "1"); x.WriteStartElement("border", Main);
            foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" }) Empty(x, side); x.WriteEndElement(); x.WriteEndElement();
            x.WriteStartElement("cellStyleXfs", Main); x.WriteAttributeString("count", "1"); Empty(x, "xf", ("numFmtId", "0"), ("fontId", "0"), ("fillId", "0"), ("borderId", "0")); x.WriteEndElement();
            x.WriteStartElement("cellXfs", Main); x.WriteAttributeString("count", "2");
            for (var i = 0; i < 2; i++)
            {
                x.WriteStartElement("xf", Main); x.WriteAttributeString("numFmtId", "0"); x.WriteAttributeString("fontId", i.ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("fillId", "0"); x.WriteAttributeString("borderId", "0"); x.WriteAttributeString("xfId", "0"); x.WriteAttributeString("applyAlignment", "1");
                if (i == 1) x.WriteAttributeString("applyFont", "1");
                Empty(x, "alignment", ("horizontal", "right"), ("vertical", "center"), ("readingOrder", "2"), ("wrapText", "1")); x.WriteEndElement();
            }
            x.WriteEndElement(); x.WriteStartElement("cellStyles", Main); x.WriteAttributeString("count", "1"); Empty(x, "cellStyle", ("name", "Normal"), ("xfId", "0"), ("builtinId", "0")); x.WriteEndElement(); x.WriteEndElement();
        }
        long written = 0;
        for (var i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i]; using var x = Entry(zip, $"xl/worksheets/sheet{i + 1}.xml");
            x.WriteStartElement("worksheet", Main); x.WriteStartElement("sheetViews", Main); x.WriteStartElement("sheetView", Main);
            x.WriteAttributeString("workbookViewId", "0"); x.WriteAttributeString("rightToLeft", "1");
            Empty(x, "pane", ("xSplit", sheet.FrozenColumns.ToString(CultureInfo.InvariantCulture)), ("ySplit", "1"), ("topLeftCell", Column(sheet.FrozenColumns) + "2"), ("activePane", sheet.FrozenColumns == 0 ? "bottomLeft" : "bottomRight"), ("state", "frozen"));
            x.WriteEndElement(); x.WriteEndElement();
            Empty(x, "sheetFormatPr", ("defaultRowHeight", "24"));
            x.WriteStartElement("cols", Main);
            for (var col = 0; col < sheet.Headers.Count; col++) Empty(x, "col", ("min", (col + 1).ToString(CultureInfo.InvariantCulture)), ("max", (col + 1).ToString(CultureInfo.InvariantCulture)), ("width", col < sheet.FrozenColumns ? "22" : "14"), ("customWidth", "1"));
            x.WriteEndElement(); x.WriteStartElement("sheetData", Main);
            void Row(int number, IReadOnlyList<WorkbookCell> cells, bool header)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (number > 1048576 || cells.Count != sheet.Headers.Count) throw new InvalidDataException("Worksheet size or row width is invalid.");
                x.WriteStartElement("row", Main); x.WriteAttributeString("r", number.ToString(CultureInfo.InvariantCulture));
                for (var c = 0; c < cells.Count; c++)
                {
                    var cell = cells[c]; if (cell.Text.Length > 32767) throw new InvalidDataException("Cell text exceeds Excel's limit.");
                    x.WriteStartElement("c", Main); x.WriteAttributeString("r", Column(c) + number.ToString(CultureInfo.InvariantCulture)); if (header) x.WriteAttributeString("s", "1");
                    if (cell.Integer is long value) { x.WriteAttributeString("t", "n"); x.WriteElementString("v", Main, value.ToString(CultureInfo.InvariantCulture)); }
                    else
                    {
                        x.WriteAttributeString("t", "inlineStr"); x.WriteStartElement("is", Main); x.WriteStartElement("t", Main);
                        x.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve"); x.WriteString(cell.Text); x.WriteEndElement(); x.WriteEndElement();
                    }
                    x.WriteEndElement();
                }
                x.WriteEndElement();
            }
            Row(1, sheet.Headers.Select(h => new WorkbookCell(h)).ToArray(), true); var rowNumber = 1;
            foreach (var row in sheet.Rows) { Row(++rowNumber, row, false); rowWritten?.Invoke(++written); }
            x.WriteEndElement(); Empty(x, "autoFilter", ("ref", $"A1:{Column(sheet.Headers.Count - 1)}{rowNumber}"));
            Empty(x, "pageMargins", ("left", "0.25"), ("right", "0.25"), ("top", "0.5"), ("bottom", "0.5"), ("header", "0.2"), ("footer", "0.2")); x.WriteEndElement();
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
