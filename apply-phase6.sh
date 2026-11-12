#!/usr/bin/env bash
# LogPilot Faz 6: incident yönetimi (kural tabanlı tespit, API, AI özetleri, arayüz).
# Proje kökünde çalıştır:  bash apply-phase6.sh
set -euo pipefail
cd "$(dirname "$0")"

[ -d src/LogPilot.AI ] || { echo "HATA: Önce Faz 5 uygulanmalı." >&2; exit 1; }

# Application artık ILogger kullanıyor
if ! grep -q "Microsoft.Extensions.Logging.Abstractions" src/LogPilot.Application/LogPilot.Application.csproj; then
  dotnet add src/LogPilot.Application package Microsoft.Extensions.Logging.Abstractions
fi

cp -R _overlay6/. .
rm -rf _overlay6

echo "== Backend: derleme ve test =="
dotnet build
dotnet test

echo "== Frontend: tip kontrolü =="
( cd frontend/logpilot-ui && npm run build )

cat <<'MSG'

Faz 6 hazır. Denemek için:
  ./scripts/start-local.sh && ./scripts/start-ui.sh
  # Bir incident üretmek: MongoDB kesintisi senaryosunu tetikle (3+ log gerekir; kullanıcı sayısı 4-8)
  curl -X POST localhost:5080/api/scenarios/mongo-outage/trigger
  # ~30 sn içinde:
  curl "localhost:5080/api/incidents?activeOnly=true"
MSG
