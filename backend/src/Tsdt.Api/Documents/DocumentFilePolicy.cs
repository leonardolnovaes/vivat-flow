using System.Text;

namespace Tsdt.Api.Documents;

public sealed class DocumentValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

public sealed class DocumentNotFoundException : Exception { }

public static class DocumentFilePolicy
{
    public static string SanitizeFileName(string? value)
    {
        if (value is null || value.Length > 512) throw new DocumentValidationException("file", "Informe um nome de arquivo válido com até 180 caracteres.");
        var leaf = (value ?? "").Replace('\\', '/').Split('/').Last().Normalize(NormalizationForm.FormKC).Trim();
        var name = new string(leaf.Where(character => char.IsLetterOrDigit(character) || character is ' ' or '.' or '_' or '-' or '(' or ')').ToArray()).Trim(' ', '.');
        if (name.Length is 0 or > 180) throw new DocumentValidationException("file", "Informe um nome de arquivo válido com até 180 caracteres.");
        return name;
    }

    public static string ValidatedContentType(string fileName, string? requestedType, ReadOnlySpan<byte> signature)
    {
        var type = requestedType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var valid = (type, extension) switch
        {
            ("application/pdf", ".pdf") => signature.StartsWith("%PDF-"u8),
            ("image/png", ".png") => signature.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ("image/jpeg", ".jpg" or ".jpeg") => signature.StartsWith(new byte[] { 0xff, 0xd8, 0xff }),
            ("image/webp", ".webp") => signature.Length >= 12 && signature[..4].SequenceEqual("RIFF"u8) && signature.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!valid) throw new DocumentValidationException("file", "Envie um PDF ou imagem PNG, JPEG ou WebP válido.");
        return type!;
    }
}
