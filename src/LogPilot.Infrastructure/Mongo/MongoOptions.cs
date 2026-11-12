namespace LogPilot.Infrastructure.Mongo;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "logpilot";
    /// Loglar (ve senaryo çalıştırma kayıtları) bu kadar saat sonra MongoDB TTL ile otomatik silinir.
    /// Değiştirilirse mevcut TTL indeksi açılışta yerinde güncellenir (veri silmeye gerek yok).
    public int LogRetentionHours { get; set; } = 24;
    public int QueryTimeoutSeconds { get; set; } = 5;

    /// AI araçlarının kullandığı SALT-OKUNUR hesap (rol: logpilotAiReader). Boşsa AI araçları uygulama hesabını kullanır;
    /// salt-okunurluk o zaman yalnızca kodda (okuma arayüzleri) sağlanır, veritabanında değil.
    public string? ReadOnlyConnectionString { get; set; }

    /// true: AI okuyucu hesabında yazma yetkisi varsa (veya auth kapalıysa) API açılmaz. Üretimde true olmalı.
    public bool RequireReadOnlyAiAccess { get; set; }
}
