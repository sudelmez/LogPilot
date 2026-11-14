using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogPilot.AI.Security;

/// Sorunun asistanın görev alanında olup olmadığına karar veren İLK savunma hattı (LLM'den ÖNCE, deterministik).
/// Asistanın tek işi: bu platformun loglarını (uygulama, teklif, SMS) inceleyip sorun araştırmak.
///   Abusive  → argo/hakaret: LLM'e hiç gitmez, kibar ret.
///   OffTopic → genel sigorta danışmanlığı, şirket hakkında sorular, gündelik/alakasız konular: kibar ret.
///   InScope  → açık bir log/sorun sinyali var: normal akış.
///   Unclear  → karar LLM'e bırakılır (prompt'taki SCOPE kuralı + "outOfScope" alanı ikinci savunma hattıdır).
/// Bilinçli olarak TEMKİNLİ: gerçek bir log sorusunu yanlışlıkla reddetmek, alakasız bir soruyu LLM'e bırakmaktan kötüdür.
/// Bu yüzden önce "alan sinyali" aranır; varsa konu dışı kalıpları reddetmez.
public static partial class ScopeGuard
{
    public enum Verdict { InScope, Unclear, OffTopic, Abusive, NotPermitted }

    public sealed record Result(Verdict Verdict, string? Reason);

    // ---------------- argo / hakaret ----------------

    /// Tam kelime eşleşmesi (normalize edilmiş). "şikayet" → "sikayet" gibi masum kelimeler yanlışlıkla yakalanmasın diye
    /// kök eşleşmesi sadece güvenli köklerde yapılır.
    private static readonly HashSet<string> AbusiveWords = new(StringComparer.Ordinal)
    {
        // "sik"/"mal" bilinçli olarak YOK: "sık sık hata" → "sik sik", "mal varlığı" gibi masum kullanımlar yakalanırdı.
        "amk", "aq", "amq", "siktir", "pic", "ibne", "kahpe", "yarrak", "yarak", "salak", "aptal",
        "dangalak", "gerizekali", "ahmak", "serefsiz", "pezevenk", "orospu",
        "fuck", "fucking", "shit", "bitch", "asshole", "bastard", "idiot", "stupid", "moron", "cunt", "dumb", "wtf"
    };

    private static readonly string[] AbusiveStems =
    [
        "orospu", "siktir", "sikerim", "sikeyim", "sikiyim", "amina", "aminako", "anani", "ananiz", "yarrag", "serefsiz", "gerizekal",
        "pezevenk", "motherfuck", "fucker", "fucked", "bullshit", "dangalak"
    ];

    // ---------------- veri değiştirme talepleri (rol: salt-okunur analist) ----------------

    /// Türkçe emir kipleri TAM kelime olarak aranır: "güncellemesi başarısız", "loglar neden silindi" gibi
    /// soru cümleleri yakalanmasın.
    private static readonly HashSet<string> ModifyCommands = new(StringComparer.Ordinal)
    {
        "sil", "silin", "silsene", "temizle", "temizleyin", "guncelle", "guncelleyin", "sifirla", "sifirlayin", "kapat", "kapatin",
        "ekle", "ekleyin", "degistir", "degistirin", "kaldir", "kaldirin", "duzenle", "duzenleyin", "bosalt", "bosaltin",
        "onayla", "onaylayin"
    };

    private static readonly string[] ModifyPhrases =
    [
        "siler misin", "silermisin", "silebilir misin", "silebilirmisin", "temizler misin", "temizleyebilir misin",
        "gunceller misin", "guncelleyebilir misin", "sifirlar misin", "kapatir misin", "kapatabilir misin", "ekler misin",
        "degistirir misin", "degistirebilir misin", "kaldirir misin", "kaldirabilir misin", "duzenler misin",
        "iptal et", "iptal edin", "iptal eder misin", "iptal edebilir misin", "onaylar misin", "onaylayabilir misin",
        "iade et", "iade edin", "iade eder misin", "iade yap", "geri ode", "tekrar cek", "yeniden cek"
    ];

