#!/usr/bin/env bash
# LogPilot Faz 1 kurulumu. Proje klasörünün içinde BİR KEZ çalıştır:  bash bootstrap.sh
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null || ! dotnet --version | grep -q '^10\.'; then
  echo "HATA: .NET 10 SDK gerekli (bulunan: $(dotnet --version 2>/dev/null || echo 'yok'))." >&2
  echo "Kurulum:  brew uninstall dotnet && brew install --cask dotnet-sdk   (sonra yeni terminal aç)" >&2
  exit 1
fi

[ -d .git ] || git init -q
dotnet new globaljson --sdk-version "$(dotnet --version)" --roll-forward latestFeature --force
dotnet new gitignore
dotnet new sln -n LogPilot

dotnet new classlib -o src/LogPilot.Domain
dotnet new classlib -o src/LogPilot.Application
dotnet new classlib -o src/LogPilot.Infrastructure
dotnet new web      -o src/LogPilot.Api
dotnet new xunit    -o tests/LogPilot.UnitTests
dotnet sln add $(find src tests -name '*.csproj')

dotnet add src/LogPilot.Application    reference src/LogPilot.Domain
dotnet add src/LogPilot.Infrastructure reference src/LogPilot.Application
dotnet add src/LogPilot.Api            reference src/LogPilot.Application src/LogPilot.Infrastructure
dotnet add tests/LogPilot.UnitTests    reference src/LogPilot.Application

dotnet add src/LogPilot.Application    package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/LogPilot.Infrastructure package MongoDB.Driver
dotnet add src/LogPilot.Infrastructure package Microsoft.Extensions.Hosting.Abstractions
dotnet add src/LogPilot.Infrastructure package Microsoft.Extensions.Options.ConfigurationExtensions
dotnet add src/LogPilot.Api            package Microsoft.AspNetCore.OpenApi
dotnet add src/LogPilot.Api            package Scalar.AspNetCore
dotnet add tests/LogPilot.UnitTests    package AwesomeAssertions
dotnet add tests/LogPilot.UnitTests    package Moq

# Şablon dosyalarını sil, hazır kaynak kodunu üzerine kopyala
rm -f src/LogPilot.Domain/Class1.cs src/LogPilot.Application/Class1.cs \
      src/LogPilot.Infrastructure/Class1.cs tests/LogPilot.UnitTests/UnitTest1.cs
cp -R _overlay/. .
rm -rf _overlay
chmod +x scripts/*.sh

echo "== Derleme ve test =="
dotnet build
dotnet test

echo
echo "Hazır. Başlatmak için:  ./scripts/start-local.sh"
