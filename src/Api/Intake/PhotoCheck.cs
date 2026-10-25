namespace MenuToOrder.Api.Intake;

public static class PhotoCheck
{
    public const long MaxBytes = 10 * 1024 * 1024;

    // Judged by the file's first bytes; the browser-supplied content type can say anything.
    public static string? MediaType(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}
