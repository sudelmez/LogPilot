# Kurumsal Web Poliçe Satın Alma: Dağıtık Loglar

> Gerçek şirket sistemlerindeki gibi, bir satın almanın izi **üç ayrı tabloya** dağılmıştır ve her tablo farklı bir
> anahtar bilir. Asistan, “hata poliçe onayında mı, ödemede mi?” sorusunu ancak bu tablolar arasında **sırayla**
> gezerek cevaplayabilir. Tüm veriler sentetiktir.

## 1. Tablolar ve anahtarları

| Tablo | Sistem | Bildiği anahtar | Dikkat |
|---|---|---|---|
| `proposal_step_logs` | Kurumsal web sitesi (`ProposalService`) | **yalnız** `userIdentityNo` (+ `proposalNo` PRP-…) | `policyNo` ayrı bir alan **değildir**; yalnızca `PolicyCreate` adımının **response JSON**'unun içinde geçer |
| `winsure_transactions` | Poliçe çekirdek sistemi (Winsure) | **yalnız** `policyNo` | Create / Approve / Edit / Cancel / Print; request/response **XML**; hata mesajı XML'deki `<Message>` |
| `payment_transactions` | Sanal POS (VPOS_A, VPOS_B) | `policyNo` + `clientReferenceCode` (PAY-…) | **Yalnız `Initiated` kaydında** `policyNo` vardır. Sağlayıcı sonucu ve iade kayıtları **aynı `clientReferenceCode` ile ama `policyNo` BOŞ** yazılır |
| `users` | Kullanıcı dizini | `userCode` ↔ `identityNo` | Kimlik no dışarıya maskeli çıkar |

Satın alma adımları: `Start → CustomerInfo → OfferSelected → PolicyCreate → Payment → PaymentResult → PolicyApprove → Completed`.

**`PURCHASE_FAILED` yalnızca bir belirtidir.** Web sitesinin müşteriye gösterdiği genel mesajdır
(“Ödeme işlemi tamamlanamadı.”, “Poliçeniz şu anda onaylanamadı…”). Asıl neden her zaman Winsure'da veya ödeme
kayıtlarındadır.

## 2. Zincir: hangi anahtarla hangi tabloya

```
users ──identityNo──► proposal_step_logs ──policyNo (response JSON içinden)──► winsure_transactions
                                                                         └──► payment_transactions (policyNo → Initiated)
                                                                                 └─clientReferenceCode─► payment_transactions
                                                                                                         (sonuç / iade, policyNo yok)
```

Elde ne olduğuna göre sıra değişir:

