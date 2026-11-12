# LogPilot: mimari

## Genel bakış
Modular Monolith + Clean Architecture. İki çalıştırılabilir host, bir MongoDB.

```
 React UI ──/api──► LogPilot.Api ──────────┐
                     │  IncidentDetectorWorker (arka plan)
                     │  Chat / AI orkestrasyonu
                     ▼                      ▼
              Application ◄── Infrastructure ──► MongoDB ◄── LogPilot.LogGenerator
                     ▲                                        (Worker Service)
                  Domain
```

Bağımlılık yönü içe doğrudur: `Api / LogGenerator → Infrastructure → Application → Domain`; `AI → Application`.
Domain MongoDB'yi, HTTP'yi veya herhangi bir LLM'i bilmez.

| Proje | Sorumluluk |
|---|---|
| Domain | Varlıklar ve saf kurallar: `ConfidenceCalculator`, `IncidentRules`, `ServiceHealthClassifier`, `TrendBuckets` |
| Application | Kullanım senaryoları, port arayüzleri (`ILogRepository`, …), `ILogAnalysisTools` (yapay zekânın tek kapısı), maskeleme |
| Infrastructure | MongoDB eşlemeleri, repository'ler, aggregation'lar, indeksler |
| AI | `ILlmProvider`, mock sağlayıcı, araç kaydı, orkestratör, cevap doğrulama, enjeksiyon koruması |
| Api | Minimal API uç noktaları, hata eşleme (ProblemDetails), sağlık kontrolleri, incident tespit worker'ı |
| LogGenerator | Senaryo motoru, normal trafik, zamanlayıcı, Pending kuyruğu işleyicisi, backfill |

## API ↔ Generator: MongoDB bir komut kuyruğu
Mesaj kuyruğu yok. `POST /api/scenarios/{id}/trigger` bir `scenario_executions` kaydı yazar (`Pending`).
Generator `FindOneAndUpdate` ile kaydı **atomik** sahiplenir (`Running`), çalıştırır (`Completed`/`Failed`).
Zamanlanmış çalıştırmalar da aynı yoldan geçer → geçmiş tek yerde. Çöken worker'ın `Running` bıraktığı kayıtlar
açılışta `Failed` yapılır. Alternatif change stream'dir; replica set gerektirdiği için seçilmedi.

## Veri modeli ve indeksler
Koleksiyonlar: `logs`, `incidents`, `chat_sessions`, `chat_messages`, `scenarios`, `scenario_executions`, `users`.

| İndeks | Sorgu |
|---|---|
| `logs {timestamp}` TTL 24 saat | Tarih filtresi, sıralama, trend; büyümeyi sınırlar (ayrı timestamp indeksi gerekmez) |
| `logs {userCode, timestamp:-1}` | Kullanıcı logları, AI araçları |
| `logs {service, timestamp:-1}` | Servis filtresi |
| `logs {errorCode, timestamp:-1}` partial (`errorCode` var) | Hata kodu araması, hata grupları; çoğu log hata olmadığı için küçük kalır |
| `logs {correlationId}`, `{transactionId}` | Akış araması |
| `scenario_executions {requestedAt}` TTL, `{status, requestedAt}`, `{scenarioId, requestedAt}` | Geçmiş, kuyruk yoklama, senaryo bazlı liste |
| `incidents {status, lastSeenAt}`, `{errorCode, lastSeenAt}` | Aktif liste, tespit |
| `chat_messages {sessionId, createdAt}` | Sohbet geçmişi |

Trade-off: her indeks yazma maliyeti getirir; generator sürekli yazdığı için log indeksleri bu listeyle sınırlı.
`level` için ayrı indeks yok (düşük kardinalite). "Son hatalar" sorgusu zaman indeksini geriye tarar; hata oranı düşük
olduğu sürece yeterli. Kanıt: entegrasyon testi `explain` ile `ix_user_timestamp` kullanımını doğrular.
`Mongo:LogRetentionHours` (varsayılan 24) değişirse açılışta mevcut TTL indeksi `collMod` ile yerinde güncellenir; veri silmeye gerek yoktur.

## İki farklı hata oranı
- **Log hata oranı** = Error+Critical log / tüm loglar: sistemin ne kadar "gürültülü" olduğu.
- **İstek hata oranı** = `outcome=Failure` / (`Success`+`Failure`): kullanıcının yaşadığı başarısızlık. Bir istek 5 hata logu üretebilir.

## Generator
Senaryolar `scenarios/*.json` (adım tabanlı; `Normal` arka plan trafiği ve `Incident`). Motor saf bir fonksiyondur:
aynı senaryo + kullanıcılar + seed + "şimdi" aynı logları üretir. Akış, "şimdi"de bitecek şekilde geriye doğru
yerleştirilir (gelecek tarihli log yok). Her kullanıcı akışının kendi `correlationId`/`transactionId` değeri vardır.
Canlı ayarlar (`enabled`, `intervalSeconds`, `probability`, `weight`) DB'de yaşar; dosya tanımı onları ezmez.

## Incident tespiti
Son 15 dakikadaki Error/Critical loglar hata koduna göre gruplanır. Semptom kodları (`LOGIN_FAILED`, …) incident
açmaz. Bir kod en az 3 kez görülürse incident açılır; ciddiyet etkilenen kullanıcı/servis sayısına göre belirlenir
ve otomatik düşürülmez. **Hiçbir zaman otomatik Resolved yapılmaz**: çözüldüğünü kanıtlayan veri yoktur, bunu kullanıcı işaretler.

## Hata yönetimi
`RequestValidationException` → 400, `NotFoundException` → 404, `ConflictException` → 409, sorgu zaman aşımı → 504,
istemci iptali → 499. Hepsi RFC 7807 ProblemDetails. Her istekte `X-Correlation-Id` (doğrulanmış) log kapsamına girer.

## Bilinçli olarak yapılmayanlar
MediatR/CQRS, her sınıfa arayüz, mikroservis, Redis, SignalR/streaming, kimlik doğrulama. Gerekçeleri: MVP'nin
yönetilebilir kalması; her biri sonradan eklenebilir.

## Bilinen sınırlamalar
- Kimlik doğrulama/yetkilendirme yok: sohbet oturumları tek "demo" kullanıcıya aittir. Araçlar yine de tarih aralığı ve alan sınırlarıyla kısıtlıdır.
- Sayfalama `skip/limit` (24 saatlik TTL'li veri için yeterli); büyürse timestamp tabanlı cursor'a geçilmeli.
- LLM sağlayıcıları: Mock (varsayılan), Ollama (yerel, ücretsiz) ve Claude (`ANTHROPIC_API_KEY` gerekir). Bkz. ai-design.md.
- Backfill çalıştırma kaydı oluşturmaz; `seed-data.sh` tekrar çalıştırılırsa loglar çoğalır.
