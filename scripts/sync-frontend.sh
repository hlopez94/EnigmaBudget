#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$ROOT/web/dist/web/browser"
TARGET="$ROOT/src/EnigmaBudget.Api/wwwroot"

mkdir -p "$TARGET"
while IFS= read -r -d '' asset; do
  relative_path="${asset#"$SOURCE"/}"
  target_directory="$TARGET/$(dirname "$relative_path")"
  mkdir -p "$target_directory"
  cp "$asset" "$TARGET/$relative_path"
done < <(find "$SOURCE" -type f ! -name index.html -print0)

# Publish the new entry point only after every referenced asset is in place.
cp "$SOURCE/index.html" "$TARGET/index.html"
