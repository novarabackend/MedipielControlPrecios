#!/usr/bin/env bash
set -euo pipefail

# Publishes competitor adapters (plugins) into backend/plugins/<AdapterName>.
# The API loads adapters from the plugins folder at runtime.

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
BACKEND_DIR="$ROOT_DIR/backend"
SRC_DIR="$BACKEND_DIR/src"
PLUGINS_DIR="$BACKEND_DIR/plugins"

publish_plugin() {
  local project_dir="$1"   # e.g. Medipiel.Competitors.BellaPiel
  local out_dir="$2"       # e.g. BellaPiel

  echo "Publishing $project_dir -> $PLUGINS_DIR/$out_dir"
  dotnet publish "$SRC_DIR/$project_dir/$project_dir.csproj" -c Release -o "$PLUGINS_DIR/$out_dir"
}

publish_plugin "Medipiel.Competitors.LineaEstetica" "LineaEstetica"
publish_plugin "Medipiel.Competitors.BellaPiel" "BellaPiel"
publish_plugin "Medipiel.Competitors.Farmatodo" "Farmatodo"
publish_plugin "Medipiel.Competitors.CruzVerde" "CruzVerde"

echo "OK: plugins published under $PLUGINS_DIR/"

