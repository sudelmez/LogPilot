#!/usr/bin/env bash
# Kullanıcıları ve senaryo kataloğunu yükler, ardından geçmişe dönük normal + hata logları üretir.
# Kullanım: ./scripts/seed-data.sh [dakika]     (varsayılan 60)
# Not: Tekrar çalıştırırsan loglar çoğalır; temiz başlamak için önce reset-database.sh çalıştır.
set -euo pipefail
cd "$(dirname "$0")/.."
MINUTES="${1:-60}"
dotnet run --project src/LogPilot.LogGenerator -- --seed-only --backfill-minutes "$MINUTES"
