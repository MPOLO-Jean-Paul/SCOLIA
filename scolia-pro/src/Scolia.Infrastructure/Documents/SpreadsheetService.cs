using ClosedXML.Excel;
using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>Exports Excel (.xlsx) mis en forme : en-tête de l'établissement, tableau filtrable, colonnes ajustées.</summary>
public sealed class SpreadsheetService : ISpreadsheetService
{
    public byte[] Export(TableDocument document)
    {
        using var workbook = new XLWorkbook();
        var name = new string(document.Title.Where(c => char.IsLetterOrDigit(c) || c == ' ').ToArray()).Trim();
        var sheet = workbook.Worksheets.Add(name.Length == 0 ? "Export" : name[..Math.Min(31, name.Length)]);
        sheet.Cell(1, 1).Value = document.School.Name;
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(XLColor.FromHtml("#13294B"));
        sheet.Cell(2, 1).Value = document.Title;
        sheet.Cell(2, 1).Style.Font.SetBold().Font.SetFontSize(12);
        if (!string.IsNullOrWhiteSpace(document.Subtitle))
        {
            sheet.Cell(3, 1).Value = document.Subtitle;
            sheet.Cell(3, 1).Style.Font.SetFontColor(XLColor.FromHtml("#64748B"));
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
