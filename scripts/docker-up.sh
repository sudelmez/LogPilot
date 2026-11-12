#!/usr/bin/env bash
# LogPilot'u tamamen Docker'da başlatır: MongoDB + API + LogGenerator + UI.
# Docker'ı gerekirse açar, port çakışmalarını kontrol eder, her şey sağlıklı olana kadar bekler, tarayıcıyı açar.
# Kullanım: ./scripts/docker-up.sh [--no-build] [--no-open]
set -euo pipefail
cd "$(dirname "$0")/.."

BUILD="--build"
OPEN_BROWSER=1
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD="" ;;
    --no-open)  OPEN_BROWSER=0 ;;
    *) echo "Bilinmeyen seçenek: $arg" >&2; exit 2 ;;
  esac
done

# .env varsa port ayarlarını oradan al (docker compose da aynı dosyayı okur).
if [[ -f .env ]]; then set -a; source .env; set +a; fi
UI_PORT="${LOGPILOT_UI_PORT:-5173}"
API_PORT="${LOGPILOT_API_PORT:-5080}"

say()  { printf '\033[1;34m▸\033[0m %s\n' "$*"; }
ok()   { printf '\033[1;32m✓\033[0m %s\n' "$*"; }
fail() { printf '\033[1;31m✗ %s\033[0m\n' "$*" >&2; exit 1; }

# macOS'ta `timeout` yok: komutu arka planda çalıştırıp en fazla N saniye bekler.
run_with_timeout() {
  local seconds=$1; shift
  "$@" >/dev/null 2>&1 &
  local pid=$!
  for ((i = 0; i < seconds; i++)); do
    if ! kill -0 "$pid" 2>/dev/null; then wait "$pid"; return $?; fi
    sleep 1
  done
  kill "$pid" 2>/dev/null || true
  return 124
}

docker_ready() { run_with_timeout 10 docker info; }

# Portu Docker dışında bir süreç mi dinliyor? (Docker'ın yayınladığı portları com.docker.* süreçleri tutar.)
port_used_by_other() {
  local owners
  owners=$(lsof -nP -iTCP:"$1" -sTCP:LISTEN -Fc 2>/dev/null | sed -n 's/^c//p' | sort -u || true)
  [[ -z "$owners" ]] && return 1
  grep -qvE '^(com\.docke|com\.docker|vpnkit|docker)' <<<"$owners"
}

# 0) Yapay zekâ ayarı: Claude seçili ama anahtar yoksa API açılışta zaten durur; baştan söyle.
if [[ "$(tr '[:upper:]' '[:lower:]' <<<"${LOGPILOT_AI_PROVIDER:-mock}")" == "claude" && -z "${ANTHROPIC_API_KEY:-}" ]]; then
  fail ".env içinde LOGPILOT_AI_PROVIDER=Claude ama ANTHROPIC_API_KEY boş.
  Anahtarı console.anthropic.com → API Keys'ten alıp .env'e yaz, ya da LOGPILOT_AI_PROVIDER=Mock yap."
fi

# Ollama seçiliyse: Mac'te açık mı ve model indirilmiş mi? (API konteyneri ona host.docker.internal ile ulaşır.)
if [[ "$(tr '[:upper:]' '[:lower:]' <<<"${LOGPILOT_AI_PROVIDER:-mock}")" == "ollama" ]]; then
  OLLAMA_MODEL="${LOGPILOT_OLLAMA_MODEL:-qwen3:8b}"
  command -v ollama >/dev/null 2>&1 || fail "LOGPILOT_AI_PROVIDER=Ollama ama Ollama kurulu değil. Kur: brew install ollama"
  # Yalnızca bu oturum için başlatılır; stop.command / docker-down.sh kapatır.
  curl -sf --max-time 2 http://localhost:11434/api/version >/dev/null || say "Ollama başlatılıyor..."
  ./scripts/ollama-service.sh start || fail "Ollama açılamadı. Elle başlat: ollama serve"
  # "qwen3:8b" → listede "qwen3:8b"; etiketsiz ad ("llama3.1") → "llama3.1:latest"
  [[ "$OLLAMA_MODEL" == *:* ]] || OLLAMA_MODEL="$OLLAMA_MODEL:latest"
  curl -sf http://localhost:11434/api/tags | grep -q "\"name\":\"$OLLAMA_MODEL\"" \
    || fail "Ollama modeli '$OLLAMA_MODEL' indirilmemiş. İndir (birkaç GB): ollama pull $OLLAMA_MODEL"
  ok "Ollama hazır ($OLLAMA_MODEL)"
