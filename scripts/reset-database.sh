#!/usr/bin/env bash
# logpilot veritabanını TAMAMEN siler. Önce API ve generator'ı durdur.
# Kullanım: ./scripts/reset-database.sh [-y]
# İndeksler ve senaryo kataloğu bir sonraki başlatmada otomatik yeniden kurulur.
set -euo pipefail

DB="${LOGPILOT_DB:-logpilot}"
URI="${LOGPILOT_MONGO_URI:-mongodb://localhost:27017}"

if ! command -v mongosh >/dev/null 2>&1; then
  echo "mongosh bulunamadı. Kurulum: brew install mongosh" >&2
  exit 1
fi

if [[ "${1:-}" != "-y" ]]; then
  read -r -p "'$DB' veritabanı silinecek. Devam edilsin mi? [y/N] " answer
  [[ "$answer" =~ ^[Yy]$ ]] || { echo "İptal edildi."; exit 0; }
fi

mongosh "$URI/$DB" --quiet --eval "db.dropDatabase()"
echo "'$DB' silindi. Sıradaki adım: ./scripts/seed-data.sh 120"
