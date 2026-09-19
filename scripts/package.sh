#!/usr/bin/env bash
# Builds the plugin for both supported Jellyfin generations and zips each with its meta.json.
# Usage: scripts/package.sh [version]   (version defaults to the one in Directory.Build.props)
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="${1:-$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' Directory.Build.props | head -1)}"
GUID="7f2b1e6a-3c4d-4a5e-9b8c-2d1e0f9a7b6c"
rm -rf dist && mkdir -p dist

# framework | Jellyfin ABI | version suffix (4th component distinguishes the two builds in the manifest)
for spec in "net9.0|10.11.0.0|11" "net10.0|12.0.0.0|12"; do
  IFS='|' read -r TFM ABI SUFFIX <<< "$spec"
  FULLVER="${VERSION}.${SUFFIX}"
  OUT="dist/build-${TFM}"
  dotnet publish Jellyfin.Plugin.JellySchedule/Jellyfin.Plugin.JellySchedule.csproj -c Release -f "$TFM" -o "$OUT" \
    -p:Version="$VERSION" -p:AssemblyVersion="$FULLVER" -p:FileVersion="$FULLVER" -p:InformationalVersion="$FULLVER" >/dev/null
  STAGE="dist/stage-${TFM}"; rm -rf "$STAGE"; mkdir -p "$STAGE"
  cp "$OUT/Jellyfin.Plugin.JellySchedule.dll" "$STAGE/"
  cat > "$STAGE/meta.json" <<JSON
{
  "guid": "$GUID",
  "name": "Jelly Schedule",
  "description": "Your library as a weekly TV channel: viewing evenings, a lineup, movie nights, re-runs and a guide to tune in to.",
  "overview": "Your library as a weekly TV channel.",
  "owner": "acdewinter",
  "category": "General",
  "version": "$FULLVER",
  "targetAbi": "$ABI",
  "framework": "$TFM",
  "changelog": "See https://github.com/acdewinter/jelly_schedule_plugin/releases",
  "imagePath": "",
  "status": "Active",
  "autoUpdate": true
}
JSON
  ZIP="dist/jellyschedule_${FULLVER}_jellyfin-${ABI%.0.0}.zip"
  (cd "$STAGE" && zip -q -r "../../$ZIP" .)
  echo "built $ZIP ($(md5sum "$ZIP" | cut -d' ' -f1))"
done
rm -rf dist/build-* dist/stage-*
ls -la dist
