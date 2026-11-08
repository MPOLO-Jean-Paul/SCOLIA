using System.Globalization;
using ClosedXML.Excel;
using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>
/// Une seule grille Excel créée à la demande à partir des promotions, cours, maxima,
/// inscriptions, codes et cotes du serveur scolaire ; aucun onglet n'est prédéfini.
/// Le format reprend les grandes rubriques, les en-têtes verticaux et les maxima
/// du modèle de délibération fourni par l'établissement.
/// </summary>
internal static class DeliberationWorkbook
{
    private const int HeaderRow = 7;
    private const int MaximaRow = 8;
    private const int FirstStudentRow = 9;

    public static byte[] Build(IReadOnlyList<(string SheetName, TableDocument Document)> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        if (grids.Count == 0) throw new ArgumentException("Aucune promotion à exporter.", nameof(grids));
        using var workbook = new XLWorkbook();
        foreach (var (requestedName, document) in grids)
        {
            var original = SpreadsheetService.SheetName(requestedName);
            var unique = original;
            var suffix = 2;
            while (workbook.Worksheets.Any(s => string.Equals(s.Name, unique, StringComparison.OrdinalIgnoreCase)))
            {
                var marker = "-" + suffix.ToString(CultureInfo.InvariantCulture);
                unique = original[..Math.Min(original.Length, 31 - marker.Length)] + marker;
                suffix++;
            }

            var sheet = workbook.Worksheets.Add(unique);
            WriteSheet(sheet, document, requestedName);
        }
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSheet(IXLWorksheet sheet, TableDocument document, string promotionCode)
    {
        if (document.Columns.Count < 3)
            throw new ArgumentException("Grille de délibération sans colonnes d'identification.");

        var headers = document.Columns;
        var jury1 = FindColumn(headers, "T1 /");
        var courseSubtotal = FindColumn(headers, "ST1 /");
        var order = Enumerable.Range(0, headers.Count).ToList();

        // Comme dans le modèle : cours de la promotion puis épreuves du jury et blocs.
        // Les cellules de chaque apprenant suivent exactement la même permutation.
        if (jury1 > 3 && courseSubtotal > jury1)
        {
            order = [0, 1, 2];
            order.AddRange(Enumerable.Range(jury1 + 1, courseSubtotal - jury1));
            order.AddRange(Enumerable.Range(3, jury1 - 2));
            order.AddRange(Enumerable.Range(courseSubtotal + 1, headers.Count - courseSubtotal - 1));
        }

        var names = order.Select(i => headers[i]).ToList();
        var count = names.Count;
        var lastStudent = FirstStudentRow + document.Rows.Count - 1;
        var summaryRow = Math.Max(lastStudent + 2, FirstStudentRow + 1);
        var footerRow = summaryRow + 2;

        sheet.Style.Font.FontName = "Aptos";
        sheet.Style.Font.FontSize = 9;
        sheet.Column(1).Width = 7;
        sheet.Column(2).Width = 17;
        sheet.Column(3).Width = 35;
        for (var col = 4; col <= count; col++) sheet.Column(col).Width = 12;
        sheet.Row(1).Height = 31;
        sheet.Row(2).Height = 26;
        sheet.Row(3).Height = 25;
        sheet.Row(4).Height = 22;
        sheet.Row(5).Height = 22;
        sheet.Row(6).Height = 28;
        sheet.Row(HeaderRow).Height = 108;
        sheet.Row(MaximaRow).Height = 25;

        Band(sheet, 1, 1, count, document.School.Name, "#152E4A");
        sheet.Cell(1, 1).Style.Font.FontSize = 17;
        Band(sheet, 2, 1, count, "GRILLE DE DÉLIBÉRATION — " + promotionCode, "#254766");
        sheet.Cell(2, 1).Style.Font.FontSize = 13;
        sheet.Range(3, 1, 3, count).Merge();
        sheet.Cell(3, 1).Value = ShortContext(document.Subtitle);
        sheet.Cell(3, 1).Style.Font.Bold = true;
        sheet.Cell(3, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        sheet.Range(4, 1, 4, count).Merge();
        sheet.Cell(4, 1).Value = "Année, promotion, cours et maxima issus de la configuration SCOLIA de l'établissement";
        sheet.Cell(4, 1).Style.Font.FontColor = XLColor.FromHtml("#385576");
        sheet.Range(5, 1, 5, count).Merge();
        sheet.Cell(5, 1).Value = "Document du jury — les codes et cotes proviennent de la base. Après modification des cotes dans SCOLIA, réexporter la grille.";
        sheet.Cell(5, 1).Style.Font.FontColor = XLColor.FromHtml("#66788B");

        var st1 = FindColumn(names, "ST1 /");
        var t1 = FindColumn(names, "T1 /");
        var st2 = FindColumn(names, "ST2 /");
        Band(sheet, 6, 1, 3, "IDENTIFICATION / JURY", "#254766");
        if (st1 >= 3 && t1 > st1 && st2 > t1)
        {
            Band(sheet, 6, 4, st1 + 1, "COURS ET SAVOIRS ISOLÉS", "#386D9A");
            Band(sheet, 6, st1 + 2, t1 + 1, "ÉPREUVES DU JURY", "#466A57");
            Band(sheet, 6, t1 + 2, st2 + 1, "BLOCS DE COMPÉTENCES", "#387D73");
            Band(sheet, 6, st2 + 2, count, "TOTAL ET DÉCISION", "#6B5C86");
        }
        else Band(sheet, 6, 4, count, "COURS, COTES ET DÉCISIONS", "#386D9A");

        sheet.Cell(HeaderRow, 1).Value = "N°";
        sheet.Cell(HeaderRow, 2).Value = "CODE";
        sheet.Cell(HeaderRow, 3).Value = "NOMS & POSTNOMS";
        for (var index = 3; index < count; index++)
        {
            var (label, maximum) = SplitHeader(names[index]);
            var col = index + 1;
            sheet.Cell(HeaderRow, col).Value = label;
            if (maximum is { } max) sheet.Cell(MaximaRow, col).Value = (double)max;
            if (names[index].StartsWith("T1 /", StringComparison.Ordinal) ||
                names[index].StartsWith("ST1 /", StringComparison.Ordinal) ||
                names[index].StartsWith("ST2 /", StringComparison.Ordinal) ||
                names[index].StartsWith("T2 /", StringComparison.Ordinal) ||
                names[index].StartsWith("TG /", StringComparison.Ordinal))
            {
                sheet.Range(HeaderRow, col, MaximaRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EAF4");
                sheet.Range(HeaderRow, col, MaximaRow, col).Style.Font.Bold = true;
            }
        }
        sheet.Range(HeaderRow, 1, MaximaRow, 1).Merge();
        sheet.Range(HeaderRow, 2, MaximaRow, 2).Merge();
        sheet.Range(HeaderRow, 3, MaximaRow, 3).Merge();

        var headings = sheet.Range(HeaderRow, 1, MaximaRow, count);
        headings.Style.Font.Bold = true;
        headings.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headings.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headings.Style.Alignment.WrapText = true;
        sheet.Range(HeaderRow, 1, MaximaRow, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#E2EAF4");
        sheet.Range(MaximaRow, 4, MaximaRow, count).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CB");
        sheet.Range(MaximaRow, 4, MaximaRow, count).Style.NumberFormat.Format = "0.##";

        for (var i = 0; i < document.Rows.Count; i++)
        {
            var source = document.Rows[i];
            var row = FirstStudentRow + i;
            sheet.Row(row).Height = 21;
            if (i % 2 == 1) sheet.Range(row, 1, row, count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F7FA");
            for (var j = 0; j < count; j++)
            {
                var raw = order[j] < source.Count ? source[order[j]] : string.Empty;
                var cell = sheet.Cell(row, j + 1);
                if (j == 1)
                {
                    cell.Style.NumberFormat.Format = "@";
                    cell.Value = raw;
                    cell.Style.Font.Bold = true;
                }
                else if (j != 2 && decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var decimalValue))
                {
                    cell.Value = (double)decimalValue;
                    cell.Style.NumberFormat.Format = "0.##";
                }
                else if (j != 2 && decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue))
                {
                    cell.Value = (double)decimalValue;
                    cell.Style.NumberFormat.Format = "0.##";
                }
                else cell.Value = raw;
            }
        }

        var bodyEnd = Math.Max(MaximaRow, lastStudent);
        var bordered = sheet.Range(HeaderRow, 1, bodyEnd, count);
        bordered.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        bordered.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        bordered.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        if (document.Rows.Count > 0)
            sheet.Range(FirstStudentRow, 1, bodyEnd, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        if (document.Rows.Count == 0)
        {
            sheet.Range(FirstStudentRow, 1, FirstStudentRow, count).Merge();
            sheet.Cell(FirstStudentRow, 1).Value = "Aucun apprenant inscrit dans cette promotion pour l'année courante.";
            sheet.Cell(FirstStudentRow, 1).Style.Font.Italic = true;
        }
        else
        {
            sheet.Cell(summaryRow, 3).Value = "MOYENNE PAR COLONNE";
            sheet.Cell(summaryRow, 3).Style.Font.Bold = true;
            // Moyennes Excel recalculables ; les totaux et décisions individuels officiels
            // restent calculés par le moteur SCOLIA, jamais par une règle inventée.
            for (var col = 4; col <= count; col++)
            {
                var name = names[col - 1];
                if (name is "Décision" or "Code jury") continue;
                var letter = XLHelper.GetColumnLetterFromNumber(col);
                sheet.Cell(summaryRow, col).FormulaA1 =
                    $"IF(COUNT({letter}{FirstStudentRow}:{letter}{lastStudent})=0,\"\",AVERAGE({letter}{FirstStudentRow}:{letter}{lastStudent}))";
                sheet.Cell(summaryRow, col).Style.NumberFormat.Format = "0.##";
            }
            sheet.Range(summaryRow, 1, summaryRow, count).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9F2FB");
        }

        sheet.Range(footerRow, 1, footerRow, count).Merge();
        sheet.Cell(footerRow, 1).Value = "Grille générée depuis la base scolaire SCOLIA : les décisions officielles sont celles de l'application et du jury.";
        sheet.Cell(footerRow, 1).Style.Font.Italic = true;
        sheet.Cell(footerRow, 1).Style.Font.FontColor = XLColor.FromHtml("#60738A");

        sheet.SheetView.FreezeRows(MaximaRow);
        sheet.SheetView.FreezeColumns(3);
        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A3;
        sheet.PageSetup.PagesWide = 1;
        sheet.PageSetup.PagesTall = 0;
        sheet.PageSetup.SetRowsToRepeatAtTop(1, MaximaRow);
    }

    private static void Band(IXLWorksheet sheet, int row, int start, int end, string label, string fill)
    {
        if (start > end) return;
        if (end > start) sheet.Range(row, start, row, end).Merge();
        var range = sheet.Range(row, start, row, end);
        range.Style.Fill.BackgroundColor = XLColor.FromHtml(fill);
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        sheet.Cell(row, start).Value = label;
    }

    private static int FindColumn(IReadOnlyList<string> columns, string prefix)
    {
        for (var i = 0; i < columns.Count; i++)
            if (columns[i].StartsWith(prefix, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static string ShortContext(string? context) =>
        string.Join(" · ", (context ?? string.Empty).Split(" · ", StringSplitOptions.RemoveEmptyEntries).Take(3));

    private static (string Name, decimal? Maximum) SplitHeader(string value)
    {
        var start = value.LastIndexOf(" (/", StringComparison.Ordinal);
        if (start > 0 && value.EndsWith(')'))
        {
            var raw = value[(start + 3)..^1];
            if (decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var max))
                return (value[..start], max);
        }
        start = value.LastIndexOf(" /", StringComparison.Ordinal);
        if (start > 0 && decimal.TryParse(value[(start + 2)..].Replace(',', '.'), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var totalMax))
            return (value[..start], totalMax);
        return (value, null);
    }
}
