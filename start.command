#!/bin/bash
# Finder'da çift tıkla: LogPilot'u Docker'da başlatır ve tarayıcıyı açar.
cd "$(dirname "$0")"
./scripts/docker-up.sh
status=$?
echo
read -n 1 -s -r -p "Bu pencereyi kapatmak için bir tuşa bas (uygulama çalışmaya devam eder)..."
exit $status