    [GeneratedRegex(@"(?:^|[.!?]\s*|\b(?:please|can you|could you|would you|pls)\s+)(?:please\s+)?(?:delete|remove|drop|truncate|wipe|purge|update|modify|insert|reset|clear|close|edit|overwrite|cancel|approve|refund|charge)\b")]
    private static partial Regex EnglishModifyRx();

    private static readonly string[] DataObjects =
    [
        "log", "kayit", "tablo", "koleksiyon", "collection", "veri", "data", "database", "veritaban", "db", "mongo", "kullanici",
        "user", "musteri", "customer", "incident", "olay", "teklif", "quote", "police", "policy", "sms", "mesaj", "message",
        "hafiza", "knowledge", "vaka", "case", "sohbet", "chat", "record", "table", "index", "odeme", "payment", "islem",
        "transaction", "pol-", "prp-", "pay-"
    ];

    private static bool IsModificationRequest(string text, IReadOnlyList<string> words)
    {
        // İfade TAM eşleşmeli: "iptal et" bir emirdir, "iptal etti" / "iptal edildi" bir soru cümlesidir.
        var command = words.Any(ModifyCommands.Contains)
                      || ModifyPhrases.Any(p => Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(p)}(?![a-z0-9])"))
                      || EnglishModifyRx().IsMatch(text);
        return command && DataObjects.Any(o => StartsWord(text, o));
    }

    // ---------------- alan sinyalleri (varsa reddetme) ----------------

    [GeneratedRegex(@"\b(?:COR|TRX|TKL|CUS|POL|SMS|USR|PRP|PAY)-[A-Za-z0-9-]+|\b[0-9a-f]{24}\b", RegexOptions.IgnoreCase)]
    private static partial Regex IdentifierRx();

    private static readonly string[] DomainSignals =
    [
        // teknik
        "log", "hata", "error", "exception", "timeout", "zaman asimi", "servis", "service", "incident", "olay", "gateway", "mongo",
        "correlation", "transaction", "kritik", "critical", "503", "502", "500", "401",
        // sorun bildiren fiiller (normalize edilmiş: ş→s, ı→i, ç→c, ğ→g, ö→o, ü→u)
        "basarisiz", "failed", "fail", "calismiyor", "acilmiyor", "alamiyor", "alamadi", "yapamiyor", "yapamadi", "gelmiyor",
        "gelmedi", "gitmedi", "gitti mi", "gitmis mi", "ulasti", "ulasmadi", "ulasmiyor", "ulasmis", "dustu", "dusuyor", "reddedildi",
        "rejected", "can't", "cannot", "couldn't", "not working", "isn't working", "didn't", "doesn't", "why", "neden", "niye",
        "sorun", "problem", "issue", "takildi", "bekliyor",
        // platform kavramları
        "giris", "login", "log in", "sign in", "oturum", "token", "otp", "teklif", "quote", "police", "policy", "bildirim",
        "notification", "mesaj", "sms", "kullanici", "user", "musteri", "customer", "acente", "agent", "sigortaci", "insurer",
        "uygulama", "app",
        // satın alma
        "satin", "odeme", "payment", "purchase", "winsure", "tahsil", "3d secure", "iade", "refund", "onaylan", "iptal ol",
        "cekildi", "cekilmedi"
    ];

    // ---------------- konu dışı kalıplar ----------------

