using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TbkReport.Models;

namespace TbkReport.Services;

public sealed class ExcelReportExporter
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingMainNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const int FirstCommentRow = 28;
    private const int ReservedCommentRows = 10;
    private const int TemplateProblemHeadingRow = 35;
    private const int TemplateProblemTextRow = 38;
    private const int ReportBodyEndRow = 58;
    private const int CharactersPerProblemLine = 38;
    private const double DefaultRowHeight = 22.8;

    public void Export(string templatePath, string outputPath, ReportExportData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(data);
        if (!File.Exists(templatePath)) throw new FileNotFoundException("Не найден шаблон отчёта.", templatePath);

        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (string.Equals(Path.GetFullPath(templatePath), output, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Нельзя сохранять отчёт поверх шаблона.");

        var temporary = Path.Combine(Path.GetDirectoryName(output)!, $".{Path.GetFileName(output)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var worksheetFound = false;
            using var sourceStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using (var source = new ZipArchive(sourceStream, ZipArchiveMode.Read))
            using (var destination = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                var legacySignatures = FindTemplateSignatureDrawings(source);
                var signatures = PrepareSignatures(source, data.Header);
                foreach (var entry in source.Entries)
                {
                    if (entry.FullName == "xl/calcChain.xml") continue;
                    if (legacySignatures.MediaEntries.Contains(entry.FullName)) continue;
                    var newEntry = destination.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                    using var input = entry.Open();
                    using var target = newEntry.Open();
                    if (entry.FullName == "xl/worksheets/sheet1.xml")
                    {
                        worksheetFound = true;
                        var sheet = XDocument.Load(input);
                        PopulateSheet(sheet, data);
                        sheet.Save(target, SaveOptions.DisableFormatting);
                    }
                    else if (entry.FullName == "xl/_rels/workbook.xml.rels")
                    {
                        var relationships = XDocument.Load(input);
                        relationships.Root?.Elements(PackageRelNs + "Relationship")
                            .Where(relationship => ((string?)relationship.Attribute("Type"))?.EndsWith("/calcChain", StringComparison.Ordinal) == true)
                            .Remove();
                        relationships.Save(target, SaveOptions.DisableFormatting);
                    }
                    else if (entry.FullName == "[Content_Types].xml")
                    {
                        var contentTypes = XDocument.Load(input);
                        contentTypes.Root?.Elements(ContentTypeNs + "Override")
                            .Where(item => (string?)item.Attribute("PartName") == "/xl/calcChain.xml")
                            .Remove();
                        if (signatures.Count > 0 && contentTypes.Root?.Elements(ContentTypeNs + "Default")
                                .All(item => !string.Equals((string?)item.Attribute("Extension"), "png", StringComparison.OrdinalIgnoreCase)) == true)
                            contentTypes.Root.Add(new XElement(ContentTypeNs + "Default",
                                new XAttribute("Extension", "png"), new XAttribute("ContentType", "image/png")));
                        contentTypes.Save(target, SaveOptions.DisableFormatting);
                    }
                    else if (entry.FullName == "xl/drawings/drawing1.xml")
                    {
                        var drawing = XDocument.Load(input);
                        RemoveTemplateSignatureDrawings(drawing);
                        if (signatures.Count > 0) AddSignatureDrawings(drawing, signatures);
                        drawing.Save(target, SaveOptions.DisableFormatting);
                    }
                    else if (entry.FullName == "xl/drawings/_rels/drawing1.xml.rels")
                    {
                        var relationships = XDocument.Load(input);
                        relationships.Root?.Elements(PackageRelNs + "Relationship")
                            .Where(item => legacySignatures.RelationshipIds.Contains((string?)item.Attribute("Id") ?? string.Empty))
                            .Remove();
                        foreach (var signature in signatures)
                            relationships.Root?.Add(new XElement(PackageRelNs + "Relationship",
                                new XAttribute("Id", signature.RelationshipId),
                                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                                new XAttribute("Target", $"../media/{signature.FileName}")));
                        relationships.Save(target, SaveOptions.DisableFormatting);
                    }
                    else if (entry.FullName == "xl/workbook.xml")
                    {
                        var workbook = XDocument.Load(input);
                        var calculation = workbook.Root?.Element(MainNs + "calcPr");
                        if (calculation is null)
                        {
                            calculation = new XElement(MainNs + "calcPr");
                            workbook.Root?.Add(calculation);
                        }
                        calculation.SetAttributeValue("calcMode", "auto");
                        calculation.SetAttributeValue("fullCalcOnLoad", "1");
                        calculation.SetAttributeValue("forceFullCalc", "1");
                        calculation.SetAttributeValue("calcOnSave", "1");
                        workbook.Save(target, SaveOptions.DisableFormatting);
                    }
                    else input.CopyTo(target);
                }
                foreach (var signature in signatures)
                {
                    var imageEntry = destination.CreateEntry($"xl/media/{signature.FileName}", CompressionLevel.Optimal);
                    using var imageTarget = imageEntry.Open();
                    imageTarget.Write(signature.Png);
                }
            }
            if (!worksheetFound) throw new InvalidDataException("В шаблоне не найден основной лист отчёта.");
            File.Move(temporary, output, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static TemplateSignatureDrawings FindTemplateSignatureDrawings(ZipArchive source)
    {
        var relationshipIds = new HashSet<string>(StringComparer.Ordinal);
        var drawingEntry = source.GetEntry("xl/drawings/drawing1.xml");
        if (drawingEntry is not null)
        {
            using var stream = drawingEntry.Open();
            var drawing = XDocument.Load(stream);
            foreach (var anchor in TemplateSignatureAnchors(drawing))
            {
                var relationshipId = (string?)anchor.Descendants(DrawingMainNs + "blip")
                    .FirstOrDefault()?.Attribute(OfficeRelNs + "embed");
                if (!string.IsNullOrWhiteSpace(relationshipId)) relationshipIds.Add(relationshipId);
            }
        }

        var mediaEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var relationshipsEntry = source.GetEntry("xl/drawings/_rels/drawing1.xml.rels");
        if (relationshipsEntry is not null && relationshipIds.Count > 0)
        {
            using var stream = relationshipsEntry.Open();
            var relationships = XDocument.Load(stream);
            foreach (var relationship in relationships.Root?.Elements(PackageRelNs + "Relationship")
                         .Where(item => relationshipIds.Contains((string?)item.Attribute("Id") ?? string.Empty))
                     ?? Enumerable.Empty<XElement>())
            {
                var target = (string?)relationship.Attribute("Target");
                if (!string.IsNullOrWhiteSpace(target) &&
                    !string.Equals((string?)relationship.Attribute("TargetMode"), "External", StringComparison.Ordinal))
                    mediaEntries.Add($"xl/media/{Path.GetFileName(target.Replace('/', Path.DirectorySeparatorChar))}");
            }
        }

        return new TemplateSignatureDrawings(relationshipIds, mediaEntries);
    }

    private static void RemoveTemplateSignatureDrawings(XDocument drawing) => TemplateSignatureAnchors(drawing).Remove();

    private static IEnumerable<XElement> TemplateSignatureAnchors(XDocument drawing) =>
        drawing.Root?.Elements().Where(anchor =>
        {
            var from = anchor.Element(DrawingNs + "from");
            var column = (int?)from?.Element(DrawingNs + "col");
            var row = (int?)from?.Element(DrawingNs + "row");
            return (column is 2 or 6) && row is >= 12 and <= 15;
        }) ?? Enumerable.Empty<XElement>();

    private sealed record TemplateSignatureDrawings(HashSet<string> RelationshipIds, HashSet<string> MediaEntries);

    private static IReadOnlyList<SignatureImage> PrepareSignatures(ZipArchive source, ReportHeaderData? header)
    {
        var values = new[]
        {
            (Value: header?.FirstShiftSignature, Column: 2, FileName: "tbk-signature-first.png", Name: "Подпись первого техника первой смены"),
            (Value: header?.SecondShiftSignature, Column: 6, FileName: "tbk-signature-second.png", Name: "Подпись первого техника второй смены")
        };
        var rendered = values.Select(value => (value, Png: SignatureData.RenderPng(value.Value)))
            .Where(item => item.Png is not null).ToArray();
        if (rendered.Length == 0) return Array.Empty<SignatureImage>();

        var drawingEntry = source.GetEntry("xl/drawings/drawing1.xml")
            ?? throw new InvalidDataException("В шаблоне отсутствует слой рисунков для подписи.");
        var relationshipsEntry = source.GetEntry("xl/drawings/_rels/drawing1.xml.rels")
            ?? throw new InvalidDataException("В шаблоне отсутствуют связи рисунков для подписи.");
        int nextRelationshipId;
        using (var stream = relationshipsEntry.Open())
        {
            var relationships = XDocument.Load(stream);
            nextRelationshipId = relationships.Root?.Elements(PackageRelNs + "Relationship")
                .Select(item => (string?)item.Attribute("Id"))
                .Select(id => id is not null && id.StartsWith("rId", StringComparison.Ordinal) && int.TryParse(id[3..], out var number) ? number : 0)
                .DefaultIfEmpty(0).Max() + 1 ?? 1;
        }
        int nextDrawingId;
        using (var stream = drawingEntry.Open())
        {
            var drawing = XDocument.Load(stream);
            nextDrawingId = drawing.Descendants(DrawingNs + "cNvPr")
                .Select(item => (int?)item.Attribute("id") ?? 0).DefaultIfEmpty(0).Max() + 1;
        }

        return rendered.Select((item, index) => new SignatureImage(item.value.Column, item.value.FileName,
            item.value.Name, $"rId{nextRelationshipId + index}", nextDrawingId + index, item.Png!)).ToArray();
    }

    private static void AddSignatureDrawings(XDocument drawing, IReadOnlyList<SignatureImage> signatures)
    {
        var root = drawing.Root ?? throw new InvalidDataException("В шаблоне повреждён слой рисунков.");
        foreach (var signature in signatures)
        {
            root.Add(new XElement(DrawingNs + "twoCellAnchor", new XAttribute("editAs", "oneCell"),
                new XElement(DrawingNs + "from",
                    new XElement(DrawingNs + "col", signature.Column),
                    new XElement(DrawingNs + "colOff", 0),
                    new XElement(DrawingNs + "row", 13),
                    new XElement(DrawingNs + "rowOff", 0)),
                new XElement(DrawingNs + "to",
                    new XElement(DrawingNs + "col", signature.Column + 1),
                    new XElement(DrawingNs + "colOff", 0),
                    new XElement(DrawingNs + "row", 15),
                    new XElement(DrawingNs + "rowOff", 0)),
                new XElement(DrawingNs + "pic",
                    new XElement(DrawingNs + "nvPicPr",
                        new XElement(DrawingNs + "cNvPr", new XAttribute("id", signature.DrawingId), new XAttribute("name", signature.Name)),
                        new XElement(DrawingNs + "cNvPicPr", new XElement(DrawingMainNs + "picLocks", new XAttribute("noChangeAspect", 1)))),
                    new XElement(DrawingNs + "blipFill",
                        new XElement(DrawingMainNs + "blip", new XAttribute(OfficeRelNs + "embed", signature.RelationshipId)),
                        new XElement(DrawingMainNs + "stretch", new XElement(DrawingMainNs + "fillRect"))),
                    new XElement(DrawingNs + "spPr",
                        new XElement(DrawingMainNs + "xfrm",
                            new XElement(DrawingMainNs + "off",
                                new XAttribute("x", signature.Column == 2 ? 2461260 : 6423660),
                                new XAttribute("y", 3764280)),
                            new XElement(DrawingMainNs + "ext", new XAttribute("cx", 1285875), new XAttribute("cy", 579120))),
                        new XElement(DrawingMainNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(DrawingMainNs + "avLst")))),
                new XElement(DrawingNs + "clientData")));
        }
    }

    private sealed record SignatureImage(int Column, string FileName, string Name, string RelationshipId, int DrawingId, byte[] Png);

    private static void PopulateSheet(XDocument sheet, ReportExportData data)
    {
        var root = sheet.Root ?? throw new InvalidDataException("В шаблоне отсутствует лист отчёта.");
        var sheetData = root.Element(MainNs + "sheetData") ?? throw new InvalidDataException("В шаблоне отсутствуют строки отчёта.");
        var firstCommentCapacity = CommentLineCapacity(root, 2);
        var secondCommentCapacity = CommentLineCapacity(root, 6);
        var firstRows = LayoutComments(data.FirstShift, firstCommentCapacity);
        var secondRows = LayoutComments(data.SecondShift, secondCommentCapacity);
        var overflow = Math.Max(0, Math.Max(firstRows, secondRows) - ReservedCommentRows);
        if (overflow > 0) InsertOverflowRows(root, sheetData, overflow);
        ClearCommentArea(root, sheetData, overflow);

        var month = data.Date.ToString("MMMM", CultureInfo.GetCultureInfo("ru-RU"));
        month = char.ToUpper(month[0], CultureInfo.GetCultureInfo("ru-RU")) + month[1..];
        SetValue(sheetData, "J4", month);
        SetValue(sheetData, "J5", ExcelDate(data.Date), "n");
        var header = data.Header;
        SetTextOrDash(sheetData, "D7", header?.Customer);
        SetTextOrDash(sheetData, "D8", header?.Project);
        SetTextOrDash(sheetData, "D9", header?.Site);
        SetTextOrDash(sheetData, "D10", header?.Complex);
        SetTextOrDash(sheetData, "D11", header?.Pipe);
        SetTextOrDash(sheetData, "B14", header?.FirstShiftTechnician1);
        SetTextOrDash(sheetData, "B15", header?.FirstShiftTechnician2);
        SetTextOrDash(sheetData, "F14", header?.SecondShiftTechnician1);
        SetTextOrDash(sheetData, "F15", header?.SecondShiftTechnician2);
        SetJoint(sheetData, "C17", data.FirstShift.FirstJoint);
        SetOptionalTime(sheetData, "C18", data.FirstShift.StartTime);
        SetJoint(sheetData, "C19", data.FirstShift.LastJoint);
        SetOptionalTime(sheetData, "C20", data.FirstShift.StopTime);
        SetOptionalCount(sheetData, "C21", Welded(data.FirstShift));
        SetOptionalCount(sheetData, "C23", data.FirstShift.Repairs);
        SetJoint(sheetData, "G17", data.SecondShift.FirstJoint);
        SetOptionalTime(sheetData, "G18", data.SecondShift.StartTime);
        SetJoint(sheetData, "G19", data.SecondShift.LastJoint);
        SetOptionalTime(sheetData, "G20", data.SecondShift.StopTime);
        SetOptionalCount(sheetData, "G21", Welded(data.SecondShift));
        SetOptionalCount(sheetData, "G23", data.SecondShift.Repairs);
        SetOptionalCount(sheetData, "K21", Combine(Welded(data.FirstShift), Welded(data.SecondShift)));
        SetOptionalCount(sheetData, "K23", Combine(data.FirstShift.Repairs, data.SecondShift.Repairs));
        var monthly = data.MonthlyTotals;
        SetOptionalCount(sheetData, "C22", monthly?.First.Welded);
        SetOptionalCount(sheetData, "G22", monthly?.Second.Welded);
        SetOptionalCount(sheetData, "K22", monthly?.Total.Welded);
        SetOptionalCount(sheetData, "C24", monthly?.First.Repairs);
        SetOptionalCount(sheetData, "G24", monthly?.Second.Repairs);
        SetOptionalCount(sheetData, "K24", monthly?.Total.Repairs);
        foreach (var cell in new[] { "C25", "G25", "K25" })
            ClearCachedFormulaValue(sheetData, cell);
        PopulateComments(root, sheetData, data.FirstShift, 1, 2, firstCommentCapacity);
        PopulateComments(root, sheetData, data.SecondShift, 5, 6, secondCommentCapacity);
        var reportEndRow = PopulateProblems(root, sheetData, data, overflow);
        EnsureCommentTextColumnsMerged(root, reportEndRow);
        CenterReportOnPrintedPage(root);
        SortAndValidateMerges(root);
    }

    private static int PopulateProblems(XElement root, XElement sheetData, ReportExportData data, int commentOverflow)
    {
        var items = (data.Problems ?? Array.Empty<string>()).Where(text => !string.IsNullOrWhiteSpace(text))
            .Select((text, index) => $"{index + 1}) {text.Trim()}")
            .Select(text => (Text: text, Rows: WrappedLineCount(text, CharactersPerProblemLine))).ToArray();
        var equipment = data.Equipment ?? Array.Empty<EquipmentData>();
        if (equipment.Any(item => string.IsNullOrWhiteSpace(item.Name) || item.InWork < 0 || item.InReserve < 0))
            throw new InvalidDataException("Проверьте названия и количество оборудования.");
        const int equipmentStart = 29;
        var headingRow = equipmentStart + equipment.Count;
        var start = headingRow + 1;
        var oldEnd = ReportBodyEndRow + commentOverflow;
        var requiredProblemRows = Math.Max(1, items.Sum(item => item.Rows));
        var end = Math.Max(oldEnd, start + requiredProblemRows - 1);
        // Capture the template styles before rebuilding only the right-hand columns.
        string?[] StylesAt(int number) => Enumerable.Range(9, 5).Select(column =>
            (string?)sheetData.Descendants(MainNs + "c").FirstOrDefault(cell =>
                (string?)cell.Attribute("r") == CellName(column, number))?.Attribute("s")).ToArray();
        var equipmentStyles = StylesAt(equipmentStart);
        var headingStyles = StylesAt(TemplateProblemHeadingRow);
        var firstProblemStyles = StylesAt(TemplateProblemTextRow + commentOverflow);
        var problemStyles = StylesAt(TemplateProblemTextRow + commentOverflow + 1);
        var bottomStyles = StylesAt(oldEnd);
        var extra = end - oldEnd;
        if (extra > 0)
        {
            var rowStyle = new XElement(sheetData.Elements(MainNs + "row").First(row => (int)row.Attribute("r")! == oldEnd - 1));
            // Insert before the common bottom border. This moves the existing bottom
            // of both comment columns and the problem column down by the same amount.
            InsertOverflowRows(root, sheetData, extra, oldEnd, rowStyle);
        }
        var merges = root.Element(MainNs + "mergeCells");
        if (merges is null) throw new InvalidDataException("В шаблоне отсутствуют объединения ячеек.");
        foreach (var merge in merges.Elements(MainNs + "mergeCell").ToArray())
        {
            var bounds = ParseMerge((string)merge.Attribute("ref")!);
            if (bounds.StartColumn >= 9 && bounds.EndColumn <= 13 && bounds.StartRow >= equipmentStart)
                merge.Remove();
        }
        for (var row = equipmentStart; row <= end; row++)
        {
            var styles = row < headingRow
                ? equipmentStyles
                : row == headingRow
                    ? headingStyles
                    : row == end
                        ? bottomStyles
                        : row == start
                            ? firstProblemStyles
                            : problemStyles;
            for (var column = 9; column <= 13; column++)
            {
                var address = CellName(column, row);
                SetValue(sheetData, address, null);
                sheetData.Descendants(MainNs + "c").First(cell => (string?)cell.Attribute("r") == address)
                    .SetAttributeValue("s", styles[column - 9]);
            }
        }
        for (var index = 0; index < equipment.Count; index++)
        {
            var row = equipmentStart + index;
            SetValue(sheetData, $"I{row}", equipment[index].Name.Trim());
            SetOptionalCount(sheetData, $"L{row}", equipment[index].InWork);
            SetOptionalCount(sheetData, $"M{row}", equipment[index].InReserve);
            AddMerge(merges, $"I{row}:K{row}");
        }
        // The problem heading follows the final equipment row without a spacer.
        SetValue(sheetData, $"I{headingRow}", "Проблемные вопросы:");
        AddMerge(merges, $"I{headingRow}:M{headingRow}");
        var current = start;
        foreach (var item in items)
        {
            SetValue(sheetData, $"I{current}", item.Text);
            AddMerge(merges, $"I{current}:M{current + item.Rows - 1}");
            current += item.Rows;
        }
        if (items.Length == 0) SetValue(sheetData, $"I{start}", "-");
        for (; current <= end; current++) AddMerge(merges, $"I{current}:M{current}");
        return end;
    }

    private static void CenterReportOnPrintedPage(XElement root)
    {
        var printOptions = root.Element(MainNs + "printOptions");
        if (printOptions is null)
        {
            printOptions = new XElement(MainNs + "printOptions");
            var pageMargins = root.Element(MainNs + "pageMargins");
            if (pageMargins is null) root.Add(printOptions);
            else pageMargins.AddBeforeSelf(printOptions);
        }

        printOptions.SetAttributeValue("horizontalCentered", "1");
    }

    private static int LayoutComments(ShiftReportData shift, int charactersPerLine)
    {
        var rows = 0;
        foreach (var comment in OrderedComments(shift))
        {
            var lineCount = WrappedLineCount(comment.Text, charactersPerLine);
            rows += Math.Max(1, lineCount);
        }
        return rows;
    }

    private static int CommentLineCapacity(XElement root, int textColumn)
    {
        var columnWidths = new Dictionary<int, double>();
        var defaultWidth = (double?)root.Element(MainNs + "sheetFormatPr")?.Attribute("defaultColWidth") ?? 8.43;
        foreach (var column in root.Element(MainNs + "cols")?.Elements(MainNs + "col") ?? Enumerable.Empty<XElement>())
        {
            var first = (int?)column.Attribute("min") ?? 1;
            var last = (int?)column.Attribute("max") ?? first;
            var width = (double?)column.Attribute("width") ?? defaultWidth;
            for (var index = first; index <= last; index++) columnWidths[index] = width;
        }

        var merges = root.Element(MainNs + "mergeCells")?.Elements(MainNs + "mergeCell") ?? Enumerable.Empty<XElement>();
        var merge = merges.Select(element => ParseMerge((string)element.Attribute("ref")!))
            .FirstOrDefault(bounds => bounds.StartColumn == textColumn && bounds.StartRow == FirstCommentRow);
        var endColumn = merge.EndColumn >= textColumn ? merge.EndColumn : textColumn;
        var widthInCharacters = 0d;
        for (var column = textColumn; column <= endColumn; column++)
            widthInCharacters += columnWidths.GetValueOrDefault(column, defaultWidth);

        // Excel column width is expressed in the width of the default font's "0".
        // Reserve a small amount for cell padding so wrapping follows the visible width.
        return Math.Max(1, (int)Math.Floor(widthInCharacters - 1));
    }

    private static void ClearCommentArea(XElement root, XElement sheetData, int overflow)
    {
        var lastCommentRow = ReportBodyEndRow + overflow;
        foreach (var row in sheetData.Elements(MainNs + "row").Where(e => (int)e.Attribute("r")! >= FirstCommentRow && (int)e.Attribute("r")! <= lastCommentRow))
        foreach (var cell in row.Elements(MainNs + "c").Where(c =>
                     Regex.IsMatch((string)c.Attribute("r")!, @"^[A-C]") || Regex.IsMatch((string)c.Attribute("r")!, @"^[E-G]")))
        {
            cell.Attribute("t")?.Remove();
            cell.Elements(MainNs + "v").Remove();
            cell.Elements(MainNs + "is").Remove();
            cell.Elements(MainNs + "f").Remove();
        }

        var merges = root.Element(MainNs + "mergeCells");
        if (merges is null) return;
        foreach (var merge in merges.Elements(MainNs + "mergeCell").ToList())
        {
            var reference = (string)merge.Attribute("ref")!;
            var first = Regex.Match(reference.Split(':')[0], @"^(?<col>[A-Z]+)(?<row>\d+)$");
            if (!first.Success) continue;
            var row = int.Parse(first.Groups["row"].Value, CultureInfo.InvariantCulture);
            var column = first.Groups["col"].Value;
            var rangeRows = Regex.Match(reference.Split(':').Last(), @"[A-Z]+(?<row>\d+)$");
            var endRow = rangeRows.Success ? int.Parse(rangeRows.Groups["row"].Value, CultureInfo.InvariantCulture) : row;
            var bounds = ParseMerge(reference);
            var inCommentColumns = (bounds.StartColumn <= 3 && bounds.EndColumn >= 1) ||
                                  (bounds.StartColumn <= 7 && bounds.EndColumn >= 5);
            if (inCommentColumns && row >= FirstCommentRow && row <= lastCommentRow && endRow >= FirstCommentRow &&
                (column is "A" or "B" or "C" or "E" or "F" or "G"))
                merge.Remove();
        }
        merges.SetAttributeValue("count", merges.Elements(MainNs + "mergeCell").Count());
    }

    private static void EnsureCommentTextColumnsMerged(XElement root, int lastRow)
    {
        var merges = root.Element(MainNs + "mergeCells");
        if (merges is null) throw new InvalidDataException("В шаблоне отсутствует список объединённых ячеек.");
        foreach (var (startColumn, endColumn) in new[] { (2, 3), (6, 7) })
        for (var row = FirstCommentRow; row <= lastRow; row++)
        {
            var covered = merges.Elements(MainNs + "mergeCell").Select(element => ParseMerge((string)element.Attribute("ref")!))
                .Any(bounds => bounds.StartColumn == startColumn && bounds.EndColumn == endColumn &&
                              bounds.StartRow <= row && bounds.EndRow >= row);
            if (!covered) AddMerge(merges, $"{CellName(startColumn, row)}:{CellName(endColumn, row)}");
        }
    }

    private static int WrappedLineCount(string text, int width)
    {
        var lines = 0;
        foreach (var paragraph in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (paragraph.Length == 0) { lines++; continue; }
            var current = 0;
            foreach (var sourceWord in Regex.Split(paragraph, @"\s+"))
            {
                var word = sourceWord;
                if (word.Length == 0) continue;
                if (current > 0 && current + 1 + word.Length <= width) current += 1 + word.Length;
                else
                {
                    if (current > 0) lines++;
                    while (word.Length >= width) { lines++; word = word[width..]; }
                    current = word.Length;
                }
            }
            if (current > 0) lines++;
        }
        return Math.Max(1, lines);
    }

    private static void PopulateComments(XElement root, XElement sheetData, ShiftReportData shift,
        int timeColumn, int textColumn, int charactersPerLine)
    {
        var mergeCells = root.Element(MainNs + "mergeCells");
        var row = FirstCommentRow;
        foreach (var comment in OrderedComments(shift))
        {
            var span = Math.Max(1, WrappedLineCount(comment.Text, charactersPerLine));
            var end = row + span - 1;
            var timeCell = CellName(timeColumn, row);
            var textCell = CellName(textColumn, row);
            if (comment.EndTime is null)
                SetValue(sheetData, timeCell, ExcelTime(comment.Time), "n");
            else
                SetValue(sheetData, timeCell, comment.TimeText, "inlineStr");
            SetValue(sheetData, textCell, comment.Text, "inlineStr");
            if (span > 1)
            {
                AddMerge(mergeCells, $"{timeCell}:{CellName(timeColumn, end)}");
                AddMerge(mergeCells, $"{textCell}:{CellName(textColumn + 1, end)}");
            }
            else AddMerge(mergeCells, $"{textCell}:{CellName(textColumn + 1, row)}");
            row = end + 1;
        }
    }

    private static IEnumerable<ReportComment> OrderedComments(ShiftReportData shift) => shift.Comments
        .OrderBy(comment => comment.Time);

    private static void InsertOverflowRows(XElement root, XElement sheetData, int count,
        int startRow = FirstCommentRow + ReservedCommentRows, XElement? rowStyle = null)
    {
        foreach (var row in sheetData.Elements(MainNs + "row").Where(e => (int)e.Attribute("r")! >= startRow).ToList())
        {
            var old = (int)row.Attribute("r")!;
            row.SetAttributeValue("r", old + count);
            foreach (var cell in row.Elements(MainNs + "c"))
                cell.SetAttributeValue("r", ShiftCellReference((string)cell.Attribute("r")!, count, startRow));
        }

        var mergeCells = root.Element(MainNs + "mergeCells");
        if (mergeCells is not null)
        {
            foreach (var merge in mergeCells.Elements(MainNs + "mergeCell"))
                merge.SetAttributeValue("ref", ShiftRange((string)merge.Attribute("ref")!, count, startRow));
        }

        foreach (var formula in root.Descendants(MainNs + "f"))
            formula.Value = Regex.Replace(formula.Value, @"(?<![A-Z0-9_])(?<col>\$?[A-Z]{1,3})(?<row>\$?\d+)", match =>
            {
                var rowText = match.Groups["row"].Value;
                var numeric = int.Parse(rowText.TrimStart('$'), CultureInfo.InvariantCulture);
                return numeric >= startRow
                    ? match.Groups["col"].Value + (rowText.StartsWith('$') ? "$" : string.Empty) + (numeric + count).ToString(CultureInfo.InvariantCulture)
                    : match.Value;
            });

        for (var row = startRow; row < startRow + count; row++)
        {
            var inserted = new XElement(MainNs + "row", new XAttribute("r", row), new XAttribute("ht", DefaultRowHeight), new XAttribute("customHeight", "1"));
            inserted.Add(new XElement(MainNs + "c", new XAttribute("r", $"A{row}"), new XAttribute("s", "23")));
            inserted.Add(new XElement(MainNs + "c", new XAttribute("r", $"B{row}"), new XAttribute("s", "56")));
            inserted.Add(new XElement(MainNs + "c", new XAttribute("r", $"C{row}"), new XAttribute("s", "66")));
            inserted.Add(new XElement(MainNs + "c", new XAttribute("r", $"E{row}"), new XAttribute("s", "26")));
            inserted.Add(new XElement(MainNs + "c", new XAttribute("r", $"F{row}"), new XAttribute("s", "56")));
            inserted.Add(new XElement(MainNs + "c", new XAttribute("r", $"G{row}"), new XAttribute("s", "57")));
            if (rowStyle is not null)
            {
                inserted = new XElement(rowStyle);
                inserted.SetAttributeValue("r", row);
                foreach (var cell in inserted.Elements(MainNs + "c"))
                {
                    cell.SetAttributeValue("r", Regex.Replace((string)cell.Attribute("r")!, @"\d+$", row.ToString(CultureInfo.InvariantCulture)));
                    cell.RemoveNodes();
                    cell.Attribute("t")?.Remove();
                }
            }
            var next = sheetData.Elements(MainNs + "row").FirstOrDefault(e => (int)e.Attribute("r")! > row);
            if (next is null) sheetData.Add(inserted); else next.AddBeforeSelf(inserted);
        }

        var dimension = root.Element(MainNs + "dimension");
        if (dimension is not null) dimension.SetAttributeValue("ref", ShiftRange((string)dimension.Attribute("ref")!, count, startRow));
        foreach (var view in root.Descendants(MainNs + "selection"))
        {
            if (view.Attribute("activeCell") is { } active) active.Value = ShiftCellReference(active.Value, count, startRow);
            if (view.Attribute("sqref") is { } range) range.Value = string.Join(' ', range.Value.Split(' ').Select(r => ShiftRange(r, count, startRow)));
        }
    }

    private static string ShiftRange(string range, int count, int startRow) => string.Join(':', range.Split(':').Select(part => ShiftCellReference(part, count, startRow)));

    private static string ShiftCellReference(string reference, int count, int startRow) => Regex.Replace(reference,
        @"(?<col>\$?[A-Z]{1,3})(?<row>\$?\d+)", match =>
        {
            var row = match.Groups["row"].Value;
            var numeric = int.Parse(row.TrimStart('$'), CultureInfo.InvariantCulture);
            return numeric >= startRow
                ? match.Groups["col"].Value + (row.StartsWith('$') ? "$" : string.Empty) + (numeric + count).ToString(CultureInfo.InvariantCulture)
                : match.Value;
        });

    private static void SetValue(XElement sheetData, string address, string? value, string type = "inlineStr")
    {
        var cell = sheetData.Descendants(MainNs + "c").FirstOrDefault(e => (string?)e.Attribute("r") == address);
        if (cell is null)
        {
            var rowNumber = int.Parse(Regex.Match(address, @"\d+").Value, CultureInfo.InvariantCulture);
            var row = sheetData.Elements(MainNs + "row").First(e => (int)e.Attribute("r")! == rowNumber);
            cell = new XElement(MainNs + "c", new XAttribute("r", address));
            var column = Regex.Match(address, @"[A-Z]+").Value;
            var nextCell = row.Elements(MainNs + "c").FirstOrDefault(e => string.CompareOrdinal(Regex.Match((string)e.Attribute("r")!, @"[A-Z]+").Value, column) > 0);
            if (nextCell is null) row.Add(cell); else nextCell.AddBeforeSelf(cell);
        }

        cell.RemoveNodes();
        if (value is null) { cell.Attribute("t")?.Remove(); return; }
        cell.SetAttributeValue("t", type);
        if (type == "inlineStr")
            cell.Add(new XElement(MainNs + "is", new XElement(MainNs + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
        else
            cell.Add(new XElement(MainNs + "v", value));
    }

    private static void ClearCachedFormulaValue(XElement sheetData, string address)
    {
        var cell = sheetData.Descendants(MainNs + "c").FirstOrDefault(e => (string?)e.Attribute("r") == address)
            ?? throw new InvalidDataException($"В шаблоне отсутствует ячейка формулы {address}.");
        if (cell.Element(MainNs + "f") is null)
            throw new InvalidDataException($"В шаблоне отсутствует формула расчёта процента в ячейке {address}.");
        cell.Elements(MainNs + "v").Remove();
    }

    private static void SetJoint(XElement sheetData, string address, string value)
    {
        var joint = int.TryParse(value, out var parsed) && parsed > 0 ? parsed.ToString(CultureInfo.InvariantCulture) : "-";
        SetValue(sheetData, address, joint, joint == "-" ? "inlineStr" : "n");
    }

    private static void SetOptionalTime(XElement sheetData, string address, TimeSpan? time) =>
        SetValue(sheetData, address, time is { } value ? ExcelTime(value) : "-", time is null ? "inlineStr" : "n");

    private static void SetOptionalCount(XElement sheetData, string address, int? value) =>
        SetValue(sheetData, address, value?.ToString(CultureInfo.InvariantCulture) ?? "-", value is null ? "inlineStr" : "n");

    private static void SetTextOrDash(XElement sheetData, string address, string? value) =>
        SetValue(sheetData, address, string.IsNullOrWhiteSpace(value) ? "-" : value.Trim());

    private static int? Combine(int? first, int? second) =>
        (first, second) switch
        {
            ({ } a, { } b) => checked(a + b),
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null
        };

    private static void AddMerge(XElement? mergeCells, string reference)
    {
        if (mergeCells is null) throw new InvalidDataException("В шаблоне отсутствует список объединённых ячеек.");
        mergeCells.Add(new XElement(MainNs + "mergeCell", new XAttribute("ref", reference)));
        mergeCells.SetAttributeValue("count", mergeCells.Elements(MainNs + "mergeCell").Count());
    }

    private static void SortAndValidateMerges(XElement root)
    {
        var merges = root.Element(MainNs + "mergeCells");
        if (merges is null) return;
        var ordered = merges.Elements(MainNs + "mergeCell")
            .Select(element => (Element: element, Bounds: ParseMerge((string)element.Attribute("ref")!)))
            .OrderBy(item => item.Bounds.StartRow)
            .ThenBy(item => item.Bounds.StartColumn)
            .ThenBy(item => item.Bounds.EndRow)
            .ThenBy(item => item.Bounds.EndColumn)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        for (var j = i + 1; j < ordered.Count; j++)
        {
            var a = ordered[i].Bounds;
            var b = ordered[j].Bounds;
            if (a.StartRow <= b.EndRow && a.EndRow >= b.StartRow && a.StartColumn <= b.EndColumn && a.EndColumn >= b.StartColumn)
                throw new InvalidDataException($"Диапазоны объединения ячеек пересекаются: {ordered[i].Element.Attribute("ref")!.Value} и {ordered[j].Element.Attribute("ref")!.Value}.");
        }
        foreach (var element in ordered.Select(item => item.Element).ToList()) element.Remove();
        merges.Add(ordered.Select(item => item.Element));
        merges.SetAttributeValue("count", ordered.Count);
    }

    private static MergeRange ParseMerge(string reference)
    {
        var parts = reference.Split(':');
        var first = Regex.Match(parts[0], @"^(?<col>[A-Z]+)(?<row>\d+)$");
        var last = Regex.Match(parts[^1], @"^(?<col>[A-Z]+)(?<row>\d+)$");
        if (!first.Success || !last.Success) throw new InvalidDataException($"Некорректный диапазон объединения: {reference}.");
        var range = new MergeRange(ColumnNumber(first.Groups["col"].Value), int.Parse(first.Groups["row"].Value, CultureInfo.InvariantCulture),
            ColumnNumber(last.Groups["col"].Value), int.Parse(last.Groups["row"].Value, CultureInfo.InvariantCulture));
        if (range.EndColumn < range.StartColumn || range.EndRow < range.StartRow)
            throw new InvalidDataException($"Перевёрнутый диапазон объединения: {reference}.");
        return range;
    }

    private static int ColumnNumber(string column)
    {
        var result = 0;
        foreach (var character in column) result = checked(result * 26 + character - 'A' + 1);
        return result;
    }

    private readonly record struct MergeRange(int StartColumn, int StartRow, int EndColumn, int EndRow);

    private static string CellName(int column, int row) => $"{(char)('A' + column - 1)}{row}";
    private static string ExcelDate(DateTime date) => (date.Date - new DateTime(1899, 12, 30)).TotalDays.ToString(CultureInfo.InvariantCulture);
    private static string? ExcelTime(TimeSpan? time) => time?.TotalDays.ToString(CultureInfo.InvariantCulture);
    private static int? Welded(ShiftReportData shift) =>
        int.TryParse(shift.FirstJoint, out var first) && int.TryParse(shift.LastJoint, out var last) && first > 0 && last >= first ? last - first + 1 : null;
}
