#!/usr/bin/env bash
# LogPilot Faz 2 kurulumu. Proje kökünde (~/Desktop/LogPilot) çalıştır:  bash apply-phase2.sh
set -euo pipefail
cd "$(dirname "$0")"

if [ ! -f LogPilot.slnx ] && [ ! -f LogPilot.sln ]; then
  echo "HATA: Bu script LogPilot proje kökünde çalıştırılmalı (LogPilot.slnx bulunamadı)." >&2
  exit 1
fi
if ! command -v dotnet >/dev/null || ! dotnet --version | grep -q '^10\.'; then
  echo "HATA: .NET 10 SDK gerekli." >&2
  exit 1
fi

# 1) LogGenerator projesini oluştur (sadece ilk çalıştırmada)
if [ ! -d src/LogPilot.LogGenerator ]; then
  dotnet new worker -o src/LogPilot.LogGenerator
  dotnet sln add src/LogPilot.LogGenerator
  dotnet add src/LogPilot.LogGenerator reference src/LogPilot.Application src/LogPilot.Infrastructure
  dotnet add tests/LogPilot.UnitTests reference src/LogPilot.LogGenerator
  rm -f src/LogPilot.LogGenerator/Worker.cs
fi

# 2) Faz 1'in geçici örnek log seeder'ını kaldır
rm -f src/LogPilot.Api/Development/SampleLogSeeder.cs
rmdir src/LogPilot.Api/Development 2>/dev/null || true

# 3) Faz 2 dosyalarını yerleştir (değişen Faz 1 dosyalarının üzerine yazar)
cp -R _overlay2/. .
rm -rf _overlay2
chmod +x scripts/*.sh

echo "== Derleme ve test =="
dotnet build
dotnet test

cat <<'MSG'

Hazır. Sıradaki adımlar:
  ./scripts/reset-database.sh -y     # Faz 1'den kalan örnek logları temizler (API/generator kapalıyken)
  ./scripts/seed-data.sh 120         # kullanıcılar + senaryolar + son 120 dakikanın logları
  ./scripts/start-local.sh           # API + LogGenerator
MSG
