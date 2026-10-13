#!/usr/bin/env bash
# Builds Release and packages dist/RetakesPlugin.zip in the CounterStrikeSharp layout:
#   addons/counterstrikesharp/plugins/RetakesPlugin/...
#   addons/counterstrikesharp/shared/RetakesPluginShared/...
# Also writes dist/release-notes.md from the matching CHANGELOG.md section.
#
# Usage: ./package.sh [X.Y.Z]   (version defaults to RetakesPlugin.Version; when given it must match)
set -euo pipefail
cd "$(dirname "$0")"

PLUGIN=RetakesPlugin
SHARED=RetakesPluginShared

SOURCE_VERSION="$(grep -oP 'public const string Version = "\K[^"]+' RetakesPlugin/RetakesPlugin.cs)"
VERSION="${1:-$SOURCE_VERSION}"
if [[ "$VERSION" != "$SOURCE_VERSION" ]]; then
    echo "ERROR: version $VERSION does not match RetakesPlugin.Version ($SOURCE_VERSION)" >&2
    exit 1
fi

NOTES="$(awk -v v="$VERSION" '$0 ~ "^## " { if (found) exit; found = ($2 == v); next } found' CHANGELOG.md)"
if [[ -z "${NOTES//[[:space:]]/}" ]]; then
    echo "ERROR: CHANGELOG.md has no \"## $VERSION\" section" >&2
    exit 1
fi

rm -rf dist RetakesPlugin/bin/Release RetakesPluginShared/bin/Release
dotnet build RetakesPlugin/RetakesPlugin.csproj -c Release

STAGE=dist/stage
PLUGIN_DIR="$STAGE/addons/counterstrikesharp/plugins/$PLUGIN"
SHARED_DIR="$STAGE/addons/counterstrikesharp/shared/$SHARED"
mkdir -p "$PLUGIN_DIR" "$SHARED_DIR"
cp -r RetakesPlugin/bin/Release/net10.0/. "$PLUGIN_DIR/"
cp -r RetakesPluginShared/bin/Release/net10.0/. "$SHARED_DIR/"

# Linux only, no debug symbols or backups.
find "$STAGE" \( -name '*.pdb' -o -name '*.bak' \) -delete
find "$STAGE" -type d -path '*/runtimes/*' \( -name 'win*' -o -name 'osx*' \) -prune -exec rm -rf {} +

test -f "$PLUGIN_DIR/$PLUGIN.dll"
test -f "$SHARED_DIR/$SHARED.dll"
if [[ -d "$STAGE/addons/counterstrikesharp/configs" ]]; then
    echo "ERROR: configs/ must never be shipped" >&2
    exit 1
fi

(cd "$STAGE" && zip -qr "../$PLUGIN.zip" addons)
rm -rf "$STAGE"
printf '%s\n' "$NOTES" > dist/release-notes.md

unzip -l "dist/$PLUGIN.zip"
echo "==> dist/$PLUGIN.zip ($VERSION)"