    private static readonly string[] OffTopicPatterns =
    [
        // şirket hakkında
        "sirketiniz", "sirketimiz", "sirketin", "firmaniz", "firmamiz", "your company", "our company", "the company",
        "ceo", "genel mudur", "yonetim kurulu", "kurucu", "founder", "maas", "salary", "insan kaynaklari", "is ilani",
        "kariyer", "career", "hisse", "stock price", "ofis", "office", "adresiniz", "telefon numaraniz", "iletisim",
        "kac calisan", "how many employees", "vizyon", "misyon", "mission",
        // genel sigorta danışmanlığı / ürün bilgisi
        "kasko nedir", "trafik sigortasi nedir", "sigorta nedir", "police nedir", "what is insurance", "what is kasko",
        "hangi sigortayi", "hangi kasko", "en iyi sigorta", "en ucuz", "cheapest", "tavsiye", "onerir misin", "oneri ver",
        "recommend", "advice", "ne kadar tutar", "fiyati ne", "fiyat ne kadar", "how much does", "teminat", "hasarsizlik",
        "nasil iptal", "iptal edebilirim", "nasil yaptiririm", "sigorta yaptir", "how do i buy", "prim ne kadar",
        // gündelik / alakasız
        "hava durumu", "weather", "yemek tarifi", "recipe", "tarif ver", "saka yap", "fikra", "joke", "siir", "poem", "sarki", "song",
        "futbol", "mac sonucu", "football", "siyaset", "politika", "politics", "secim", "election", "bitcoin", "borsa",
        "dolar kuru", "exchange rate", "film oner", "dizi oner", "movie", "odev", "homework", "ceviri yap", "translate",
        "hava nasil", "sevgili", "ask aci", "burc", "horoscope", "hikaye anlat", "tell me a story",
        "baskenti", "capital of", "dunyanin en", "kod yaz", "write code", "essay"
    ];

    public static Result Evaluate(string question)
    {
        var text = Normalize(question);
        var words = Regex.Split(text, @"[^a-z0-9]+").Where(w => w.Length > 0).ToList();

        // 1) Argo/hakaret: alan sinyali olsa bile reddedilir (soru kibarca tekrar sorulabilir).
        var abusive = words.FirstOrDefault(w => AbusiveWords.Contains(CollapseRepeats(w)) || AbusiveStems.Any(s => CollapseRepeats(w).StartsWith(s, StringComparison.Ordinal)));
        if (abusive is not null) return new Result(Verdict.Abusive, "abusive language");

        // 2) Veri değiştirme talebi: asistanın böyle bir yetkisi yok (salt-okunur analist). Log sorusu içerse de reddedilir.
        if (IsModificationRequest(text, words)) return new Result(Verdict.NotPermitted, "data modification request");

        // 2) Somut tanımlayıcı veya açık bir log/sorun sinyali varsa: görev alanında.
        if (IdentifierRx().IsMatch(question)) return new Result(Verdict.InScope, "identifier");
        // Kelime BAŞI sınırıyla eşleşir (Türkçe ekler sonda serbest): "mission" ≠ "permission", "ofis" = "ofiste".
        var offTopic = OffTopicPatterns.FirstOrDefault(p => StartsWord(text, p));
        var domain = DomainSignals.Where(s => StartsWord(text, s)).ToList();

        // Konu dışı kalıp + YALNIZCA zayıf sinyaller ("neden", "why", "sigorta" kelimesi) ⇒ konu dışı.
        // Ör. "Kasko neden pahalı?" → konu dışı; "Kasko teklifi neden gelmiyor?" → güçlü sinyal (gelmiyor) ⇒ görev alanında.
        var strong = domain.Where(s => s is not ("neden" or "niye" or "why" or "teklif" or "quote" or "police" or "policy" or "musteri" or "customer" or "app" or "uygulama" or "user" or "kullanici")).ToList();
        if (offTopic is not null && strong.Count == 0) return new Result(Verdict.OffTopic, offTopic.Trim());
        if (strong.Count > 0 || domain.Count >= 2) return new Result(Verdict.InScope, "domain signal");

        return new Result(Verdict.Unclear, null);
    }

    private static bool StartsWord(string text, string phrase)
    {
        for (var i = text.IndexOf(phrase, StringComparison.Ordinal); i >= 0; i = text.IndexOf(phrase, i + 1, StringComparison.Ordinal))
            if (i == 0 || !char.IsLetterOrDigit(text[i - 1])) return true;
        return false;
    }

    // ---------------- ret metinleri ----------------

