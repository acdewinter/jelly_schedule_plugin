#!/usr/bin/env bash
# Adds the zips in dist/ to manifest.json (the Jellyfin plugin repository file).
# Usage: scripts/update-manifest.sh <version> <download-base-url>
set -euo pipefail
cd "$(dirname "$0")/.."
VERSION="$1"; BASEURL="$2"
python3 - "$VERSION" "$BASEURL" <<'PY'
import json, sys, hashlib, glob, os, datetime, re
version, baseurl = sys.argv[1], sys.argv[2]
guid = "7f2b1e6a-3c4d-4a5e-9b8c-2d1e0f9a7b6c"
path = "manifest.json"
manifest = json.load(open(path)) if os.path.exists(path) else []
plugin = next((p for p in manifest if p["guid"] == guid), None)
if plugin is None:
    plugin = {"guid": guid, "name": "Jelly Schedule", "description": "Your library as a weekly TV channel: viewing evenings, a lineup, movie nights, re-runs and a guide to tune in to.", "overview": "Your library as a weekly TV channel.", "owner": "acdewinter", "category": "General", "imageUrl": "https://raw.githubusercontent.com/acdewinter/jelly_schedule_plugin/main/docs/icon.png", "versions": []}
    manifest.append(plugin)
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
for zip_path in sorted(glob.glob("dist/*.zip")):
    name = os.path.basename(zip_path)
    m = re.match(r"jellyschedule_(?P<ver>[\d.]+)_jellyfin-(?P<abi>[\d.]+)\.zip", name)
    if not m:
        continue
    ver, abi = m.group("ver"), m.group("abi") + ".0.0"
    checksum = hashlib.md5(open(zip_path, "rb").read()).hexdigest()
    plugin["versions"] = [v for v in plugin["versions"] if v["version"] != ver]
    plugin["versions"].insert(0, {"version": ver, "changelog": f"See {baseurl.rsplit('/download', 1)[0]}", "targetAbi": abi, "sourceUrl": f"{baseurl}/{name}", "checksum": checksum, "timestamp": now})
plugin["versions"].sort(key=lambda v: [int(x) for x in v["version"].split(".")], reverse=True)
json.dump(manifest, open(path, "w"), indent=2)
print(json.dumps(plugin["versions"][:2], indent=2))
PY
