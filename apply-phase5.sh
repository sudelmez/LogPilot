#!/usr/bin/env bash
# LogPilot Faz 5: AI asistanı (provider soyutlaması, mock sağlayıcı, araç çağırma, sohbet geçmişi, UI).
# Proje kökünde çalıştır:  bash apply-phase5.sh
set -euo pipefail
cd "$(dirname "$0")"

[ -f src/LogPilot.Api/Endpoints/AnalyticsEndpoints.cs ] || { echo "HATA: Önce Faz 3 uygulanmalı." >&2; exit 1; }
[ -d frontend/logpilot-ui ] || { echo "HATA: Önce Faz 4 uygulanmalı." >&2; exit 1; }

# 1) LogPilot.AI projesi (sadece ilk çalıştırmada)
if [ ! -d src/LogPilot.AI ]; then
  dotnet new classlib -o src/LogPilot.AI
  rm -f src/LogPilot.AI/Class1.cs
  dotnet sln add src/LogPilot.AI
  dotnet add src/LogPilot.AI reference src/LogPilot.Application
  dotnet add src/LogPilot.AI package Microsoft.Extensions.DependencyInjection.Abstractions
  dotnet add src/LogPilot.AI package Microsoft.Extensions.Logging.Abstractions
  dotnet add src/LogPilot.AI package Microsoft.Extensions.Options.ConfigurationExtensions
  dotnet add src/LogPilot.Api reference src/LogPilot.AI
  dotnet add tests/LogPilot.UnitTests reference src/LogPilot.AI
fi

# 2) Dosyaları yerleştir
cp -R _overlay5/. .
rm -rf _overlay5

echo "== Backend: derleme ve test =="
dotnet build
dotnet test

echo "== Frontend: tip kontrolü =="
( cd frontend/logpilot-ui && npm run build )

cat <<'MSG'

Faz 5 hazır. Dene:
  ./scripts/start-local.sh      # API + generator
  ./scripts/start-ui.sh         # AI Assistant sayfası: http://localhost:5173/assistant

  # veya API'den:
  SID=$(curl -s -X POST localhost:5080/api/chat/sessions | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")
  curl -s -X POST localhost:5080/api/chat/sessions/$SID/messages -H 'Content-Type: application/json' \
       -d '{"content":"Why can'"'"'t TCLI log in to the mobile application?"}'
MSG
