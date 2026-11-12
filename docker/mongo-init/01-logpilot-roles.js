// LogPilot MongoDB rolleri — yalnızca BOŞ bir veri klasöründe (ilk kurulumda) mongo imajı tarafından çalıştırılır.
// Mevcut kurulumda uygulamak için: ./scripts/docker-down.sh --wipe && docker compose -f docker-compose.yml -f docker-compose.secure.yml up -d
//
//   logpilot_app        → readWrite  : generator log yazar; API sohbet, incident ve vaka hafızası yazar
//   logpilot_ai_reader  → logpilotAiReader : SADECE 'find', SADECE aşağıdaki koleksiyonlarda. Silme/güncelleme/ekleme YOK.
//
// Koleksiyon listesi MongoAccessPolicy.AiReadableCollections ile AYNI olmalı (bir test bunu denetler).
const dbName = process.env.LOGPILOT_DB || "logpilot";
const target = db.getSiblingDB(dbName);
const aiReadable = ["logs", "quote_logs", "sms_logs", "users", "incidents", "knowledge_cases"];

target.createRole({
  role: "logpilotAiReader",
  privileges: aiReadable.map(c => ({ resource: { db: dbName, collection: c }, actions: ["find"] })),
  roles: []
});

// Uygulama açılışta TTL süresini değiştirirse collMod gerekir; dbAdmin (drop yetkisi de verir) yerine dar bir rol.
target.createRole({
  role: "logpilotIndexMaintenance",
  privileges: [{ resource: { db: dbName, collection: "" }, actions: ["collMod"] }],
  roles: []
});

target.createUser({
  user: process.env.LOGPILOT_APP_USER,
  pwd: process.env.LOGPILOT_APP_PASSWORD,
  roles: [{ role: "readWrite", db: dbName }, "logpilotIndexMaintenance"]
});

target.createUser({
  user: process.env.LOGPILOT_AI_READER_USER,
  pwd: process.env.LOGPILOT_AI_READER_PASSWORD,
  roles: ["logpilotAiReader"]
});

print(`LogPilot roles created in '${dbName}': logpilot_app (readWrite), ai reader (find only on ${aiReadable.join(", ")}).`);
