#!/usr/bin/env bash
# Güvenli moddaki (docker-compose.secure.yml) MongoDB'de rolleri günceller: yeni bir log tablosu eklendiğinde
# AI okuyucu hesabının o tabloyu da okuyabilmesi için. Veri silmez; mevcut kullanıcılara dokunmaz.
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose exec -T mongo sh -c \
  'mongosh --quiet -u "$MONGO_INITDB_ROOT_USERNAME" -p "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin /docker-entrypoint-initdb.d/01-logpilot-roles.js'
echo "Roller güncellendi. API'yi yeniden başlatınca AI hesabının yetkileri tekrar doğrulanır: docker compose restart api"
