# Sigorta Teklifi Kurgusu: İki Log Tablosu, Bir Asistan

> Bu doküman LogPilot'a eklenen **sigorta teklifi** dünyasını anlatır: hangi veriler nerede durur, tablolar hangi
> anahtarlarla birbirine bağlanır, hangi arızalar simüle edilir ve AI asistanı **hangi soruda hangi tabloya bakacağını**
> nasıl öğrenir. Tüm veriler sentetiktir (kimlik numaraları dahil).

---

## 1. Hikâye

Bir sigorta aracı şirketi düşünün. Müşteriler üç kanaldan **Kasko** ve **Trafik** teklifi alıyor:

| Kanal | Kim başlatıyor? | Hangi sistemler log yazıyor? |
|---|---|---|
| **Mobil uygulama** | Müşterinin kendisi (giriş yapmış) | Mobil backend → `logs`, teklif motoru → `quote_logs` |
| **Web (anonim)** | Giriş yapmamış ziyaretçi, sadece T.C. kimlik no girer | Sadece teklif motoru → `quote_logs` |
| **Acente** | Acente kullanıcısı (DEVR, KARL) müşteri adına | Sadece teklif motoru → `quote_logs` |

Mobil uygulama ekibi ile teklif motoru ekibi **ayrı sistemler** işletiyor ve logları **ayrı tablolara** gidiyor.
Destek ekibine gelen şikâyet ise hep aynı cümle: *"Müşteri teklif alamıyor."* Asıl soru: **sorun hangi sistemde?**

