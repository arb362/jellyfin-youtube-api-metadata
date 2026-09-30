#!/usr/bin/env bash
# Builds the plugin zip Jellyfin installs and records it in manifest.json, so the repository can be
# used as a Jellyfin plugin repository straight from GitHub (no GitHub Release needed):
#
#   scripts/build-release.sh              # uses the OWNER/REPO already in manifest.json
#   scripts/build-release.sh you/my-fork  # and rewrites the download URL for that GitHub repo
#
# Output: releases/youtube-api-metadata_<version>.zip (+ .md5 / .sha256) and an updated
# manifest.json entry for <version>, where <version> comes from build.yaml.
#
# Requirements: dotnet SDK 10, python3.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

SLUG="${1:-}"
VERSION="$(sed -nE 's/^version: *"?([0-9.]+)"?.*/\1/p' build.yaml)"
if [[ -z "$VERSION" ]]; then
    echo "error: could not read version from build.yaml" >&2
    exit 1
fi

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

echo "==> Restoring, testing and publishing v$VERSION (Release)"
dotnet restore >/dev/null
dotnet test --configuration Release --nologo --verbosity quiet
dotnet publish Jellyfin.Plugin.YoutubeApiMetadata/Jellyfin.Plugin.YoutubeApiMetadata.csproj \
    --configuration Release --nologo --verbosity quiet --output "$STAGE/publish"

echo "==> Assembling zip"
mkdir -p "$STAGE/zip" releases
# Ship exactly the artifacts build.yaml declares (what the official jprm build would ship).
sed -nE 's/^- *"([^"]+)".*/\1/p' build.yaml | while read -r artifact; do
    if [[ ! -f "$STAGE/publish/$artifact" ]]; then
        echo "error: expected artifact $artifact missing from publish output" >&2
        exit 1
    fi
    cp "$STAGE/publish/$artifact" "$STAGE/zip/"
done

# meta.json is what Jellyfin reads from an installed plugin folder; jprm writes the same file.
python3 - "$STAGE/zip/meta.json" "$VERSION" <<'PY'
import datetime, json, re, sys
out, version = sys.argv[1], sys.argv[2]
build = {}
with open("build.yaml") as f:
    text = f.read()
for key in ("name", "guid", "targetAbi", "owner", "overview", "description", "category"):
    m = re.search(rf'^{key}: *"?(.*?)"?\s*$', text, re.M)
    build[key] = m.group(1) if m else ""
m = re.search(r'^changelog: >\n((?:[ \t]+.*\n?)+)', text, re.M)
changelog = " ".join(line.strip() for line in m.group(1).splitlines()) if m else ""
meta = {
    "category": build["category"],
    "changelog": changelog,
    "description": build["description"],
    "guid": build["guid"],
    "name": build["name"],
    "overview": build["overview"],
    "owner": build["owner"],
    "targetAbi": build["targetAbi"],
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "version": version,
    "status": "Active",
    "autoUpdate": True,
    "imagePath": "",
}
with open(out, "w") as f:
    json.dump(meta, f, indent=2)
PY

ZIP="releases/youtube-api-metadata_${VERSION}.zip"
rm -f "$ZIP"
python3 - "$STAGE/zip" "$ROOT/$ZIP" <<'PY'
import os, sys, zipfile
src, dest = sys.argv[1], sys.argv[2]
# Fixed timestamps so rebuilding identical binaries yields an identical zip (and checksum).
fixed = (2020, 1, 1, 0, 0, 0)
with zipfile.ZipFile(dest, "w", zipfile.ZIP_DEFLATED) as zf:
    for name in sorted(os.listdir(src)):
        info = zipfile.ZipInfo(name, date_time=fixed)
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o644 << 16
        with open(os.path.join(src, name), "rb") as f:
            zf.writestr(info, f.read())
PY
(cd releases && md5sum "$(basename "$ZIP")" > "$(basename "${ZIP%.zip}").md5" && sha256sum "$(basename "$ZIP")" > "$(basename "${ZIP%.zip}").sha256")
CHECKSUM="$(md5sum "$ZIP" | cut -d' ' -f1)"
echo "    $ZIP  (md5 $CHECKSUM)"

echo "==> Updating manifest.json"
python3 - "$VERSION" "$CHECKSUM" "$ZIP" "$SLUG" <<'PY'
import datetime, json, re, sys
version, checksum, zip_path, slug = sys.argv[1:5]

with open("manifest.json") as f:
    manifest = json.load(f)
entry = manifest[0]

if not slug:
    # Reuse whatever repo the manifest already points at.
    for v in entry["versions"]:
        m = re.match(r"https://raw\.githubusercontent\.com/([^/]+/[^/]+)/", v.get("sourceUrl", ""))
        if m:
            slug = m.group(1)
            break
slug = slug or "OWNER/REPO"

with open("build.yaml") as f:
    text = f.read()
m = re.search(r'^changelog: >\n((?:[ \t]+.*\n?)+)', text, re.M)
changelog = (" ".join(line.strip() for line in m.group(1).splitlines()) + "\n") if m else ""
m = re.search(r'^targetAbi: *"?([^"\n]+)"?', text, re.M)
target_abi = m.group(1) if m else entry["versions"][0]["targetAbi"]

new_version = {
    "version": version,
    "changelog": changelog,
    "targetAbi": target_abi,
    "sourceUrl": f"https://raw.githubusercontent.com/{slug}/main/{zip_path}",
    "checksum": checksum,
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}
entry["versions"] = [new_version] + [v for v in entry["versions"] if v["version"] != version]

with open("manifest.json", "w") as f:
    json.dump(manifest, f, indent=4)
    f.write("\n")
print(f"    {new_version['sourceUrl']}")
if slug == "OWNER/REPO":
    print("    NOTE: manifest still points at OWNER/REPO - run scripts/set-github-repo.sh <owner>/<repo>")
PY

echo "==> Done. Commit releases/ and manifest.json, push, and add the raw manifest.json URL to Jellyfin."
