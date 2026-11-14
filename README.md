# LogPilot

Yerel çalışan, yapay zekâ destekli **uygulama logu analiz ve teknik destek platformu**.
Sentetik servisler sürekli log üretir; iş kullanıcıları doğal dilde sorar ("TCLI mobil uygulamaya neden giriş yapamıyor?"),
asistan **gerçek loglardan kanıt toplayıp** nedenleri, güven düzeyini ve çözüm adımlarını açıklar. Uydurmaz, kanıt yoksa söyler.

> Tüm kullanıcılar, loglar, işlemler ve hatalar sentetiktir. Gerçek veri, kimlik bilgisi veya özel kod yoktur.

## Neler var
| | |
|---|---|
| **Backend** | .NET 10, ASP.NET Core minimal API, MongoDB, Modular Monolith + Clean Architecture |
| **Generator** | Worker Service; JSON senaryoları, normal trafik + 8 hata senaryosu, tetikleme API'si |
| **Analytics** | MongoDB aggregation: trend, servis dağılımı, en sık hatalar, servis sağlığı, log vs istek hata oranı |
| **AI** | Araç çağırma, sağlayıcı soyutlaması (mock dahil), kanıt doğrulama, hesaplanan güven, enjeksiyon koruması, maskeleme |
| **Incidents** | Kural tabanlı tespit, ciddiyet, AI özeti, durum yönetimi |
| **UI** | React + TypeScript + MUI: Dashboard, AI Assistant, Logs Explorer, Incidents, Scenarios |
| **Kalite** | Birim + entegrasyon + AI değerlendirme testleri, sağlık kontrolleri, JSON loglama, Docker |