Gerçek hayattaki zorluk tam da bu: mobil uygulama çoğu zaman sadece `502 Bad Gateway` görür; asıl neden
(SBM araç sorgusunun zaman aşımı, bir sigorta şirketinin API'sinin çökmesi, müşterinin kimlik verisinin uyuşmaması…)
diğer tablodadır. Bazen de tam tersi: istek teklif motoruna **hiç ulaşmamıştır**, sorun oturumdadır.

## 2. Veri modeli ve eşleştirme anahtarları

```
                ┌──────────────────────── users (müşteri/kullanıcı dizini) ────────────────────────┐
                │ userCode   userId    customerNo   identityNo (PII, 11 hane)   roles     status   │
                │ BOB        USR-1003  CUS-100103   99003023757                 Customer  Active   │
                │ DEVR       USR-1005  –            –                           Agent     Active   │
                └──────┬──────────────────────┬───────────────────────────────┬────────────────────┘
                       │ userCode             │ customerNo                    │ identityNo
                       ▼                      ▼                               ▼
   logs (app_logs)                    quote_logs (QuoteEngine)
   ─────────────────────────────      ──────────────────────────────────────────────────────────────
   userCode, correlationId  ◄──────►  correlationId   (sadece Mobil kanalda ortak)
   operation = RequestQuote           quoteId (TKL-…)  tüm adımları bağlar
   metadata.quoteId (varsa)  ──────►  userCode   = Mobil: müşteri · Acente: ACENTE · Web: boş
                                      customerNo = Mobil/Acente: dolu · Web: boş
                                      identityNo = her kanalda dolu (dışarıya maskeli)
```

| Soru | Eşleştirme yolu |
|---|---|
| Mobil isteğin teklif motorundaki karşılığı | `logs.correlationId = quote_logs.correlationId` |
| Bir teklifin mobil tarafı | `quote_logs.quoteId` → `correlationId` → `logs` |
| Acentenin müşteri adına yaptığı teklif | `users.customerNo = quote_logs.customerNo` (userCode **acentedir**) |
| Web'den anonim alınan teklif | `users.identityNo = quote_logs.identityNo` (tek anahtar) |
| Bir teklif kimin? | `quote_logs` → customerNo/identityNo → `users` |

**KVKK kuralı:** `identityNo` LLM'e **hiç** gönderilmez. Eşleştirme backend'de yapılır, çıktıda `*********95`
şeklinde maskelenir. Asistan "kimlik numarasıyla eşledim" diyebilir ama numarayı göremez. Testler bunu doğrular.

### `quote_logs` belgesinin şekli

```json
{
  "timestamp": "2026-11-12T10:15:03.120Z",
  "level": "Error",
  "service": "VehicleRegistryGateway",
  "quoteId": "TKL-20261112-48213",
  "step": "VehicleLookup",
  "channel": "Mobile",
  "product": "Kasko",
  "userCode": "BOB",
  "customerNo": "CUS-100103",
  "identityNo": "99003023757",
  "correlationId": "COR-20261112-3FA9C1",
  "errorCode": "VEHICLE_REGISTRY_TIMEOUT",
  "exceptionType": "TimeoutException",
  "message": "Vehicle registry (SBM) did not respond within 5000 ms for plate 34 KLP 482.",
  "durationMs": 5000,
  "metadata": { "provider": "SBM", "timeoutMs": "5000" }
}
```

### Teklif adımları

```
QuoteRequested ─► CustomerVerification ─► VehicleLookup ─► RiskScoring ─► InsurerPricing ×3 ─► QuoteIssued
(QuoteOrchestrator) (CustomerRegistryService, KPS) (VehicleRegistryGateway, SBM) (RiskScoringService) (InsurerGateway: INS_AKS, INS_NRD, INS_GNS)
                          │                          │                  │                 │
                          └──────── herhangi bir adım düşerse ─► QuoteFailed (QUOTE_FAILED = semptom)
```

## 3. Simüle edilen senaryolar

| Dosya | Tür | Kanal | Ne olur? | Kök neden nerede? |
|---|---|---|---|---|
| `normal-quote-mobile` | Normal | Mobil | Teklif 3 sigortacıdan fiyatla verilir | – |
| `normal-quote-web` | Normal | Web | Anonim Trafik teklifi, sadece kimlik no | – (eşleşme yalnız identityNo ile) |
| `normal-quote-agent` | Normal | Acente | Acente müşteri adına teklif alır | – (userCode = acente) |
| `quote-vehicle-registry-timeout` | Incident | Mobil | SBM yanıt vermez, teklif düşer; uygulama `502` görür | `quote_logs` → `VEHICLE_REGISTRY_TIMEOUT` |
| `quote-insurer-pricing-error` | Incident | Mobil | INS_NRD API'si 500 döner; teklif **2/3** sigortacıyla verilir, uygulama "başarılı" der | `quote_logs` → `INSURER_PRICING_ERROR` |
| `quote-identity-mismatch` | Incident | Acente | KPS kontrolünde kimlik verisi uyuşmaz | `quote_logs` → `CUSTOMER_IDENTITY_MISMATCH` (iş kuralı) |
| `quote-risk-rejected` | Incident | Mobil | Risk skoru limitin üstünde, otomatik ret; uygulama `422` | `quote_logs` → `RISK_REJECTED` (iş kuralı) |
| `quote-session-expired` | Incident | Mobil | Token süresi dolmuş; istek teklif motoruna **hiç ulaşmaz** | `logs` → `TOKEN_EXPIRED`; `quote_logs` **boş** |

Senaryolar aynı JSON formatındadır; yeni alanlar:

```json
{
  "users": { "status": "Active", "role": "Customer", "minCount": 1, "maxCount": 3 },
  "quote": { "channel": "Mobile", "products": ["Kasko", "Trafik"] },
  "steps": [
    { "offsetMs": 0, "application": "MobileApp", "service": "MobileGateway", "operation": "RequestQuote", "...": "..." },
    { "offsetMs": 60, "target": "Quote", "service": "QuoteOrchestrator", "operation": "QuoteRequested",
      "message": "Quote {quoteId} requested via {channel} for {product} (customer {customerNo}).",
      "metadata": { "plate": "{plate}" } },
    { "offsetMs": 1500, "target": "Quote", "service": "InsurerGateway", "operation": "InsurerPricing",
      "insurerCode": "INS_AKS", "premiumAmount": 14500 }
  ]
}
```

`target: "Quote"` olan adım `quote_logs`'a, diğerleri `logs`'a yazılır. Motor akış başına bir `quoteId`, `correlationId`,
plaka ve ürün üretir; kanal kuralı (hangi anahtarın loga yazılacağı) motorda uygulanır. Yeni değişkenler:
`{quoteId} {product} {customerNo} {customerCode} {channel} {plate}`.

## 4. AI'a nasıl öğretiyoruz? (5 katman)

Tek bir "akıllı prompt" yetmez; öğretim beş katmanda yapılır. Her katman bir öncekinin açığını kapatır.

### Katman 1 — Veri kataloğu (prompt'ta): *"Ne nerede durur?"*
`SystemPrompts.DataCatalog` üç tabloyu, her tablonun anahtarlarını, **kanala göre hangi anahtarın dolu olduğunu** ve
teklif adımlarının sırasını anlatır. Model "teklif" kelimesini görünce `quote_logs`'u, "acentenin teklifinde
userCode acentedir" bilgisini buradan öğrenir. Ayrıca her araç sonucunda logların `"source": "app_logs" | "quote_logs"`
alanı vardır: model kanıtın hangi sistemden geldiğini söyleyebilir.

### Katman 2 — Playbook'lar (prompt'ta): *"Hangi soruda hangi yol?"*

| Niyet | Tablo sırası | Yol |
|---|---|---|
| P1 Mobil/giriş/OTP | logs | `GetUserRecentErrors` → `FindRelatedLogs` |
| P2 Belirli kişi teklif alamıyor | users → quote_logs → logs | `TraceCustomerJourney` → bulgulara göre karar |
| P3 Teklif numarası verildi | quote_logs → logs → users | `FindRelatedLogs(quoteId)` (+ `GetCustomerProfile(quoteId)`) |
| P4 Toplu teklif sorusu | quote_logs | `GetQuoteFunnel` → `SearchQuoteLogs(errorCode)` |
| P5 Acentenin teklifleri | quote_logs → users | `SearchQuoteLogs(userCode=acente, channel=Agent)` |
| P6 SMS/poliçe bildirimi ulaştı mı? | quote_logs → users → sms_logs | `TraceNotificationDelivery` (toplu: `SearchSmsLogs`) |
| P7 Sistem sağlığı, hata oranı, incident | logs | `GetServiceHealth`, `GetErrorStatistics`, `GetActiveIncidents` |
| P8 Belirsiz (kişi var, sistem belli değil) | logs + quote_logs | Hem `GetUserRecentErrors` hem `TraceCustomerJourney` |

Bu tablo artık elle yazılmıyor: `InvestigationPlaybooks.cs` kayıt defterinden üretiliyor (bkz. bölüm 8).

Kural 10: *"Bir tablonun boş dönmesi 'sorun yok' demek değildir; doğru tabloya bak."*

### Katman 3 — Eşleştirmeyi yapan araçlar (backend): *"JOIN'i LLM yazmaz"*
LLM'e serbest Mongo sorgusu verilmez. Tablolar arası birleştirme deterministik, test edilebilir araçlarda yapılır:

| Araç | Ne yapar? |
|---|---|
| `TraceCustomerJourney(userCode \| customerNo \| quoteId)` | Müşteriyi `users`'tan çözer → tüm anahtarlarıyla `quote_logs` (mobil/web/acente) + `logs` → correlationId/quoteId ile bağlar → **platformun hesapladığı bulgular** döner: *"never reached QuoteEngine"*, *"QUOTE_REQUEST_FAILED sadece semptom, kök neden VEHICLE_REGISTRY_TIMEOUT"*, *"teklif acente DEVR tarafından alındı"*, *"INS_NRD teklifi eksik"* |
| `SearchQuoteLogs(...)` | `quote_logs` filtreli arama; `userCode`/`customerNo` verilirse kişi dizinden çözülüp **VEYA** ile tüm anahtarlarla eşlenir |
| `GetQuoteFunnel(product?, channel?, insurerCode?)` | İstenen/verilen/düşen/eksik-teklifli sayıları, adım bazlı hatalar, sigortacı başarı oranı ve süreleri |
| `GetCustomerProfile(userCode \| customerNo \| quoteId)` | Dizin kaydı (kimlik no maskeli); teklif numarasından müşteriye gider |
| `FindRelatedLogs(... , quoteId)` | Artık **iki tabloyu birden** getirir |

Böylece küçük bir model bile doğru JOIN'i "unutamaz"; model sadece *kimin* izini süreceğine karar verir.

### Katman 4 — Hata kodu semantiği: *"Semptom mu, kök neden mi, iş kuralı mı?"*
`ErrorCodeCatalog` ve prompt'taki "ERROR CODE SEMANTICS" bölümü:
- **Semptom** (asla kök neden değil): `QUOTE_REQUEST_FAILED`, `QUOTE_FAILED`, `LOGIN_FAILED`, `PROPOSAL_CREATION_FAILED`
- **İş kuralı** (hata değil, açıklanır): `RISK_REJECTED`, `CUSTOMER_IDENTITY_MISMATCH`, `USER_INACTIVE`
- **Teknik arıza**: `VEHICLE_REGISTRY_TIMEOUT`, `INSURER_PRICING_ERROR`, `TOKEN_EXPIRED`, …

`AnswerBuilder` zaten kanıtla desteklenmeyen kök nedeni siler; `TraceCustomerJourney` her teklifin kök hatasını
semptomları atlayarak hesaplar.

### Katman 5 — Değerlendirme testleri: *"Öğrendiğini kanıtla"*
`tests/LogPilot.UnitTests/Evaluation/AssistantEvaluationTests.cs` her playbook için gerçek senaryo dosyalarından
log üretip asistana soru sorar ve **değişmezleri** denetler (uydurma log/servis/hata kodu yok, kimlik no sızmıyor):

| Test | Doğruladığı davranış |
|---|---|
| `P2_mobile_quote_failure_is_traced_into_quote_logs_for_the_root_cause` | Kanıt iki tablodan; kök neden SBM timeout, 502 semptom |
| `P2_a_quote_that_never_reached_the_engine_is_blamed_on_the_app_side` | `quote_logs` boş → neden `TOKEN_EXPIRED` |
| `P2_an_agent_made_quote_is_found_for_the_customer_through_the_user_directory` | Müşteri uygulamayı hiç kullanmamış; acente teklifi customerNo ile bulunur |
| `P2_a_web_quote_is_matched_by_identity_number_without_exposing_it` | Web teklifi yalnız kimlik no ile eşlenir, numara cevaba sızmaz |
| `P3_a_quote_number_returns_the_flow_from_both_collections` | Teklif no → iki tablo + müşteri |
| `P4_missing_insurer_offers_are_explained_from_the_funnel` | Huni → INS_NRD → kanıt |
| `An_underwriting_decline_is_reported_as_a_business_rule_not_a_fault` | RISK_REJECTED "beklenen davranış" |
| `A_login_question_still_goes_to_the_app_logs_only` | Giriş sorusu teklif araçlarına sapmaz |

Mongo entegrasyon testleri (`QuoteLogsTests`) indeksleri, VEYA eşleştirmesini, Decimal128 primleri ve log detay
endpoint'inin teklif loglarını açmasını gerçek veritabanında doğrular. Gerçek bir LLM (Claude/Ollama) bağlandığında aynı
sorular arayüzden sorularak davranış karşılaştırılabilir; mock sağlayıcı bu playbook'ların "altın cevabı"dır.

## 5. Örnek konuşmalar

**"LENA'ya poliçe SMS'i ulaştı mı?"** (zincirleme, bkz. bölüm 8)
→ `users (userCode=LENA)` → `quote_logs` (LENA'nın en son poliçesi, kimlik no ile) → `sms_logs` (LENA'nın telefonu, poliçe
zamanından sonrası). Karar: **FailedToSend** — PROV_A sağlayıcısı mesajı 2 kez 503 ile reddetmiş, müşteriye ulaşmamış.


**"BOB mobil uygulamadan kasko teklifi alamıyor, neden?"**
1. `TraceCustomerJourney(userCode=BOB)` → BOB = CUS-100103; 6 app log + 9 quote log.
2. Bulgu: *Mobile quote request COR-…-3FA9C1 → quote TKL-…-48213: VEHICLE_REGISTRY_TIMEOUT in VehicleRegistryGateway at step VehicleLookup (the app only logged QUOTE_REQUEST_FAILED).*
3. Cevap: kök neden SBM araç sorgusunun 5 sn'de yanıt vermemesi; uygulamadaki 502 semptom. Kanıt: bir `app_logs` + bir `quote_logs` kaydı.

**"ALEX teklif alamıyor, sorun teklif sisteminde mi?"**
→ Bulgu: *never reached QuoteEngine; the app logged TOKEN_EXPIRED.* Cevap: Hayır, teklif motoruna istek hiç gitmemiş;
oturum süresi dolmuş, yeniden giriş gerekli.

**"ELIF neden teklif alamadı?"** (ELIF uygulamayı hiç kullanmamış)
→ `logs`'ta ELIF yok; dizinden CUS-100105 bulunur → `quote_logs`'ta acente DEVR'in açtığı teklif →
`CUSTOMER_IDENTITY_MISMATCH`. Cevap: sistem hatası değil; müşteri verisi KPS ile uyuşmuyor, acente veriyi düzeltmeli.

**"Kasko tekliflerinde neden bazı sigorta şirketlerinin teklifi gelmiyor?"**
→ `GetQuoteFunnel` → INS_NRD 4 fiyatlamanın 4'ünde hata, 4 teklif eksik sigortacıyla verilmiş →
`SearchQuoteLogs(errorCode=INSURER_PRICING_ERROR)` ile kanıt.

**"TKL-20261112-48213 numaralı teklif neden başarısız?"**
→ `FindRelatedLogs(quoteId)` her iki tablodan tüm akış + `GetCustomerProfile(quoteId)` ile kimin teklifi olduğu.

## 6. Yeni bir tablo eklerken (ör. poliçe/ödeme logları) tarif

1. **Domain + repository**: ayrı belge tipi ve koleksiyon; TTL + her eşleştirme anahtarı için indeks.
2. **Anahtar haritası**: hangi alan hangi tabloya bağlanıyor (bu dokümandaki diyagram gibi) — `users` merkezde kalsın.
3. **Araçlar**: bir arama aracı + bir toplu istatistik aracı; tablolar arası birleştirmeyi `TraceCustomerJourney`
   benzeri bir araca ekle. PII'yi backend'de eşle, dışarıya maskele.
4. **Prompt**: `DataCatalog`'a tabloyu, anahtarlarını ve bir playbook satırını ekle; yeni hata kodlarını
   semptom / iş kuralı / teknik olarak sınıflandır (`ErrorCodeCatalog`).
5. **Senaryolar**: en az bir "kök neden diğer tabloda" ve bir "diğer tablo boş" senaryosu yaz.
6. **Değerlendirme**: her playbook için bir test; `AssertGrounded` + PII sızıntı kontrolü.

## 7. Bilinen sınırlar

- SMS kararı (`Verdict`) yalnızca seçilen zaman aralığındaki teslim raporlarına bakar; operatörden rapor geç gelirse
  `Pending` döner.
- Kullanıcı sohbete telefon veya kimlik numarası yazarsa maskeleyici onu siler; arama kullanıcı kodu, müşteri no,
  teklif no veya poliçe no ile yapılmalıdır (bilinçli KVKK tercihi).

- Incident tespiti (`IncidentDetectionService`) şimdilik yalnız `logs` üzerinde çalışır; `quote_logs` hataları ancak
  mobil taraftaki semptom (`QUOTE_REQUEST_FAILED`) üzerinden incident olur. Teklif motoru için ayrı kural eklenebilir.
- Dashboard grafikleri `logs` tablosunu gösterir; teklif hunisi şimdilik `/api/quote-logs/funnel` ve asistan üzerinden.
- Huni hesabı seçilen penceredeki logları belleğe alır (en fazla 20.000 kayıt); çok büyük hacimde Mongo aggregation'a
  taşınmalıdır.

## 8. Zincirleme (multi-hop) araştırma: "Poliçe SMS'i müşteriye ulaştı mı?"

Büyük sistemlerde cevap çoğu zaman tek tabloda değildir; bir tablodan aldığın anahtarla diğerine, oradan aldığınla
üçüncüsüne gidersin. Bunun için üçüncü bir sistem eklendi:

| Tablo | Sistem | Tanıdığı anahtar |
|---|---|---|
| `quote_logs` | Teklif motoru + poliçe (`PolicyService`, `PolicyIssued` adımı, `policyNo` POL-…) | quoteId, policyNo, identityNo |
| `users` | Kullanıcı dizini — artık **telefon** da tutuyor (sentetik, maskeli çıkar) | userCode, customerNo, identityNo, phone |
| `sms_logs` | SMS ağ geçidi (`NotificationGateway`, `SmsProviderAdapter`) | **sadece telefon**, messageId, şablon |

SMS sistemi müşteriyi, teklifi ya da poliçeyi tanımaz. Bu yüzden zincir zorunludur:

```
1) quote_logs  quoteId / policyNo        → teklifin sahibi: identityNo (+ customerNo), poliçe zamanı
2) users       identityNo                → müşteri → telefon
3) sms_logs    telefon + poliçe zamanı   → POLICY_ISSUED mesajları: Queued → SentToProvider → Delivered/Failed/Undelivered
```

Kişiyle başlanırsa ("LENA'ya SMS gitti mi?") sıra `users → quote_logs → sms_logs` olur. Her halka `TraceHop` olarak
kaydedilir (hangi tablo, hangi anahtarla, ne bulundu). Bir halka koparsa zincir orada durur ve bu da bir bulgudur:
kimlik dizinde yok (veri kalitesi), müşterinin telefonu yok, kişi müşteri değil acente…

**Karar türleri:** `Delivered` · `NotDeliveredToHandset` (operatör teslim edememiş: bizim dışımızda) ·
`FailedToSend` (sağlayıcı reddetmiş) · `Pending` · `NeverSent` (poliçe var, SMS hiç istenmemiş — sebep
`quote_logs`'taki `NOTIFICATION_PUBLISH_FAILED`) · `NoPolicy` · `Unknown`.

**Senaryolar:** `normal-policy-sms` (teslim edildi), `policy-sms-provider-failure` (SMS_PROVIDER_ERROR),
`policy-sms-undelivered` (SMS_UNDELIVERED), `policy-sms-not-sent` (sms_logs boş, sebep quote_logs'ta).

### "Hangi soru gelirse nereye bakılır" — tek kaynak: `InvestigationPlaybooks.cs`

Playbook sayısı arttıkça bilgiyi prompt metninde elle tutmak kırılganlaşır (prompt bir yerde, araçlar başka yerde,
testler üçüncü yerde). Bu yüzden hepsi tek bir kayıt defterinde, kod olarak tutuluyor:

- `Sources`: her tablo — ne içerir, anahtarları, hangi araçla okunur.
- `Links`: tablolar arası köprüler (`quote_logs → users via identityNo → phone` gibi).
- `All`: playbook'lar — tetikleyici kelimeler (TR/EN), örnek sorular, **tablo sırası (Path)**, araçlar, dikkat edilecekler,
  "belirli kişi gerekir mi" kuralı ve öncelik.

Bu tek kaynaktan dört şey beslenir:

1. **Prompt**: `SystemPrompts.DataCatalog` = `RenderCatalog()` + hata kodu semantiği. Kural 11: "birden fazla tabloya
   baktıysan sırasını ve her adımı bağlayan anahtarı söyle".
2. **Yönlendirme ipucu**: `ChatOrchestrator` her soruyu `Classify` ile bir playbook'a eşler ve prompt'un sonuna
   *"ROUTING HINT: büyük ihtimalle P6, yol quote_logs → users → sms_logs, araç TraceNotificationDelivery"* ekler.
   Bağlayıcı değildir ("veri başka yeri gösteriyorsa ona uy"), ama küçük modellerin (Ollama) doğru araçla başlamasını
   ciddi şekilde kolaylaştırır. Hangi playbook'a gidildiği loglanır.
3. **Mock model**: testlerdeki kural tabanlı model aynı `Classify`'ı kullanır; yani "altın cevap" ile prompt aynı
   bilgiden gelir.
4. **Testler** (`InvestigationPlaybookTests`): her playbook'un örnek soruları kendi playbook'una düşmeli; playbook'ların
   andığı her araç ve tablo gerçekten var olmalı; her araç en az bir playbook'ta/tabloda anılmalı (model ne zaman
   kullanacağını bilsin); prompt bütün kataloğu içermeli; belirsiz sorular öncelik kurallarına uymalı
   (ör. "TKL-… teklifinin SMS'i gitti mi?" → teklif no olmasına rağmen P6).

**Sınıflandırma kuralları:** tetikleyici eşleşmesi olan playbook'lar → önceliğe göre (P6 > P3 > P5 > diğerleri) →
eşleşme sayısına göre. `Required`/`Forbidden` kuralı, aynı kelimelerin (teklif) kişi varken P2'ye, yokken P4'e
gitmesini sağlar. Hiçbir şey eşleşmez ama kişi varsa P8 (iki sisteme de bak).

### Yeni bir zincir eklemek (ör. ödeme → tahsilat → e-posta)

1. Yeni tabloyu ve anahtarını `Sources`'a, köprüleri `Links`'e ekle.
2. Zinciri yürüten bir araç yaz: her halkayı `TraceHop` olarak kaydet, koparsa dur ve sebebini bulgu olarak döndür,
   PII'yi backend'de çöz ve maskele.
3. `All`'a bir playbook ekle (tetikleyiciler, örnek sorular, Path, araçlar). `InvestigationPlaybookTests` örnek
   soruların doğru yönlendiğini otomatik kontrol eder.
4. Her karar türü için bir senaryo ve bir değerlendirme testi yaz (özellikle "son tablo boş, sebep ilk tabloda" tipi).
