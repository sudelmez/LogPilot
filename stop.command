#!/bin/bash
# Finder'da çift tıkla: LogPilot konteynerlerini durdurur (veri korunur).
cd "$(dirname "$0")"
./scripts/docker-down.sh
echo
read -n 1 -s -r -p "Kapatmak için bir tuşa bas..."
