using System.Globalization;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Scolia.Application.Abstractions;

namespace Scolia.Infrastructure.Documents;

/// <summary>Documents PDF professionnels (QuestPDF). Charte SCOLIA : bleu marine, bleu action, gris bleuté.</summary>
public sealed class DocumentService : IDocumentService
{
    private const string Navy = "#13294B";
    private const string Blue = "#1D4ED8";
    private const string Soft = "#E8EEF7";
    private const string Muted = "#64748B";
    private const string Border = "#CBD5E1";
    private const string Font = "Roboto";
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly object InitLock = new();
    private static bool initialized;

    public DocumentService() => Initialize();

    public static void Initialize()
    {
        lock (InitLock)
        {
            if (initialized) return;
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.UseSystemFonts = false;
            var assembly = typeof(DocumentService).Assembly;
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
            }
            initialized = true;
        }
    }

    private static string M(decimal amount, string symbol) => amount.ToString("N0", Fr) + " " + symbol;

    private static void Header(IContainer container, SchoolHeader school, string title, string? subtitle)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                if (school.Logo is { Length: > 0 })
                    row.ConstantItem(54).Height(54).Image(school.Logo).FitArea();
                row.RelativeItem().PaddingLeft(school.Logo is { Length: > 0 } ? 10 : 0).Column(c =>
                {
                    c.Item().Text(school.Name.ToUpper(Fr)).FontSize(13).Bold().FontColor(Navy);
                    if (!string.IsNullOrWhiteSpace(school.Motto)) c.Item().Text(school.Motto).FontSize(8.5f).Italic().FontColor(Muted);
                    var contact = string.Join(" · ", new[] { school.Address, school.Phone, school.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (contact.Length > 0) c.Item().Text(contact).FontSize(8).FontColor(Muted);
                });
            });
            col.Item().PaddingTop(8).Height(3).Background(Blue);
            col.Item().PaddingTop(10).Text(title).FontSize(15).Bold().FontColor(Navy);
            if (!string.IsNullOrWhiteSpace(subtitle)) col.Item().Text(subtitle).FontSize(9).FontColor(Muted);
        });
    }

    private static void Footer(IContainer container, string? text)
    {
        container.BorderTop(0.5f).BorderColor(Border).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(text ?? $"Document généré par SCOLIA le {DateTime.Now.ToString("dd/MM/yyyy à HH:mm", Fr)}").FontSize(7.5f).FontColor(Muted);
            row.ConstantItem(80).AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }

    /// <summary>QR de vérification, en PNG : réutilisé par les cartes au format Word.</summary>
    public static byte[] QrPng(string text) => Qr(text);

    private static byte[] Qr(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(8, [19, 41, 75], [255, 255, 255]);
    }

    public byte[] Receipt(ReceiptDocument d)
    {
        var qr = Qr(d.VerificationUrl);
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(26);
            page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(9.5f).FontColor("#1E293B"));
            page.Header().Element(c => Header(c, d.School, "REÇU DE PAIEMENT", null));
            if (d.Cancelled)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-30).Text("ANNULÉ").FontSize(64).Bold().FontColor("#DC262655");
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Background(Soft).Padding(8).Column(c =>
                    {
                        c.Item().Text("N° du reçu").FontSize(7.5f).FontColor(Muted);
                        c.Item().Text(d.Number).FontSize(13).Bold().FontColor(Navy);
                    });
                    row.ConstantItem(8);
                    row.RelativeItem().Background(Soft).Padding(8).Column(c =>
                    {
                        c.Item().Text("Date").FontSize(7.5f).FontColor(Muted);
                        c.Item().Text(d.Date.ToString("dd MMMM yyyy", Fr)).FontSize(11).Bold().FontColor(Navy);
                    });
                });
                col.Item().Border(0.5f).BorderColor(Border).Padding(8).Column(c =>
                {
                    c.Item().Text("APPRENANT").FontSize(7.5f).Bold().FontColor(Blue);
                    c.Item().Text(d.StudentName).FontSize(11).Bold();
                    c.Item().Text($"Matricule {d.Matricule} · Promotion {d.Promotion} · Année {d.AcademicYear}").FontSize(8.5f).FontColor(Muted);
                    if (!string.IsNullOrWhiteSpace(d.PayerName)) c.Item().Text($"Versé par : {d.PayerName}").FontSize(8.5f).FontColor(Muted);
                });
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(1.4f); });
                    table.Header(h =>
                    {
                        h.Cell().Background(Navy).Padding(5).Text("Affectation").FontColor(Colors.White).Bold().FontSize(8.5f);
                        h.Cell().Background(Navy).Padding(5).AlignRight().Text("Montant").FontColor(Colors.White).Bold().FontSize(8.5f);
                    });
                    var i = 0;
                    foreach (var (label, amount) in d.Allocations)
                    {
                        var bg = i++ % 2 == 0 ? "#FFFFFF" : "#F7F9FD";
                        table.Cell().Background(bg).Padding(5).Text(label);
                        table.Cell().Background(bg).Padding(5).AlignRight().Text(M(amount, d.School.CurrencySymbol));
                    }
                    table.Cell().BorderTop(1).BorderColor(Navy).Padding(5).Text("TOTAL VERSÉ").Bold();
                    table.Cell().BorderTop(1).BorderColor(Navy).Padding(5).AlignRight().Text(M(d.Amount, d.School.CurrencySymbol)).Bold().FontSize(11).FontColor(Navy);
                });
                col.Item().Text(t =>
                {
                    t.Span("Arrêté le présent reçu à la somme de : ").FontSize(8.5f).FontColor(Muted);
                    t.Span(d.AmountInWords + ".").FontSize(8.5f).Italic();
                });
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text($"Mode de paiement : {d.Method}" + (string.IsNullOrWhiteSpace(d.Reference) ? string.Empty : $" · Réf. {d.Reference}")).FontSize(8.5f);
                        c.Item().Text($"Reste à payer pour l'année : {M(d.BalanceAfter, d.School.CurrencySymbol)}").FontSize(8.5f).Bold().FontColor(d.BalanceAfter > 0 ? "#B45309" : "#15803D");
                        c.Item().PaddingTop(14).Text($"Le caissier : {d.Cashier}").FontSize(8.5f);
                        c.Item().PaddingTop(18).Width(150).BorderBottom(0.5f).BorderColor(Muted);
                        c.Item().Text("Signature et cachet").FontSize(7).FontColor(Muted);
                        if (d.Cancelled) c.Item().PaddingTop(6).Text($"Reçu annulé : {d.CancellationReason}").FontSize(8).Bold().FontColor("#DC2626");
                    });
                    row.ConstantItem(92).Column(c =>
                    {
                        c.Item().Width(92).Height(92).Image(qr);
                        c.Item().AlignCenter().Text("Vérifier l'authenticité").FontSize(6.5f).FontColor(Muted);
                    });
                });
            });
            page.Footer().Element(c => Footer(c, d.School.ReceiptFooter));
        })).GeneratePdf();
    }

    public byte[] Statement(StatementDocument d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(32);
        page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(9).FontColor("#1E293B"));
        page.Header().Element(c => Header(c, d.School, "RELEVÉ DE COMPTE", $"Situation arrêtée au {d.AsOf.ToString("dd/MM/yyyy", Fr)}"));
        page.Content().PaddingTop(12).Column(col =>
        {
            col.Spacing(10);
            col.Item().Background(Soft).Padding(10).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(d.StudentName).FontSize(12).Bold().FontColor(Navy);
                    c.Item().Text($"Matricule {d.Matricule} · {d.Promotion} · Année scolaire {d.AcademicYear}").FontColor(Muted);
                });
                row.ConstantItem(170).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text("Reste à payer").FontSize(8).FontColor(Muted);
                    c.Item().AlignRight().Text(M(d.Balance, d.School.CurrencySymbol)).FontSize(15).Bold().FontColor(d.Balance > 0 ? "#B45309" : "#15803D");
                    if (d.Overdue > 0) c.Item().AlignRight().Text($"dont exigible : {M(d.Overdue, d.School.CurrencySymbol)}").FontSize(8).FontColor("#DC2626");
                });
            });
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(2.4f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); c.RelativeColumn(1.1f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); c.RelativeColumn(1.2f); });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Rubrique", "Échéance", "Montant", "Pris en charge", "Payé", "Reste", "Statut" })
                        h.Cell().Background(Navy).Padding(4).Text(title).FontColor(Colors.White).FontSize(8).Bold();
                });
                var i = 0;
                foreach (var l in d.Lines)
                {
                    var bg = i++ % 2 == 0 ? "#FFFFFF" : "#F7F9FD";
                    t.Cell().Background(bg).Padding(4).Text(l.Label);
                    t.Cell().Background(bg).Padding(4).Text(l.DueDate.ToString("dd/MM/yyyy", Fr));
                    t.Cell().Background(bg).Padding(4).AlignRight().Text(l.Amount.ToString("N0", Fr));
                    t.Cell().Background(bg).Padding(4).AlignRight().Text(l.Covered == 0 ? "—" : l.Covered.ToString("N0", Fr));
                    t.Cell().Background(bg).Padding(4).AlignRight().Text(l.Paid.ToString("N0", Fr));
                    t.Cell().Background(bg).Padding(4).AlignRight().Text(l.Remaining.ToString("N0", Fr)).FontColor(l.IsOverdue ? "#DC2626" : "#1E293B");
                    t.Cell().Background(bg).Padding(4).Text(l.Status).FontColor(l.IsOverdue ? "#DC2626" : Muted);
                }
                t.Cell().ColumnSpan(4).BorderTop(1).BorderColor(Navy).Padding(4).Text("TOTAL NET À PAYER : " + M(d.TotalNet, d.School.CurrencySymbol)).Bold();
                t.Cell().BorderTop(1).BorderColor(Navy).Padding(4).AlignRight().Text(d.TotalPaid.ToString("N0", Fr)).Bold();
                t.Cell().BorderTop(1).BorderColor(Navy).Padding(4).AlignRight().Text(d.Balance.ToString("N0", Fr)).Bold();
                t.Cell().BorderTop(1).BorderColor(Navy);
            });
            col.Item().PaddingTop(6).Text("Reçus enregistrés").Bold().FontColor(Navy);
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(1.4f); c.RelativeColumn(1); c.RelativeColumn(2.6f); c.RelativeColumn(1); c.RelativeColumn(1.1f); });
                t.Header(h =>
                {
                    foreach (var title in new[] { "N° reçu", "Date", "Affectation", "Mode", "Montant" })
                        h.Cell().BorderBottom(1).BorderColor(Navy).Padding(4).Text(title).FontSize(8).Bold().FontColor(Navy);
                });
                foreach (var r in d.Receipts)
                {
                    var cancelled = r.Status != "Valide";
                    t.Cell().Padding(4).Text(r.Number + (cancelled ? " (annulé)" : string.Empty)).FontColor(cancelled ? Muted : "#1E293B");
                    t.Cell().Padding(4).Text(r.Date.ToString("dd/MM/yyyy", Fr));
                    t.Cell().Padding(4).Text(r.Allocations ?? string.Empty).FontSize(8);
                    t.Cell().Padding(4).Text(r.Method);
                    t.Cell().Padding(4).AlignRight().Text(r.Amount.ToString("N0", Fr)).FontColor(cancelled ? Muted : "#1E293B");
                }
            });
        });
        page.Footer().Element(c => Footer(c, null));
    })).GeneratePdf();

    /// <summary>Cartes d'accès en PDF : mise en page dans <see cref="StudentCardPdf"/>.</summary>
    public byte[] StudentCards(CardDocument d) => StudentCardPdf.Build(d);

    /// <summary>Cartes d'accès en Word : même grille que le PDF, dans <see cref="CardWordWriter"/>.</summary>
    public byte[] StudentCardsWord(CardDocument d) => CardWordWriter.Write(d);

    public byte[] Table(TableDocument d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(d.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(28);
        page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(8.5f).FontColor("#1E293B"));
        page.Header().Element(c => Header(c, d.School, d.Title, d.Subtitle));
        var widths = ColumnWidths(d);
        var amounts = AmountColumns(d);
        page.Content().PaddingTop(10).Table(t =>
        {
            t.ColumnsDefinition(c => { foreach (var w in widths) c.RelativeColumn(w); });
            t.Header(h =>
            {
                for (var c = 0; c < d.Columns.Count; c++)
                    Align(h.Cell().Background(Navy).Padding(4), amounts[c]).Text(d.Columns[c]).FontColor(Colors.White).Bold().FontSize(8);
            });
            var i = 0;
            foreach (var row in d.Rows)
            {
                var bg = i++ % 2 == 0 ? "#FFFFFF" : "#F7F9FD";
                for (var c = 0; c < row.Count; c++) Align(t.Cell().Background(bg).Padding(4), c < amounts.Length && amounts[c]).Text(row[c]);
            }
            if (d.Totals is not null)
                for (var c = 0; c < d.Totals.Count; c++)
                    Align(t.Cell().BorderTop(1).BorderColor(Navy).Padding(4), c < amounts.Length && amounts[c]).Text(d.Totals[c]).Bold();
        });
        page.Footer().Element(c => Footer(c, null));
    })).GeneratePdf();

    /// <summary>Largeur de chaque colonne selon son contenu : un nom complet a plus de place qu'un code de promotion.</summary>
    private static float[] ColumnWidths(TableDocument d) => d.Columns.Select((title, c) =>
    {
        var longest = d.Rows.Take(300).Select(r => c < r.Count ? r[c].Length : 0).DefaultIfEmpty(0).Max();
        return Math.Clamp(Math.Max(title.Length * 0.8f, longest), 3f, 30f);
    }).ToArray();

    /// <summary>Colonnes de montants, alignées à droite pour que les chiffres se lisent en colonne.</summary>
    private static bool[] AmountColumns(TableDocument d) => d.Columns.Select((_, c) =>
    {
        var values = d.Rows.Select(r => c < r.Count ? r[c] : string.Empty).Where(v => v.Length > 0).ToList();
        return values.Count > 0 && values.All(v => v.EndsWith(" FC", StringComparison.Ordinal) || v.EndsWith(" $", StringComparison.Ordinal));
    }).ToArray();

    private static IContainer Align(IContainer cell, bool right) => right ? cell.PaddingRight(8).AlignRight() : cell;

    public byte[] ReportCard(ReportCardDocument d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(36);
        page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(9.5f).FontColor("#1E293B"));
        page.Header().Element(c => Header(c, d.School, "BULLETIN DE NOTES", $"Année scolaire {d.AcademicYear}"));
        page.Content().PaddingTop(12).Column(col =>
        {
            col.Spacing(10);
            col.Item().Background(Soft).Padding(10).Column(c =>
            {
                c.Item().Text(d.StudentName).FontSize(12).Bold().FontColor(Navy);
                c.Item().Text($"Matricule {d.Matricule} · {d.Promotion}").FontColor(Muted);
            });
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(1.2f); c.RelativeColumn(1.4f); c.RelativeColumn(1.6f); });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Cours", "Coefficient", "Moyenne / 20", "Points" })
                        h.Cell().Background(Navy).Padding(5).Text(title).FontColor(Colors.White).Bold().FontSize(8.5f);
                });
                var i = 0;
                foreach (var (course, coefficient, average, _) in d.Lines)
                {
                    var bg = i++ % 2 == 0 ? "#FFFFFF" : "#F7F9FD";
                    t.Cell().Background(bg).Padding(5).Text(course);
                    t.Cell().Background(bg).Padding(5).AlignCenter().Text(coefficient.ToString("0.##", Fr));
                    t.Cell().Background(bg).Padding(5).AlignCenter().Text(average?.ToString("0.00", Fr) ?? "—").FontColor(average < 10 ? "#DC2626" : "#1E293B");
                    t.Cell().Background(bg).Padding(5).AlignCenter().Text(average is null ? "—" : (average.Value * coefficient).ToString("0.00", Fr));
                }
            });
            col.Item().Row(row =>
            {
                Box(row.RelativeItem(), "Moyenne générale", d.Average?.ToString("0.00", Fr) + " / 20");
                row.ConstantItem(8);
                Box(row.RelativeItem(), "Rang", d.Rank is null ? "—" : $"{d.Rank} / {d.ClassSize}");
                row.ConstantItem(8);
                Box(row.RelativeItem(), "Décision", d.Decision);
            });
            col.Item().PaddingTop(30).Row(row =>
            {
                row.RelativeItem().Column(c => { c.Item().Text("Le titulaire").FontColor(Muted); c.Item().PaddingTop(30).Width(140).BorderBottom(0.5f); });
                row.RelativeItem().AlignRight().Column(c => { c.Item().Text("La direction").FontColor(Muted); c.Item().PaddingTop(30).Width(140).BorderBottom(0.5f); });
            });
        });
        page.Footer().Element(c => Footer(c, null));
    })).GeneratePdf();

    private static void Box(IContainer container, string label, string? value) =>
        container.Border(0.5f).BorderColor(Border).Padding(8).Column(c =>
        {
            c.Item().Text(label).FontSize(8).FontColor(Muted);
            c.Item().Text(value ?? "—").FontSize(13).Bold().FontColor(Navy);
        });
}
