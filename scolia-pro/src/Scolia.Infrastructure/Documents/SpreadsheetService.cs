using ClosedXML.Excel;
using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>Exports Excel (.xlsx) mis en forme : en-tête de l'établissement, tableau filtrable, colonnes ajustées.</summary>
public sealed class SpreadsheetService : ISpreadsheetService
{
    public byte[] Export(TableDocument document)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(document.Title));
        // En-tête : le logo de l'établissement à gauche, puis son nom, le titre et le sous-titre.
        var logo = document.School.Logo is { Length: > 0 } own ? own : StudentCardPdf.DefaultSeal();
        var text = 1;
        try
        {
            using var image = new MemoryStream(logo);
            sheet.AddPicture(image).MoveTo(sheet.Cell(1, 1), 4, 4).WithSize(52, 52);
            text = 2;
            sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 9);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { }
        for (var row = 1; row <= 3; row++) sheet.Row(row).Height = 20;
        sheet.Cell(1, text).Value = document.School.Name;
        sheet.Cell(1, text).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.FromHtml("#13294B"));
        sheet.Cell(2, text).Value = document.Title;
        sheet.Cell(2, text).Style.Font.SetBold().Font.SetFontSize(12);
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            sheet.Cell(3, text).Value = document.Subtitle;
            sheet.Cell(3, text).Style.Font.SetFontColor(XLColor.FromHtml("#64748B"));
        }
        const int headerRow = 5;
        for (var c = 0; c < document.Columns.Count; c++) sheet.Cell(headerRow, c + 1).Value = document.Columns[c];
        var r = headerRow + 1;
        foreach (var row in document.Rows)
        {
            for (var c = 0; c < row.Count; c++) SetValue(sheet.Cell(r, c + 1), row[c]);
            r++;
        }
        if (document.Rows.Count > 0)
        {
            var table = sheet.Range(headerRow, 1, r - 1, document.Columns.Count).CreateTable();
            table.Theme = XLTableTheme.TableStyleMedium2;
        }
        else
        {
            var header = sheet.Range(headerRow, 1, headerRow, document.Columns.Count);
            header.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#13294B")).Font.SetFontColor(XLColor.White);
        }
        if (document.Totals is not null)
        {
            for (var c = 0; c < document.Totals.Count; c++) SetValue(sheet.Cell(r, c + 1), document.Totals[c]);
            sheet.Row(r).Style.Font.SetBold();
        }
        sheet.Columns().AdjustToContents(headerRow, r, 8, 60);
        sheet.SheetView.FreezeRows(headerRow);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Nom d'onglet lisible dans la limite Excel de 31 caractères : « Suivi des paiements · Octobre 2026 » donne
    /// « Octobre 2026 », « Liste d'enregistrement — ACC1 » donne « ACC1 ». La coupe se fait entre deux mots.
    /// </summary>
    internal static string SheetName(string title)
    {
        var parts = title.Split([" · ", " — ", " : "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts.Length > 1 && title.Length > 31 ? parts[^1] : title;
        name = new string(name.Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim().Trim('\'');
        if (name.Length > 31)
        {
            var cut = name.LastIndexOf(' ', 31);
            name = name[..(cut > 10 ? cut : 31)].TrimEnd();
        }
        return name.Length == 0 ? "Export" : name;
    }

    /// <summary>Les montants « 55 000 FC » restent numériques dans Excel pour permettre les calculs.</summary>
    private static void SetValue(IXLCell cell, string text)
    {
        var digits = text.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        var symbolIndex = digits.IndexOfAny(['F', '$', '€']);
        var numeric = symbolIndex > 0 ? digits[..symbolIndex] : digits;
        if (numeric.Length > 0 && numeric.Length < 16 && numeric.All(ch => char.IsDigit(ch) || ch is ',' or '-') && numeric.Any(char.IsDigit) && text.Any(char.IsWhiteSpace) && symbolIndex > 0
            && decimal.TryParse(numeric.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount))
        {
            cell.Value = amount;
            cell.Style.NumberFormat.Format = "#,##0";
            return;
        }
        cell.Value = text;
    }
}
