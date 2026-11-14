// LogPilot MongoDB rolleri.
//  • İlk kurulumda (boş veri klasörü) mongo imajı bu betiği otomatik çalıştırır.
//  • Mevcut kurulumda rolleri güncellemek için (yeni tablo eklendiğinde): ./scripts/mongo-update-roles.sh
// Betik tekrar çalıştırılabilir: roller varsa güncellenir, kullanıcılar varsa dokunulmaz.
//
//   logpilot_app        → readWrite + collMod : generator log yazar; API sohbet, incident ve vaka hafızası yazar
//   logpilot_ai_reader  → logpilotAiReader    : SADECE 'find', SADECE aşağıdaki koleksiyonlarda. Silme/güncelleme/ekleme YOK.
//
// Koleksiyon listesi MongoAccessPolicy.AiReadableCollections ile AYNI olmalı (bir test bunu denetler).
const dbName = process.env.LOGPILOT_DB || "logpilot";
const target = db.getSiblingDB(dbName);
const aiReadable = ["logs", "quote_logs", "sms_logs", "users", "incidents", "knowledge_cases", "proposal_step_logs", "winsure_transactions", "payment_transactions"];

function upsertRole(name, privileges) {
  if (target.getRole(name)) target.updateRole(name, { privileges, roles: [] });
  else target.createRole({ role: name, privileges, roles: [] });
}

function ensureUser(user, pwd, roles) {
  if (!user || !pwd) return;
  if (!target.getUser(user)) target.createUser({ user, pwd, roles });
}

upsertRole("logpilotAiReader", aiReadable.map(c => ({ resource: { db: dbName, collection: c }, actions: ["find"] })));
// Uygulama açılışta TTL süresini değiştirirse collMod gerekir; dbAdmin (drop yetkisi de verir) yerine dar bir rol.
upsertRole("logpilotIndexMaintenance", [{ resource: { db: dbName, collection: "" }, actions: ["collMod"] }]);

ensureUser(process.env.LOGPILOT_APP_USER, process.env.LOGPILOT_APP_PASSWORD, [{ role: "readWrite", db: dbName }, "logpilotIndexMaintenance"]);
ensureUser(process.env.LOGPILOT_AI_READER_USER, process.env.LOGPILOT_AI_READER_PASSWORD, ["logpilotAiReader"]);

print(`LogPilot roles ready in '${dbName}': logpilot_app (readWrite), ai reader (find only on ${aiReadable.join(", ")}).`);
