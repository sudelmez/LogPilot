#!/usr/bin/env bash
# React arayüzünü geliştirme modunda başlatır: http://localhost:5173  (/api -> http://localhost:5080)
set -euo pipefail
cd "$(dirname "$0")/../frontend/logpilot-ui"
[ -d node_modules ] || npm install
npm run dev
