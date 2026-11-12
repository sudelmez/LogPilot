namespace LogPilot.LogGenerator;

public sealed class GeneratorOptions
{
    public const string SectionName = "Generator";

    /// Boşsa, çalışma dizininden yukarı doğru "scenarios" klasörü aranır.
    public string? ScenariosPath { get; set; }

    /// Boşsa, yukarı doğru "seed" klasörü aranır (users.json burada).
    public string? SeedPath { get; set; }

    /// 0'dan büyükse: açılışta `logs` koleksiyonu BOŞSA bu kadar dakikalık geçmiş log üretilir (Docker ilk kurulum).
    /// Veri varsa hiçbir şey yapmaz, yani yeniden başlatmalarda loglar çoğalmaz. 0 = kapalı.
    public int InitialBackfillMinutes { get; set; }

    public NormalTrafficOptions NormalTraffic { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
    public ProcessorOptions Processor { get; set; } = new();

    public bool IsValid() =>
        InitialBackfillMinutes is >= 0 and <= 7 * 24 * 60
        && NormalTraffic.MinIntervalSeconds >= 1
        && NormalTraffic.MaxIntervalSeconds >= NormalTraffic.MinIntervalSeconds
        && NormalTraffic.MinFlowsPerTick >= 1
        && NormalTraffic.MaxFlowsPerTick >= NormalTraffic.MinFlowsPerTick
        && NormalTraffic.MaxLogCount > 0
        && Scheduler.TickSeconds >= 1
        && Scheduler.IntervalMultiplier is >= 0.1 and <= 100
        && Processor.PollIntervalMilliseconds >= 100;
}

public sealed class NormalTrafficOptions
{
    public bool Enabled { get; set; } = true;
    public int MinIntervalSeconds { get; set; } = 60;
    public int MaxIntervalSeconds { get; set; } = 120;
    public int MinFlowsPerTick { get; set; } = 1;
    public int MaxFlowsPerTick { get; set; } = 3;
    public long MaxLogCount { get; set; } = 200_000;   // aşılırsa normal trafik duraklar
}

public sealed class SchedulerOptions
{
    public int TickSeconds { get; set; } = 5;

    /// Hata senaryolarının aralığını (senaryo ayarındaki intervalSeconds) bu kadarla çarpar.
    /// 1 = senaryodaki gibi, 3 = üç kat seyrek. Senaryo ayarlarına dokunmadan genel tempoyu ayarlar.
    public double IntervalMultiplier { get; set; } = 3;
}

public sealed class ProcessorOptions
{
    public int PollIntervalMilliseconds { get; set; } = 1000;
}
