#!/usr/bin/env bash
# LogPilot Faz 4: React arayüzü (Dashboard, Logs Explorer, Scenarios) + senaryo ayarları API'si.
# Proje kökünde çalıştır:  bash apply-phase4.sh
set -euo pipefail
cd "$(dirname "$0")"

[ -f src/LogPilot.Api/Endpoints/AnalyticsEndpoints.cs ] || { echo "HATA: Önce Faz 3 uygulanmalı." >&2; exit 1; }
command -v node >/dev/null || { echo "HATA: Node.js gerekli (brew install node)." >&2; exit 1; }
if [ "$(node -p "process.versions.node.split('.')[0]")" -lt 20 ]; then
  echo "HATA: Node.js 20 veya üstü gerekli (bulunan: $(node -v))." >&2; exit 1
fi

cp -R _overlay4/. .
rm -rf _overlay4
chmod +x scripts/*.sh

echo "== Backend: derleme ve test =="
dotnet build
dotnet test

echo "== Frontend: bağımlılıklar =="
cd frontend/logpilot-ui
if [ ! -d node_modules ]; then
  npm install react react-dom react-router-dom @mui/material @mui/icons-material @emotion/react @emotion/styled @tanstack/react-query recharts
  npm install -D vite @vitejs/plugin-react typescript @types/react @types/react-dom
fi
echo "== Frontend: tip kontrolü + üretim derlemesi =="
npm run build

cat <<'MSG'

Faz 4 hazır. İki terminalde:
  ./scripts/start-local.sh     # API + generator
  ./scripts/start-ui.sh        # http://localhost:5173
MSG
