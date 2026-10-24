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

    /// <summary>
    /// Cartes d'accès aux examens : A4 portrait, huit cartes de 92 × 63 mm par page, deux colonnes et quatre rangées,
    /// espacements de 6 mm et 7 mm, marges de 10 mm. Chaque carte porte une bande de promotion et un QR à droite.
    /// </summary>
    public byte[] StudentCardsWord(CardDocument d) => CardWordWriter.Write(d);

    public byte[] StudentCards(CardDocument d)
    {
        var pages = new List<IReadOnlyList<CardItem>>();
        for (var start = 0; start < d.Cards.Count; start += Scolia.Domain.Students.StudentCardRules.CardsPerPage)
            pages.Add(d.Cards.Skip(start).Take(Scolia.Domain.Students.StudentCardRules.CardsPerPage).ToList());
        return Document.Create(doc =>
        {
            foreach (var pageCards in pages)
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(Scolia.Domain.Students.StudentCardRules.MarginMm, Unit.Millimetre);
                    page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(7).FontColor("#1E293B"));
                    page.Content().Column(column =>
                    {
                        for (var row = 0; row < 4; row++)
                        {
                            if (row > 0) column.Item().Height(Scolia.Domain.Students.StudentCardRules.RowGapMm, Unit.Millimetre);
                            column.Item().Row(line =>
                            {
                                for (var col = 0; col < 2; col++)
                                {
                                    if (col > 0) line.ConstantItem(Scolia.Domain.Students.StudentCardRules.ColumnGapMm, Unit.Millimetre);
                                    var index = row * 2 + col;
                                    // La seconde colonne prend le reste de la ligne : l'arrondi des points PDF ne fait jamais déborder la page.
                                    var width = col == 0
                                        ? line.ConstantItem(Scolia.Domain.Students.StudentCardRules.CardWidthMm, Unit.Millimetre)
                                        : line.RelativeItem();
                                    var cell = width.Height(Scolia.Domain.Students.StudentCardRules.CardHeightMm, Unit.Millimetre);
                                    if (index < pageCards.Count) cell.Element(c => StudentCard(c, d, pageCards[index]));
                                }
                            });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    private static void StudentCard(IContainer container, CardDocument d, CardItem card)
    {
        container.Border(0.4f).BorderColor("#94A3B8").Column(column =>
        {
            column.Item().Height(7, Unit.Millimetre).Background(card.Color);
            column.Item().Padding(4, Unit.Millimetre).Column(body =>
            {
                var header = new[] { d.Header.Country, d.Header.Ministry, d.Header.Direction }.Where(x => !string.IsNullOrWhiteSpace(x));
                body.Item().Text(string.Join(" · ", header)).FontSize(5.5f).AlignCenter();
                var place = new[] { d.Header.Division, d.Header.CityZone, d.Header.Institute }.Where(x => !string.IsNullOrWhiteSpace(x));
                body.Item().Text(string.Join(" · ", place)).FontSize(5.5f).AlignCenter();
                body.Item().PaddingTop(1.5f).Text("CARTE D'ACCÈS").FontSize(9.3f).Bold().Underline().AlignCenter();
                body.Item().Text($"SESSION {d.Session}").FontSize(14).Bold().AlignCenter();
                body.Item().PaddingTop(5).Row(line =>
                {
                    line.RelativeItem().DefaultTextStyle(x => x.FontSize(8.5f)).Column(identity =>
                    {
                        identity.Spacing(2.5f);
                        identity.Item().Text(t => { t.Span("NOM : ").Bold(); t.Span(card.LastName); });
                        identity.Item().Text(t => { t.Span("POST-NOM : ").Bold(); t.Span(card.MiddleName); });
                        identity.Item().Text(t => { t.Span("PRÉNOM : ").Bold(); t.Span(card.FirstName); });
                        identity.Item().Text(t => { t.Span("CLASSE : ").Bold(); t.Span(card.Classe); });
                    });
                    line.ConstantItem(25, Unit.Millimetre).Image(Qr(card.QrText));
                });
            });
        });
    }

    public byte[] Table(TableDocument d) => Document.Create(doc => doc.Page(page =>
    {
        page.Size(d.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(28);
        page.DefaultTextStyle(s => s.FontFamily(Font).FontSize(8.5f).FontColor("#1E293B"));
        page.Header().Element(c => Header(c, d.School, d.Title, d.Subtitle));
        page.Content().PaddingTop(10).Table(t =>
        {
            t.ColumnsDefinition(c => { foreach (var _ in d.Columns) c.RelativeColumn(); });
            t.Header(h =>
            {
                foreach (var title in d.Columns) h.Cell().Background(Navy).Padding(4).Text(title).FontColor(Colors.White).Bold().FontSize(8);
            });
            var i = 0;
            foreach (var row in d.Rows)
            {
                var bg = i++ % 2 == 0 ? "#FFFFFF" : "#F7F9FD";
                foreach (var cell in row) t.Cell().Background(bg).Padding(4).Text(cell);
            }
            if (d.Totals is not null)
                foreach (var cell in d.Totals) t.Cell().BorderTop(1).BorderColor(Navy).Padding(4).Text(cell).Bold();
        });
        page.Footer().Element(c => Footer(c, null));
    })).GeneratePdf();

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
