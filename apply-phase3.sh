#!/usr/bin/env bash
# LogPilot Faz 3: log endpoint'leri + analytics. Proje kökünde çalıştır:  bash apply-phase3.sh
set -euo pipefail
cd "$(dirname "$0")"
[ -d src/LogPilot.LogGenerator ] || { echo "HATA: Önce Faz 2 uygulanmalı." >&2; exit 1; }

cp -R _overlay3/. .
rm -rf _overlay3

echo "== Derleme ve test =="
dotnet build
dotnet test

cat <<'MSG'

Faz 3 hazır. API'yi başlatıp dene:
  curl "http://localhost:5080/api/analytics/summary"
  curl "http://localhost:5080/api/analytics/error-trend?bucketMinutes=15"
  curl "http://localhost:5080/api/analytics/errors-by-service"
  curl "http://localhost:5080/api/analytics/top-error-codes?limit=5"
  curl "http://localhost:5080/api/analytics/service-health"
  curl "http://localhost:5080/api/logs/recent-errors?limit=5"
  curl "http://localhost:5080/api/logs/by-user/TCLI"
MSG
