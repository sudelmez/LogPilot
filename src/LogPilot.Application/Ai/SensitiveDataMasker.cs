using System.Text.RegularExpressions;

namespace LogPilot.Application.Ai;

/// Token, parola, e-posta, telefon, kart, IBAN, plaka, kimlik no (uzun sayı) gibi değerleri LLM'e VE kullanıcıya gitmeden önce maskeler.
/// Kimlikleri (COR-..., TRX-..., USR-...) bozmamak için sayı kalıpları "önünde harf/tire yok" şartı arar.
public sealed partial class SensitiveDataMasker
{
    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]*")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\bbearer\s+[A-Za-z0-9\-._~+/]{8,}=*", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\b(password|passwd|pwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token|token|authorization)\b\s*[:=]\s*(?!bearer\b)[^\s,;]+", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<![\w\-])\+\d[\d\s\-()]{7,}\d(?![\w\-])")]
    private static partial Regex InternationalPhone();

    [GeneratedRegex(@"(?<![\w\-])\d{10,13}(?![\w\-])")]
    private static partial Regex LongNumber();

    [GeneratedRegex(@"(?<![\w\-])(?:\d[ \-]?){13,16}(?![\w\-])")]
    private static partial Regex CardNumber();

    /// TR IBAN: TR + 24 hane (boşluklu veya boşluksuz).
    [GeneratedRegex(@"\bTR\d{2}(?:\s?\d{4}){5}\s?\d{2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex Iban();

    /// Türkiye araç plakası (KVKK: araç sahibine götüren kişisel veri): il kodu 01–81 + 1–3 harf + 2–4 rakam. "34 KLP 482".
    [GeneratedRegex(@"(?<![\w\-])(?:0[1-9]|[1-7]\d|8[01])\s?[A-Z]{1,3}\s?\d{2,4}(?![\w\-])")]
    private static partial Regex Plate();

    public string? Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var s = Jwt().Replace(text, "[MASKED_TOKEN]");
        s = Bearer().Replace(s, "Bearer [MASKED_TOKEN]");
        s = KeyValue().Replace(s, "$1=[MASKED]");
        s = Email().Replace(s, "[MASKED_EMAIL]");
        s = Iban().Replace(s, "[MASKED_IBAN]");
        s = Plate().Replace(s, "[MASKED_PLATE]");
        s = CardNumber().Replace(s, "[MASKED_NUMBER]");
        s = InternationalPhone().Replace(s, "[MASKED_PHONE]");
        s = LongNumber().Replace(s, "[MASKED_NUMBER]");
        return s;
    }

    public string MaskRequired(string text) => Mask(text) ?? "";
}