    public static string RefusalMessage(Verdict verdict, string question)
    {
        var turkish = LooksTurkish(question);
        return (verdict, turkish) switch
        {
            (Verdict.NotPermitted, true) =>
                "Bu işlemi yapma yetkim yok. Ben salt-okunur bir analiz asistanıyım: logları okuyup sorunları araştırabilirim, " +
                "ama hiçbir veriyi silemem, değiştiremem veya ekleyemem. Bu tür işlemler yetkili ekip tarafından uygulama üzerinden yapılmalıdır.",
            (Verdict.NotPermitted, false) =>
                "I'm not allowed to do that. I'm a read-only analysis assistant: I can read the logs and investigate problems, " +
                "but I cannot delete, change or create any data. Such changes must be made by authorised staff in the application.",
            (Verdict.Abusive, true) =>
                "Bu şekilde ifade edilen bir talebe yardımcı olamıyorum. Loglarla ilgili bir sorunuz varsa memnuniyetle bakarım.",
            (Verdict.Abusive, false) =>
                "I can't help with a request phrased like that. If you have a question about the logs, I'm happy to look into it.",
            (_, true) =>
                "Üzgünüm, bu konuda size yardımcı olamıyorum. Ben yalnızca LogPilot'taki uygulama, teklif ve SMS loglarını inceleyerek " +
                "hata ve sorunları araştırabilirim. Örneğin: \"BOB neden teklif alamıyor?\" veya \"LENA'ya poliçe SMS'i ulaştı mı?\"",
            _ =>
                "Sorry, I can't help with that. I can only investigate problems in LogPilot's application, quote and SMS logs. " +
                "For example: \"Why can't BOB get a quote?\" or \"Did the policy SMS reach LENA?\""
        };
    }

    // ---------------- yardımcılar ----------------

    private static readonly string[] TurkishWords = [" ve ", " bir ", " mi", " mı", " mu", " neden ", " nedir", " nasıl", " ne ", " için ", " bu ", " şu ", " misin", " musun", " bana ", " sen"];

    /// Türkçe karakter içermeyen kısa mesajlar için ("salak bot", "kasko nedir") sık Türkçe kelimelere de bakılır.
    private static readonly HashSet<string> TurkishVocabulary = new(StringComparer.Ordinal)
    {
        "nedir", "neden", "niye", "nasil", "merhaba", "selam", "sistem", "hata", "teklif", "kasko", "sigorta", "police", "musteri",
        "kullanici", "bana", "anlat", "yardim", "lutfen", "tesekkur", "amk", "aq", "salak", "aptal", "siktir", "orospu",
        "gerizekali", "serefsiz", "dangalak", "ahmak", "kahpe", "ibne", "pic", "yarrak", "sil", "silin", "temizle", "guncelle",
        "sifirla", "kapat", "ekle", "degistir", "kaldir", "loglari", "kayitlari", "verileri", "tabloyu"
    };

    public static bool LooksTurkish(string text)
        => text.Any(c => "çğıöşüÇĞİÖŞÜ".Contains(c))
           || TurkishWords.Any(w => (" " + text.ToLower(CultureInfo.GetCultureInfo("tr-TR")) + " ").Contains(w))
           || Regex.Split(Normalize(text), @"[^a-z0-9]+").Any(w => TurkishVocabulary.Contains(CollapseRepeats(w)));

    /// Küçük harf (Türkçe), Türkçe karakterleri ASCII'ye katla, basit leetspeak'i çöz ("s1kt1r", "@ptal").
    public static string Normalize(string text)
    {
        var lower = text.ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        var sb = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            sb.Append(c switch
            {
                'ş' => 's', 'ı' => 'i', 'ç' => 'c', 'ğ' => 'g', 'ö' => 'o', 'ü' => 'u', 'â' => 'a', 'î' => 'i', 'û' => 'u',
                '@' => 'a', '$' => 's', '€' => 'e',
                '’' or '`' or '´' => '\'',
                _ => c
            });
        }
        return sb.ToString();
    }

    /// "salaaak" → "salak", "s1kt1r" → "siktir" (rakamlı leet sadece kelime içinde harflerle birlikteyse çözülür).
    private static string CollapseRepeats(string word)
    {
        if (word.Any(char.IsLetter))
            word = word.Replace('1', 'i').Replace('3', 'e').Replace('4', 'a').Replace('0', 'o').Replace('5', 's');
        var sb = new StringBuilder(word.Length);
        foreach (var c in word)
            if (sb.Length == 0 || sb[^1] != c || !char.IsLetter(c)) sb.Append(c);
        return sb.ToString();
    }
}
