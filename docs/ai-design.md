# LogPilot: yapay zekâ asistanı tasarımı

## Akış
```
Soru ─► ChatService ─► ChatOrchestrator
          (doğrulama, oturum)   │
                                ├─► ILlmProvider  ── araç çağrısı ister ──┐
                                │                                         ▼
                                │         ToolRegistry ─► ILogAnalysisTools (doğrulanmış, sınırlı, salt-okunur)
                                │                                         │ sonuç (maskelenmiş)
                                │         PromptInjectionGuard ◄──────────┘
                                │         <untrusted_log_data> sarmalı
                                ◄── (en fazla 5 iterasyon, 8 araç çağrısı)
                                ▼
                    LLM yapılandırılmış JSON cevap (HAM, güvenilmez)
                                ▼
                    AnswerBuilder: doğrula → güveni hesapla → temizle → maskele
                                ▼
                    AssistantAnswer (+ araç kayıtları) ─► chat_messages
```

## Araçlar (`ILogAnalysisTools`)
`SearchLogs`, `GetUserRecentErrors`, `GetServiceHealth`, `GetErrorStatistics`, `FindRelatedLogs`, `GetActiveIncidents`, `GetIncidentDetails`.

Yapay zekâ **serbest MongoDB sorgusu çalıştıramaz**. Her araç tipli bir istek nesnesi alır:
- İzinli alanlar: JSON argümanlar katı modda okunur; tanımsız alan = hata.
- Üst sınırlar: en fazla 50 sonuç; araç 10 sn zaman aşımı + `CancellationToken`.
- Tarih sınırı: kullanıcının seçtiği aralık (en çok 7 gün); araç bu aralığın dışına çıkamaz.
- Kimlik alanları `^[A-Za-z0-9_.\-]{1,64}$` ile doğrulanır.
- Araç hataları LLM'e "araç hatası" olarak döner; istisna fırlatmaz.

## Halüsinasyona karşı (backend doğrular, LLM'e güvenmez)
1. **Kanıt ID'leri**: LLM log metni değil log *ID'si* verir. Araç sonuçlarında olmayan ID atılır ve sınırlılık olarak raporlanır.
2. **Olası neden**: LLM bir hata kodu (`probableCauseCode`) belirtmek zorundadır; atıf yapılan loglardan biri o kodu taşımıyorsa neden kaldırılır.
3. **Servisler** sadece araçların gördüğü servislerle sınırlanır.
4. **"Çözüldü" iddiaları**: açık bir sonraki başarı logu (`outcome=Success`) yoksa cümle silinir.
5. **Güven** LLM'den alınmaz (`confidence` alanı yok sayılır).