| Başlangıç | Sıra |
|---|---|
| Kişi (`userCode` / `customerNo`) | users → proposal_step_logs → winsure_transactions → payment_transactions (policyNo) → payment_transactions (clientReferenceCode) |
| `policyNo` (POL-…) | winsure_transactions → payment_transactions (policyNo) → payment_transactions (clientReferenceCode) → proposal_step_logs (response JSON'da policyNo) → users |
| `proposalNo` (PRP-…) | proposal_step_logs → users → winsure_transactions → payment_transactions → payment_transactions |

Kişiyle başlanınca **aralıktaki en son hatalı satın alma** seçilir (yoksa en son satın alma); deneme sayısı ayrıca
raporlanır. Bir halka koparsa (kimlik dizinde yok, `PolicyCreate`'e hiç gelinmemiş, ödeme başlatılmamış) zincir orada
durur ve bu da bir bulgudur.

## 3. Araçlar (AI)

| Araç | Ne yapar |
|---|---|
| `TracePolicyPurchase` | Yukarıdaki zinciri tek çağrıda yürütür; her halkayı (`TraceHop`), kararı, hatanın gerçekten oluştuğu aşamayı ve **müşteriden ücret alınıp alınmadığını** döner |
| `SearchProposalSteps` | Adımlar (kişi verilirse kimlik no backend'de çözülür; `policyNo` response içinde aranır) |
| `SearchWinsureTransactions` | Winsure işlemleri (`policyNo`, işlem, yalnız başarısızlar) |
| `SearchPaymentTransactions` | Ödemeler; açıklaması modele “önce policyNo, sonra clientReferenceCode ile tekrar ara” der |
| `GetPurchaseStats` | Toplu: başlayan / oluşturulan / ödenen / onaylanan / iptal; hatalar tablo ve aşamaya göre (belirti en sona) |

Yönlendirme: playbook **P9** (“satın”, “ödeme”, “onay”, “iptal”, “iade”, “POL-”, “PRP-”, “PAY-” …). SMS/bildirim
soruları P6'da kalır (öncelik daha yüksek).

## 4. Kararlar

| Karar | Anlamı | Asıl neden nerede | Ücret alındı mı |
|---|---|---|---|
| `Completed` | Ödendi, Winsure onayladı | – | Evet (normal) |
| `Cancelled` | Satın alındı, sonra Winsure'da iptal | winsure (Cancel) + payment (Refund) | İade edildiyse hayır |
| `PaymentFailed` | Kart reddi (51) veya 3D Secure başarısız | payment_transactions, **policyNo'suz** sağlayıcı kaydı | Hayır |
| `PaymentPending` | Ödeme başlatıldı, sağlayıcıdan sonuç gelmedi | payment_transactions'ta yalnız `Initiated` var | Bilinmiyor (mutabakat gerekir) |
| `ApproveFailedAfterPayment` | Ödeme alındı ama Winsure onaylamadı (açık hasar dosyası, WS-4012) | winsure_transactions (Approve) | **Evet: iade veya manuel onay gerekir** |
| `PolicyCreateFailed` | Winsure poliçeyi oluştururken zaman aşımı | winsure_transactions (Create) | Hayır (ödemeye geçilmedi) |
| `AbandonedBeforePolicy` / `AbandonedBeforePayment` | Kullanıcı yarıda bıraktı | proposal_step_logs | Hayır |
| `Unknown` | Zincir koptu (kimlik dizinde yok, kayıt yok) | – | – |

Hata kodları: `PAYMENT_DECLINED`, `PAYMENT_3DS_FAILED` müşteri tarafı; `WINSURE_APPROVE_REJECTED` iş kuralı;
`WINSURE_TIMEOUT`, `PAYMENT_CALLBACK_TIMEOUT` teknik; `PURCHASE_FAILED` belirti.

## 5. Senaryolar

| Dosya | Tür | Sonuç |
|---|---|---|
| `normal-corporate-purchase` | Normal | Completed |
| `normal-corporate-purchase-cancelled` | Normal | Cancelled + iade |
| `purchase-payment-declined` | Incident | PaymentFailed (kod 51) |
| `purchase-3ds-failed` | Incident | PaymentFailed (3D Secure) |
| `purchase-payment-no-callback` | Incident | PaymentPending |
| `purchase-approve-rejected` | Incident | ApproveFailedAfterPayment |
| `purchase-winsure-create-timeout` | Incident | PolicyCreateFailed |

Senaryo dosyalarında `steps` boştur; akışı `purchase` bloğu (`outcome`, `products`, `provider`) belirler ve
`PurchaseFlowBuilder` üç tabloya dağıtık kayıt üretir. Generator açılışta yalnızca **boş olan** tablo gruplarını
geçmişe dönük doldurur; mevcut tablolar çoğalmaz.

## 6. Yetki (güvenli mod)

AI'ın salt-okunur MongoDB hesabı (`logpilot_ai_reader`) yeni tabloları da yalnızca `find` ile okuyabilir.
Roller ilk kurulumda `docker/mongo-init/01-logpilot-roles.js` ile oluşur. **Mevcut** bir kurulumda yeni tablo
eklendiğinde rolleri güncellemek için (veri silmez):

```bash
./scripts/mongo-update-roles.sh
```

```bash
docker compose restart api
```

API açılışta AI hesabının yetkilerini tekrar doğrular. Konuşma tarafında “poliçeyi iptal et”, “ödemeyi iade et”,
“poliçeyi onayla” gibi talepler modele gitmeden reddedilir (salt-okunur rol).

## 7. KVKK

Winsure request XML'i ve `CustomerInfo` request JSON'u kimlik numarası içerir. Bu ham alanlar modele gönderilmez;
kanıtlarda yalnızca maskeli kısa bir response parçası yer alır. Log detay ekranı (yetkili personel) ham XML/JSON'u
maskeleyerek gösterir. Testler, satın alma cevaplarında kimlik no ve telefonun hiç görünmediğini doğrular.
