namespace Scolia.Application.Services;

/// <summary>
/// Type réel d'une image, lu dans ses premiers octets : le type annoncé par l'envoi peut manquer ou être faux.
/// </summary>
public static class ImageKind
{
    public static string? Detect(ReadOnlySpan<byte> data) => data switch
    {
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        [0x47, 0x49, 0x46, 0x38, ..] => "image/gif",
        _ => null,
    };
}