fi

# 1) Docker
command -v docker >/dev/null 2>&1 || fail "Docker kurulu değil. https://www.docker.com/products/docker-desktop adresinden Docker Desktop'ı kur."

if pgrep -f "/Volumes/Docker/Docker.app" >/dev/null 2>&1; then
  fail "Docker Desktop kurulum diskinden (/Volumes/Docker) çalışıyor; bu durumda yanıt vermiyor ve CPU'yu tüketiyor.
  1) Docker'ı kapat (menü çubuğundaki balina → Quit Docker Desktop; olmuyorsa Activity Monitor'den sonlandır).
  2) Finder'da 'Docker' diskini çıkar (Eject).
  3) Docker'ı Applications klasöründen aç (orada yoksa önce DMG'den Applications'a sürükle), sonra tekrar dene."
fi

if ! docker_ready; then
  say "Docker Desktop başlatılıyor..."
  open -a Docker 2>/dev/null || fail "Docker Desktop açılamadı. Uygulamayı elle başlat ve tekrar dene."
  for ((i = 0; i < 60; i++)); do
    docker_ready && break
    sleep 3
  done
  docker_ready || fail "Docker 3 dakikada hazır olmadı. Docker Desktop'ın açık olduğundan emin ol ve tekrar dene."
fi
ok "Docker hazır"

# 2) Port çakışmaları
port_used_by_other "$UI_PORT"  && fail "Port $UI_PORT başka bir süreç tarafından kullanılıyor (yerel UI açık olabilir: start-ui.sh). Onu kapat veya .env'de LOGPILOT_UI_PORT ayarla."
port_used_by_other "$API_PORT" && fail "Port $API_PORT başka bir süreç tarafından kullanılıyor (yerel API açık olabilir: start-local.sh). Onu kapat veya .env'de LOGPILOT_API_PORT ayarla."

if [[ -z "${LOGPILOT_MONGO_PORT:-}" ]] && port_used_by_other 27017; then
  # Genelde brew ile kurulu MongoDB'dir. Docker'daki MongoDB ayrı bir veritabanıdır; host'ta 27018'den açılır.
  export LOGPILOT_MONGO_PORT=27018
  say "27017 yerel MongoDB tarafından kullanılıyor; Docker MongoDB'si localhost:27018'de açılacak."
fi

# 3) Mod: güvenli kurulum (.env içinde COMPOSE_FILE=...secure.yml) — MongoDB auth + salt-okunur AI hesabı
if [[ "${COMPOSE_FILE:-}" == *secure* ]]; then
  for v in LOGPILOT_MONGO_ROOT_PASSWORD LOGPILOT_APP_PASSWORD LOGPILOT_AI_READER_PASSWORD; do
    [[ -n "${!v:-}" ]] || fail "Güvenli mod açık ama .env içinde $v yok."
  done
  ok "Güvenli mod: MongoDB kimlik doğrulaması açık, AI salt-okunur hesapla bağlanıyor"
fi

# 4) Derle ve başlat
say "Konteynerler derleniyor ve başlatılıyor (ilk seferde birkaç dakika sürebilir)..."
# shellcheck disable=SC2086
if ! docker compose up -d $BUILD --wait --wait-timeout 300; then
  echo
  docker compose ps
  echo
  docker compose logs --tail 30 api generator ui
  fail "Uygulama sağlıklı başlamadı. Yukarıdaki loglara bak."
fi

URL="http://localhost:$UI_PORT"
echo
ok "LogPilot çalışıyor"
echo "   Arayüz:      $URL"
echo "   API belgesi: http://localhost:$API_PORT/scalar"
echo "   Loglar:      docker compose logs -f"
[[ "${COMPOSE_FILE:-}" == *secure* ]] && echo "   AI yetkisi:  docker compose logs api | grep -i 'read-only MongoDB account'"
echo "   Durdurmak:   stop.command  veya  ./scripts/docker-down.sh"

[[ $OPEN_BROWSER -eq 1 ]] && open "$URL" 2>/dev/null || true
