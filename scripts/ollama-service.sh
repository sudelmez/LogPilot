#!/usr/bin/env bash
# Ollama'yı yalnızca LogPilot çalışırken açık tutar.
#   ./scripts/ollama-service.sh start   → kapalıysa başlatır (bilgisayar açılışına KAYDETMEZ) ve bunu not eder
#   ./scripts/ollama-service.sh stop    → yalnızca 'start' ile LogPilot'un açtığı Ollama'yı kapatır
# Ollama'yı sen başka bir iş için elle açtıysan 'stop' ona dokunmaz.
set -euo pipefail
cd "$(dirname "$0")/.."

MARKER=".ollama-started-by-logpilot"   # içerik: "brew" veya "pid <numara>"
URL="http://localhost:11434/api/version"

running() { curl -sf --max-time 2 "$URL" >/dev/null 2>&1; }
has_brew_ollama() { command -v brew >/dev/null 2>&1 && brew list --versions ollama >/dev/null 2>&1; }

start() {
  command -v ollama >/dev/null 2>&1 || { echo "Ollama kurulu değil. Kur: brew install ollama" >&2; exit 1; }

  if running; then
    [[ -f "$MARKER" ]] || echo "Ollama zaten açık (LogPilot dışında başlatılmış; durdurma sırasında kapatılmayacak)."
    return 0
  fi

  if has_brew_ollama; then
    # 'run': yalnızca şimdi başlatır; 'start' gibi oturum açılışına kaydetmez.
    brew services run ollama >/dev/null
    echo "brew" > "$MARKER"
  else
    mkdir -p "$HOME/Library/Logs"
    nohup ollama serve >>"$HOME/Library/Logs/ollama-logpilot.log" 2>&1 &
    echo "pid $!" > "$MARKER"
  fi

  for ((i = 0; i < 20; i++)); do running && return 0; sleep 1; done
  echo "Ollama 20 saniyede açılmadı. Elle dene: ollama serve" >&2
  exit 1
}

stop() {
  if [[ ! -f "$MARKER" ]]; then
    running && echo "Ollama açık ama LogPilot başlatmadı; dokunulmadı."
    return 0
  fi

  read -r mode pid < "$MARKER" || true
  if [[ "$mode" == "brew" ]]; then
    brew services stop ollama >/dev/null 2>&1 || true
  elif [[ "$mode" == "pid" && -n "${pid:-}" ]]; then
    kill "$pid" 2>/dev/null || true
  fi
  rm -f "$MARKER"

  for ((i = 0; i < 10; i++)); do running || { echo "Ollama durduruldu."; return 0; }; sleep 1; done
  echo "Ollama hâlâ yanıt veriyor; elle kapat: brew services stop ollama" >&2
}

case "${1:-}" in
  start) start ;;
  stop)  stop ;;
  *) echo "Kullanım: $0 start|stop" >&2; exit 2 ;;
esac