## Gereksinimler
.NET 10 SDK, Node.js 20+, MongoDB (yerel kurulum **veya** Docker), `mongosh` (reset script'i için: `brew install mongosh`)

## Hızlı başlangıç (yerel)
```bash
brew services start mongodb-community     # veya: docker compose up -d --wait mongo
./scripts/reset-database.sh -y            # (opsiyonel) temiz başla
./scripts/seed-data.sh 120                # kullanıcılar + senaryolar + son 120 dk log
./scripts/start-local.sh                  # API (5080) + LogGenerator
./scripts/start-ui.sh                     # UI: http://localhost:5173
```
- API dokümantasyonu: <http://localhost:5080/scalar> · OpenAPI: `/openapi/v1.json`
- Sağlık: `/health/live` (süreç), `/health/ready` (MongoDB), `/health` (hepsi)

## Tek tıkla (Docker)
Sadece **Docker Desktop** gerekir (.NET, Node veya MongoDB kurmak gerekmez).

- **Başlat:** Finder'da `start.command` dosyasına çift tıkla (veya `./scripts/docker-up.sh`).
  Docker kapalıysa açar, her şeyi derler, sağlıklı olana kadar bekler ve <http://localhost:5173> adresini açar.
- **Durdur:** `stop.command` (veya `./scripts/docker-down.sh`). Veri korunur.
- **Sıfırla:** `./scripts/docker-down.sh --wipe` (MongoDB verisi silinir; sonraki açılışta temiz başlar).

| Konteyner | Görev | Adres |
|---|---|---|
| `logpilot-ui` | nginx + derlenmiş React; `/api` ve `/health` isteklerini API'ye iletir | <http://localhost:5173> |
| `logpilot-api` | API + incident tespiti + AI asistanı | <http://localhost:5080/scalar> |
| `logpilot-generator` | Sürekli log üretimi; **boş veritabanında ilk açılışta son 120 dk'yı doldurur** | – |
| `logpilot-mongo` | MongoDB 8, veri `mongo-data` volume'ünde kalıcı | `localhost:27017` (yerel MongoDB açıksa `27018`) |

Ayarlar (portlar, ilk doldurma süresi, normal trafik, AI sağlayıcısı) için `.env.example` dosyasını `.env` olarak kopyalayıp düzenle.
Faydalı komutlar: `docker compose ps`, `docker compose logs -f generator`, `docker compose up -d --build` (kod değişince).

> İlk derleme birkaç dakika sürer (imajlar indirilir); sonrakiler önbellekten gelir.
> Docker Desktop'ı kurulum diskinden (`/Volumes/Docker`) değil, **Applications** klasöründen çalıştır: aksi halde yanıt vermez.

## Yapay zekâ: Mock, Ollama veya Claude
Sağlayıcı `.env` içindeki `LOGPILOT_AI_PROVIDER` ile seçilir (`cp .env.example .env`). Hepsi aynı araçları ve aynı
doğrulama hattını kullanır; cevaptaki `provider` alanı hangisinin cevapladığını gösterir.

| | Mock (varsayılan) | Ollama | Claude |
|---|---|---|---|
| Ücret | Ücretsiz | Ücretsiz (bilgisayarında çalışır) | Kullandıkça ödenir |
| Kalite | Anahtar kelime kuralları | Orta (küçük model) | En iyi |
| Hız | Anında | Yavaş (soru başına ~0,5–3 dk) | Hızlı |
| Gerekenler | – | Ollama + ~5 GB model, 16 GB RAM önerilir | API anahtarı |

**Ollama (ücretsiz, yerel):**
```bash
brew install ollama
ollama serve &                # yalnızca modeli indirmek için geçici olarak aç
ollama pull qwen3:8b          # araç çağırmayı destekleyen bir model (~5 GB)
kill %1
```
`.env`: `LOGPILOT_AI_PROVIDER=Ollama` (model: `LOGPILOT_OLLAMA_MODEL`). Ollama Docker'da değil Mac'te çalışır (GPU için);
Docker'daki API ona `host.docker.internal` ile ulaşır. Ollama'yı ayrıca açık tutmana gerek yok: `start.command` onu yalnızca
o oturum için başlatır (bilgisayar açılışına kaydetmez) ve modelin inik olduğunu kontrol eder; `stop.command` kapatır.
Ollama'yı başka bir iş için kendin açtıysan `stop.command` ona dokunmaz. (Elle: `./scripts/ollama-service.sh start|stop`.)
`brew services start ollama` kullanma: Ollama'yı her girişte otomatik başlatır.
Bilgisayarı yormamak için `LOGPILOT_AI_AUTO_SUMMARY=false` önerilir (incident özeti yalnızca "Regenerate" ile üretilir).

**Claude (ücretli):** <https://console.anthropic.com> → **API Keys** ile anahtar oluştur (Claude uygulama aboneliğinden ayrıdır),
`.env`: `LOGPILOT_AI_PROVIDER=Claude` ve `ANTHROPIC_API_KEY=sk-ant-...`. İsteğe bağlı: `LOGPILOT_AI_MODEL`
(varsayılan `claude-opus-5-5`), `LOGPILOT_AI_EFFORT` (`low`…`max`). Console'da harcama limiti koymak iyi bir fikirdir.
Anahtarı asla `appsettings.json`'a veya koda yazma; `.env` git'e girmez.

Değişiklikten sonra `stop.command` + `start.command` (veya `./scripts/start-local.sh`) ile yeniden başlat; ikisi de `.env`'i okur.

## Vaka hafızası (asistan geçmişi hatırlar)
Modeller sohbetlerden kendiliğinden öğrenmez; LogPilot bunun yerine **insanların doğruladığı** çözümleri saklar ve
aynı hata tekrar görüldüğünde önerir. Mock, Ollama ve Claude ile aynı şekilde çalışır; veri MongoDB'de (`knowledge_cases`) kalır.

- **Hafızaya girişi:** asistan cevabında 👍 → "What actually fixed it?" alanına gerçek çözümü yaz; veya incident'ı
  *Resolved* yaparken çözüm notu yaz; veya **Knowledge** sayfasından elle ekle. 👎 ya da notsuz 👍 hafızaya girmez:
  model kendi (belki yanlış) cevabını "bilgi" sayamaz.
- **Kullanım:** her cevaptan sonra backend, cevabın doğrulanmış kanıtındaki hata kodlarıyla eşleşen vakaları
  **Related past cases** olarak ekler; model de `SearchKnowledgeBase` aracıyla arayabilir. Geçmiş vakalar ipucudur:
  kanıt sayılmaz, güven puanını etkilemez.
- **Yönetim:** Knowledge sayfasında ara, düzenle, sil; her vakanın kaç kez önerildiği görünür. Kaydedilen metin maskelenir.

## Örnek sorular
- Why can't TCLI log in to the mobile application?
- Which service has the highest error rate today?
- Why are proposal creation requests failing?
- Are multiple users affected by the same error?
- Is there an active MongoDB-related incident?
- What errors occurred in the last 15 minutes?
- Summarize today's critical incidents.
- Which errors are related to correlation ID COR-20261108-001ABC?

Bir incident görmek için: `curl -X POST localhost:5080/api/scenarios/mongo-outage/trigger` (≈30 sn içinde Incidents sayfasında).

## API özeti
| Alan | Uç noktalar |
|---|---|
| Logs | `GET /api/logs` · `/api/logs/{id}` · `/by-user/{userCode}` · `/by-correlation/{id}` · `/by-transaction/{id}` · `/recent-errors` |
| Analytics | `GET /api/analytics/summary` · `/error-trend` · `/errors-by-service` · `/top-error-codes` · `/service-health` · `/error-groups` |
| Scenarios | `GET /api/scenarios` · `/{id}` · `POST /{id}/trigger` (202/409) · `PUT /{id}/settings` · `GET /executions` |
| Incidents | `GET /api/incidents` · `/{id}` · `/{id}/logs` · `PATCH /{id}/status` · `POST /{id}/summary` |
| Chat | `POST /api/chat/sessions` · `GET /api/chat/sessions` · `GET/POST /api/chat/sessions/{id}/messages` |

Hatalar RFC 7807 ProblemDetails; her yanıtta `X-Correlation-Id`.

## Testler
```bash
dotnet test
```
- `LogPilot.UnitTests`: iş mantığı, senaryo motoru, güven hesabı, maskeleme, enjeksiyon, **AI değerlendirme** (MongoDB gerekmez).
- `LogPilot.IntegrationTests`: gerçek MongoDB + `WebApplicationFactory` (aggregation doğruluğu, indeksler, uçtan uca API).
  MongoDB erişilemezse **atlanır**. Farklı adres için: `LOGPILOT_TEST_MONGO=mongodb://host:27017`. Her çalıştırma kendi geçici veritabanını kullanır ve siler.

## Dokümantasyon
- [Mimari](docs/architecture.md): katmanlar, veri modeli, indeksler ve trade-off'lar, generator, incident tespiti
- [AI tasarımı](docs/ai-design.md): araçlar, doğrulama, güven yöntemi, enjeksiyon koruması, sağlayıcı ekleme, kapsam koruması, AI rolleri (salt-okunur MongoDB hesabı) ve KVKK
- [Sigorta teklifi kurgusu](docs/quote-scenario.md): `logs` ↔ `quote_logs` ↔ `users` ve `sms_logs` eşleştirmesi, teklif/poliçe SMS senaryoları, zincirleme araştırma ve AI yönlendirme playbook'ları
- [Kurumsal web poliçe satın alma](docs/purchase-scenario.md): `proposal_step_logs` → `winsure_transactions` → `payment_transactions` dağıtık logları, zincir sırası, kararlar ve rol güncelleme

## Sorun giderme
- **API açılmıyor / `Mongo:ConnectionString is required`**: `appsettings.json` bulunamadı; çalışma dizinini kontrol edin.
- **Senaryolar sayfası boş**: katalog generator açılışında yüklenir; `./scripts/seed-data.sh` veya `start-local.sh` çalıştırın.
- **Incidents boş**: bir hata kodu son 15 dk'da ≥3 kez görülmeli; bir senaryoyu tetikleyin.
- **Log saklama süresi**: loglar 24 saat tutulur (`LOGPILOT_LOG_RETENTION_HOURS`); değiştirip yeniden başlatmak yeterli, mevcut TTL indeksi kendiliğinden güncellenir.

## Durum
Faz 1–7 tamamlandı; Claude sağlayıcısı eklendi. Yol haritası fikirleri: öğrenen vaka hafızası (çözülen vakalardan bilgi tabanı), kimlik doğrulama + rol bazlı araç yetkisi,
akış (streaming) cevaplar, cursor tabanlı sayfalama.
