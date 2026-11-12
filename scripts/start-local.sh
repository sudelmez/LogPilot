#!/usr/bin/env bash
# API (http://localhost:5080) ve LogGenerator'ı birlikte başlatır. Durdurmak için Ctrl+C.
set -euo pipefail
cd "$(dirname "$0")/.."

# .env varsa yapay zekâ ayarlarını oradan al (Docker ile aynı dosya; anahtar yalnızca orada durur).
if [[ -f .env ]]; then set -a; source .env; set +a; fi
[[ -n "${LOGPILOT_AI_PROVIDER:-}" ]] && export Ai__Provider="$LOGPILOT_AI_PROVIDER"
[[ -n "${LOGPILOT_AI_MODEL:-}" ]]    && export Ai__Claude__Model="$LOGPILOT_AI_MODEL"
[[ -n "${LOGPILOT_AI_EFFORT:-}" ]]   && export Ai__Claude__ChatEffort="$LOGPILOT_AI_EFFORT"
[[ -n "${LOGPILOT_OLLAMA_MODEL:-}" ]] && export Ai__Ollama__Model="$LOGPILOT_OLLAMA_MODEL"
[[ -n "${LOGPILOT_AI_AUTO_SUMMARY:-}" ]] && export Incidents__AutoSummarize="$LOGPILOT_AI_AUTO_SUMMARY"
# Yerel çalıştırmada API Mac'te: Ollama'ya localhost'tan ulaşır (.env'deki host.docker.internal Docker içindir).
export Ai__Ollama__BaseUrl="http://localhost:11434"
echo "Yapay zekâ sağlayıcısı: ${Ai__Provider:-Mock}"

# MongoDB zaten dinliyorsa (ör. brew) Docker'a dokunma: Docker Desktop kapalıyken `docker compose` sessizce takılır.
if nc -z localhost 27017 >/dev/null 2>&1; then
  echo "MongoDB localhost:27017 üzerinde çalışıyor."
elif command -v docker >/dev/null 2>&1; then
  echo "MongoDB Docker ile başlatılıyor (Docker Desktop açık olmalı)..."
  docker compose up -d --wait mongo
else
  echo "MongoDB bulunamadı. Başlatın: brew services start mongodb-community" >&2
  exit 1
fi

# Tek seferde derle: iki "dotnet run" aynı anda derlemeye çalışmasın.
dotnet build --nologo -v q

dotnet run --no-build --project src/LogPilot.LogGenerator &
GENERATOR_PID=$!
trap 'kill "$GENERATOR_PID" 2>/dev/null || true' EXIT INT TERM

dotnet run --no-build --project src/LogPilot.Api --urls http://localhost:5080
