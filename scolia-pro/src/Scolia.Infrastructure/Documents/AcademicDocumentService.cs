using Scolia.Application.Abstractions;
using Scolia.Application.Dtos;

namespace Scolia.Infrastructure.Documents;

/// <summary>Documents EPST et LMD de l'ordinateur serveur (QuestPDF).</summary>
public sealed class AcademicDocumentService : IAcademicDocumentService
{
    public byte[] EpstBulletins(EpstBulletinPrintDto document) => EpstBulletinPdf.Build(document);
    public byte[] EpstPalmares(EpstPalmaresPrintDto document) => EpstPalmaresPdf.Build(document);
    public byte[] LmdTranscripts(LmdTranscriptPrintDto document) => LmdTranscriptPdf.Build(document);
    public byte[] LmdMinutes(LmdTranscriptPrintDto document) => LmdMinutesPdf.Build(document);
}
