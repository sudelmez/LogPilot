#!/usr/bin/env bash
# LogPilot konteynerlerini ve (LogPilot başlattıysa) Ollama'yı durdurur. MongoDB verisi korunur.
# Kullanım: ./scripts/docker-down.sh          (durdur)
#           ./scripts/docker-down.sh --wipe   (durdur + MongoDB verisini SİL; sonraki açılışta temiz başlar)
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ "${1:-}" == "--wipe" ]]; then
  read -r -p "Docker'daki LogPilot veritabanı silinecek. Devam edilsin mi? [e/H] (e = evet, y = yes) " answer
  [[ "$answer" =~ ^([EeYy]|[Ee]vet|[Yy]es)$ ]] || { echo "İptal edildi (silmek için 'e' yazın)."; exit 0; }
  docker compose down -v
  echo "Durduruldu ve veri silindi."
else
  docker compose down
  echo "Durduruldu. Veri korunuyor; tekrar başlatmak için start.command."
fi

# LogPilot'un açtığı Ollama'yı da kapat (model belleği boşalır). Elle açılmış bir Ollama'ya dokunmaz.
./scripts/ollama-service.sh stop
