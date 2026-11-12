#!/usr/bin/env bash
# LogPilot Faz 7: kalite ve dağıtım (entegrasyon + AI değerlendirme testleri, sağlık kontrolleri, JSON loglama, Docker, dokümantasyon).
# Proje kökünde çalıştır:  bash apply-phase7.sh
set -euo pipefail
cd "$(dirname "$0")"

[ -f src/LogPilot.Api/Endpoints/IncidentEndpoints.cs ] || { echo "HATA: Önce Faz 6 uygulanmalı." >&2; exit 1; }

# Entegrasyon test projesi (sadece ilk çalıştırmada)
if [ ! -d tests/LogPilot.IntegrationTests/obj ] && [ ! -f tests/LogPilot.IntegrationTests/LogPilot.IntegrationTests.csproj ]; then
  dotnet new xunit -o tests/LogPilot.IntegrationTests
  rm -f tests/LogPilot.IntegrationTests/UnitTest1.cs
  dotnet sln add tests/LogPilot.IntegrationTests
  dotnet add tests/LogPilot.IntegrationTests reference src/LogPilot.Api src/LogPilot.LogGenerator
  dotnet add tests/LogPilot.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
  dotnet add tests/LogPilot.IntegrationTests package AwesomeAssertions
fi

cp -R _overlay7/. .
rm -rf _overlay7

echo "== Derleme =="
dotnet build

echo "== Testler (MongoDB yoksa entegrasyon testleri atlanır) =="
dotnet test

echo "== Frontend =="
( cd frontend/logpilot-ui && npm run build )

cat <<'MSG'

Faz 7 hazır.
  dotnet test                                  # birim + AI değerlendirme + (MongoDB varsa) entegrasyon
  curl localhost:5080/health/ready             # {"status":"Healthy","checks":[{"name":"mongodb",...}]}
  docker compose --profile app up --build      # tüm uygulama Docker'da (UI :5173, API :5080)
MSG