## Güven değerlendirmesi (`ConfidenceCalculator`)
Deterministik ve açıklanabilir; her cevapta puan ve gerekçeler döner (UI'da tooltip).

| Etken | Puan |
|---|---|
| Destekleyen log sayısı: 1 / 2–4 / ≥5 | +5 / +15 / +25 |
| Açıklayıcı hata kodu (semptom değil) | +30 |
| Aynı correlation zinciri ≥2 serviste | +20 |
| Kanıt tutarlı (tek kod ≥%80) / karışık (<%50) | +10 / −15 |
| ≥2 farklı kullanıcıda doğrulanmış | +10 |
| Etkilenen kullanıcıda sonradan başarı (geçici olabilir) | −25 |
| Sonuçlar kesilmiş | −5 |

Eşikler: ≥70 High, ≥40 Medium, aksi Low. Kanıt yoksa `Insufficient`; kök neden iddiası yoksa (ör. servis sağlığı) `NotApplicable`.
Tek bir log asla High olmaz: doğrulama (birden çok log ve kullanıcı) gerekir.

## Vaka hafızası (knowledge base)
Model eğitilmez; öğrenme **doğrulanmış vaka** kaydıyla olur (`KnowledgeService`, koleksiyon `knowledge_cases`).

| Giriş | Koşul | Kaynak |
|---|---|---|
| Cevaba 👍 + çözüm notu | Notsuz 👍 ve 👎 yalnızca geri bildirimdir | `ChatFeedback` (mesaj başına tek vaka, tekrar onayda güncellenir) |
| Incident *Resolved* + çözüm notu | Not yalnızca çözülürken verilebilir | `IncidentResolution` (incident başına tek vaka) |
| Knowledge sayfası | Başlık + çözüm zorunlu, kodlar `^[A-Za-z0-9_.\-]{1,64}$` | `Manual` |

- Hata kodları büyük harfle saklanır (`ix_errorcodes_updatedat`); tüm metin `SensitiveDataMasker`'dan geçer.
- **İlgili vakalar** (`AssistantAnswer.RelatedCases`) backend'de, cevabın doğrulanmış kanıtındaki hata kodlarıyla bulunur
  (sağlayıcıdan bağımsız): açıklayıcı kod eşleşmesi > kod sayısı > ortak servis > yenilik; en fazla 3. Hata kodu yoksa öneri
  yok (yalnız servis eşleşmesi gürültülü). Önerilen vakaların `TimesSuggested` sayacı artar.
- `SearchKnowledgeBase` aracı (en az bir filtre, en fazla 5 sonuç, tarih aralığından bağımsız) sonucu da
  `<untrusted_log_data>` içinde ve enjeksiyon temizliğinden geçerek verilir; vakalar kanıt listesine ve servis doğrulama
  kümesine girmez, güven puanını etkilemez.

## Araç kullanmadan cevap koruması
Küçük modeller bazen hiç araç çağırmadan tahmin yazar. Orkestratör ilk cevapta hiç araç kullanılmamışsa ve (netleştirme
sorusu yoksa **veya** soru incelenebilir bir tanımlayıcı içeriyorsa: kullanıcı/hata kodu, `COR-`/`TRX-`, `…Service`)
modeli **bir kez** "önce araçlarla incele" diye geri gönderir. Gerçekten belirsiz sorularda ("Can you help me?")
netleştirme sorusu korunur.

## Prompt injection ve gizlilik
- Log içeriği (mesaj, istisna metni, metadata) **güvenilmez veridir**. LLM'e `<untrusted_log_data>` içinde ve JSON olarak verilir; sistem istemi bunun talimat olmadığını söyler.
- `PromptInjectionGuard`, talimat gibi görünen kalıpları JSON'un **string değerlerinde** temizler (JSON yapısı bozulmaz) ve sayar; sayı cevapta "sınırlılık" olarak gösterilir.
- Asıl koruma mimaridir: araçlar salt-okunurdur, çıktı doğrulanır; kötü niyetli log bir aksiyon tetikleyemez, kanıt/güven/çözüldü iddiasını değiştiremez.
- `SensitiveDataMasker`: JWT, bearer token, `password=`/`token=` değerleri, e-posta, telefon, kart numarası, uzun sayılar LLM'e **ve** kullanıcıya gitmeden maskelenir (COR-/TRX-/USR- kimlikleri bozulmaz).

## Sağlayıcı soyutlaması
`ILlmProvider.CompleteAsync(LlmRequest)` → araç çağrıları **veya** nihai metin. Orkestratör, doğrulama ve güven mantığı
sağlayıcıdan bağımsızdır; üç sağlayıcı var, `Ai:Provider` (Docker/`.env`: `LOGPILOT_AI_PROVIDER`) ile seçilir:

- **`Mock`** (varsayılan): API anahtarı gerekmez, kural tabanlıdır (soru → araç → şablonlu JSON cevap), deterministiktir ve
  değerlendirme testlerinin temelidir.
- **`Claude`** (`ClaudeLlmProvider`): Anthropic'in resmi C# SDK'sı (`Anthropic` NuGet), Messages API + araç kullanımı.
  - Model `Ai:Claude:Model` (varsayılan `claude-opus-5-5`); düşünme her zaman açık, derinliği `ChatEffort` (varsayılan
    `medium`) ve `SummaryEffort` (`low`) belirler.
  - Araç döngüsünde Claude'un asistan blokları (düşünce blokları dahil) `LlmMessage.ProviderState` içinde taşınır ve bir
    sonraki istekte **değiştirilmeden** geri gönderilir. Araç sonuçları tek bir user mesajında toplanır.
  - Önbellek: son araç tanımına ve konuşma önekine `cache_control` konur; aynı turdaki araç döngüsü öneki yeniden kullanır.
    Kullanım (giriş/çıkış/önbellek token'ları) her çağrıda loglanır.
  - Red (`stop_reason: refusal`): sunucu tarafı yedek model (`fallbacks: "default"`) varsayılan açık
    (`UseRefusalFallback`); yine de reddedilirse sabit, kanıtsız bir cevap döner.
  - Hatalar (`401`, `429`, `5xx`, ağ) `LlmUnavailableException`'a çevrilir: sohbet kullanıcıya nedeni söyleyen bir cevap
    yazar (500 yok); incident özetini elle yenileme 503 döner. Ayrıntı ve anahtar asla kullanıcıya gitmez.
  - Anahtar yalnızca `ANTHROPIC_API_KEY` ortam değişkeninden (`.env`) okunur; `Provider=Claude` iken anahtar yoksa API
    **açılışta** net bir hatayla durur.

- **`Ollama`** (`OllamaLlmProvider`): bilgisayarda çalışan yerel model, ücretsiz; `/api/chat` + araç çağırma.
  - `Ai:Ollama:Model` (varsayılan `qwen3:8b`, araç destekli olmalı), `BaseUrl` (Docker'dan `host.docker.internal:11434`),
    `ContextTokens` (16384; araç sonuçları sığsın diye Ollama varsayılanından büyük), `Think` (kapalı = hızlı).
  - Araç çağrılarının kimliği yoksa üretilir; `<think>` blokları cevaptan silinir; araçsız son turda `format: json` istenir.
  - Ollama kapalı / model inik değil / zaman aşımı → `LlmUnavailableException` ("ollama serve", "ollama pull …" önerisiyle).
- Otomatik incident özeti `Incidents:AutoSummarize` ile kapatılabilir (yerel modelde CPU/RAM, Claude'da maliyet için);
  elle "Regenerate" her zaman çalışır.

Başka bir model eklemek için: `ILlmProvider` uygula, `AddAi` içinde kaydet, `Ai:Provider` değerini ekle.

## Değerlendirme (evaluation) testleri
`tests/LogPilot.UnitTests/Evaluation`: gerçek senaryo dosyalarından üretilen deterministik loglar + gerçek araçlar + mock sağlayıcıyla
uçtan uca sorular çalıştırılır ve şu **değişmezler** denetlenir: her kanıt ID'si gerçek bir log; her servis/hata kodu veride var;
veri yokken `Insufficient`; seçilen tarih aralığına uyum; log içindeki enjeksiyon metni cevabı değiştirmez; belirsiz soruda
araç çağrılmadan netleştirme sorusu.

## Sınırlamalar
- Mock sağlayıcı anahtar kelime tabanlıdır (TR/EN); Claude daha esnek anlar ama aynı doğrulama hattından geçer.
- Ollama'nın küçük modelleri Claude'dan zayıftır: JSON biçimini bozabilir (bir kez yeniden denenir) ve daha sık "yetersiz kanıt" der.
- Claude ve Ollama cevapları deterministik değildir; değerlendirme testleri Mock ile çalışır. İstek biçimleri `ClaudeProviderTests` / `OllamaProviderTests` ile (ağsız) doğrulanır.
- Her sohbet turu birkaç model çağrısı yapar (araç turları); maliyet ve süre `ChatEffort` ile ayarlanır.
- Maskeleme kalıp tabanlıdır; her olası gizli bilgiyi yakalayamaz.
- Kimlik doğrulama yok: araçlar kullanıcı bazlı yetkiyi değil, tarih ve alan sınırlarını uygular.

## LLM sorgu yazmaz: araç seçer

Model hiçbir zaman MongoDB sorgusu, JOIN veya filtre ifadesi üretmez. Akış her zaman şöyledir:

1. Model, tanımlı araçlardan birini ve **JSON argümanlarını** seçer (ör. `TraceNotificationDelivery {"quoteId":"TKL-…"}`).
2. Backend argümanları katı şemayla doğrular (tanımsız alan = hata, kimlikler regex'le sınırlı, tarih aralığı kullanıcının
   seçtiği pencereyle kırpılır, sonuç sayısı sınırlı).
3. Sorguları **önceden yazılmış, salt-okunur repository metotları** çalıştırır; tablolar arası eşleştirme (kimlik no →
   telefon gibi) de backend'de yapılır. PII modele hiç gitmez.
4. Sonuç maskelenip `<untrusted_log_data>` içinde modele döner; model yalnızca bunu **yorumlayıp** JSON cevap yazar.
5. `AnswerBuilder` cevabı doğrular: var olmayan log id'leri, desteklenmeyen kök neden ve kanıtsız "çözüldü" iddiaları atılır.

## Kapsam koruması (konu dışı ve argo sorular)

Asistanın tek işi platformun loglarını inceleyip sorun araştırmaktır. İki savunma hattı vardır:

| Hat | Nerede | Ne yapar |
|---|---|---|
| 1. `ScopeGuard` | `ChatOrchestrator` başında, LLM'den **önce**, deterministik | Argo/hakaret → `Abusive`; açık konu dışı kalıp (genel sigorta danışmanlığı, şirket/maaş/CEO, hava, spor, fıkra, kod yazma…) ve **güçlü bir log/sorun sinyali yoksa** → `OffTopic`. Model çağrılmaz, maliyet yok. |
| 2. Prompt kuralı 12 | LLM | Belirsiz kalan sorularda model `"outOfScope": true` döner; platform standart ret metnini kullanır (modelin yazdığı metin kullanılmaz). |

Tasarım ilkeleri:
- **Yanlış ret, yanlış kabulden kötüdür.** Önce alan sinyali aranır: "Kasko teklifi neden **gelmiyor**?" görev alanındadır,
  "Kasko **nedir**?" değildir. Somut bir kimlik (COR-/TKL-/POL-/CUS-…) varsa soru asla konu dışı sayılmaz.
- Argo, içinde bir log sorusu olsa bile reddedilir; kullanıcı soruyu kibarca tekrar sorabilir.
- Türkçe normalizasyon (ş→s, ı→i…), basit leetspeak (`s1kt1r`) ve harf tekrarı (`salaaak`) çözülür. "sık sık" ve "şikayet"
  gibi masum kelimeler yanlışlıkla yakalanmasın diye riskli kısa kökler listede bilinçli olarak yoktur.
- Selamlaşma ve muğlak istekler ("Merhaba", "Can you help me?") reddedilmez; model netleştirme sorusu sorar.
- Ret metni sorunun dilinde (TR/EN) döner; cevapta `outOfScope: "OffTopic" | "Abusive"` alanı bulunur ve arayüz kanıt/güven
  bölümleri yerine yalnızca bu mesajı gösterir.
- Testler: tüm playbook örnek soruları korumadan geçmeli (`ScopeGuardTests`), konu dışı/argo örnekler reddedilmeli, ret
  durumunda model hiç çağrılmamalı (`ChatOrchestratorTests`).

## Roller ve yetkiler: AI salt-okunur bir analisttir

AI'ın veri değiştirememesi tek bir yere (prompt'a) bırakılmadı; dört bağımsız katmanda uygulanır. Bir katman hata
yapsa bile diğerleri tutar.

| Katman | Nerede | Ne sağlar | Nasıl test edilir |
|---|---|---|---|
| 1. Araç listesi | `ToolRegistry` | Modelin çağırabileceği her araç bir okuma işlemidir (`Get/Search/Find/Trace`). Yazma/silme aracı yoktur. | `RoleAndPrivacyTests` |
| 2. Kod | `LogAnalysisTools` | Bağımlılıkları yalnızca okuma arayüzleridir (`ILogReader`, `IQuoteLogReader`, `ISmsLogReader`, `IUserReader`, `IIncidentReader`, `IKnowledgeReader`). Yazma metodu derleme zamanında erişilemez. | Reflection testi: bu arayüzlerde Add/Update/Delete… adlı metot olamaz |
| 3. Veritabanı | MongoDB rolü `logpilotAiReader` | AI'ın kullandığı hesap yalnızca `find` yetkisine sahiptir ve yalnızca `logs, quote_logs, sms_logs, users, incidents, knowledge_cases` koleksiyonlarını görür. Sohbet kayıtlarını bile okuyamaz. Kod "zorla" yazmaya çalışsa MongoDB `Unauthorized` döner. | `MongoRoleTests`: `--auth` ile geçici bir mongod başlatılır; insert/update/delete/drop reddedilir |
| 4. Konuşma | `ScopeGuard` + prompt "ROLE" bölümü | "BOB'un loglarını sil", "incident'ı kapat", "veritabanını sıfırla", "delete all logs" gibi talepler modele gitmeden reddedilir: *"Bu işlemi yapma yetkim yok. Ben salt-okunur bir analiz asistanıyım…"*. Model de böyle bir talep görürse `notPermitted: true` döner; "sildim" demesi imkânsızdır, metni platform yazar. | `RoleAndPrivacyTests`, `ChatOrchestratorTests` |

Açılışta `MongoAccessVerifier`, AI hesabının gerçek yetkilerini MongoDB'ye sorar (`connectionStatus`, veri değiştirmez):
- Yazma yetkisi görürse `Mongo:RequireReadOnlyAiAccess=true` iken API **açılmaz** (fail closed), değilse kritik log yazar.
- MongoDB'de kimlik doğrulaması kapalıysa uyarır: o durumda salt-okunurluk yalnızca 1., 2. ve 4. katmanda vardır.

Hesaplar (`docker-compose.secure.yml` + `docker/mongo-init/01-logpilot-roles.js`):

| Hesap | Rol | Kim kullanır |
|---|---|---|
| `root` | root | Yalnızca kurulum; uygulama kullanmaz |
| `logpilot_app` | readWrite + yalnız `collMod` | Generator (log yazar), API (sohbet, incident, vaka hafızası) |
| `logpilot_ai_reader` | `logpilotAiReader` (yalnız `find`) | AI araçları ve dashboard analitiği |

```bash
./scripts/docker-down.sh --wipe   # roller yalnızca boş veri klasöründe kurulur (ilk geçişte bir kez)
docker compose -f docker-compose.yml -f docker-compose.secure.yml up -d --build --wait
```
Parolalar `.env` dosyasından gelir (`LOGPILOT_MONGO_ROOT_PASSWORD`, `LOGPILOT_APP_PASSWORD`, `LOGPILOT_AI_READER_PASSWORD`).

## KVKK: kişisel veri modele ve cevaba nasıl gitmez?

| Veri | Ne yapılır |
|---|---|
| T.C. kimlik no (`identityNo`) | Sadece backend eşleştirmede kullanır; modele ve cevaba `*********95` olarak çıkar |
| Telefon | Aynı; `+90******4729` |
| Plaka, IBAN, kart, e-posta, telefon, 10–13 haneli sayılar, token/parola | Log metinlerinden ve metadata'dan `SensitiveDataMasker` ile `[MASKED_PLATE]`, `[MASKED_IBAN]`… olarak silinir |
| Kullanıcının sohbete yazdığı kişisel veri | Soru **veritabanına kaydedilmeden ve modele gitmeden önce** maskelenir (`ChatService` + `ChatOrchestrator`) |
| Kişilere atıf | Yalnızca takma kimliklerle: `userCode`, `customerNo` |

Ek güvenceler:
- Prompt'ta: "Maskeli değerleri tahmin etmeye, yeniden kurmaya veya istemeye çalışma."
- `AnswerBuilder` cevabın tüm metin alanlarını son kez maskeler.
- `No_personal_data_leaks_into_any_answer_across_all_scenarios`: tüm senaryolar üretilir, her müşteri için teklif, SMS ve
  giriş soruları sorulur; hiçbir cevapta gerçek kimlik no, telefon veya plaka bulunmamalıdır.
- Ollama yerelde çalıştığında veri makineden çıkmaz. Claude seçilirse modele giden içerik zaten maskelidir, ama bu bir
  yurt dışı aktarım sayılabilir: hukuk/KVKK değerlendirmesi kurumun kararıdır.
- Sınır: serbest metindeki **isim-soyisim** otomatik tanınmaz (logların isim yazmaması gerekir). Logs Explorer ekranı
  yetkili personel içindir ve ham logu gösterir; o ekranın erişim yetkisi ayrı bir konudur.
