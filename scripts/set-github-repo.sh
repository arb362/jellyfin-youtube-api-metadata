#!/usr/bin/env bash
# Points manifest.json and the README at your GitHub repository, e.g.:
#
#   scripts/set-github-repo.sh yourname/jellyfin-youtube-api-metadata
#
# Run once after forking/pushing (it only rewrites the OWNER/REPO placeholder or a previous slug
# set by this script), then commit manifest.json and README.md.
set -euo pipefail

SLUG="${1:-}"
if [[ ! "$SLUG" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "usage: $0 <github-owner>/<repo>" >&2
    exit 1
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

python3 - "$SLUG" <<'PY'
import json, re, sys
slug = sys.argv[1]

with open("manifest.json") as f:
    manifest = json.load(f)
changed = 0
for entry in manifest:
    for v in entry["versions"]:
        url = v.get("sourceUrl", "")
        m = re.match(r"(https://raw\.githubusercontent\.com/)([^/]+/[^/]+)(/main/releases/.*)", url)
        if m:
            v["sourceUrl"] = m.group(1) + slug + m.group(3)
            changed += 1
with open("manifest.json", "w") as f:
    json.dump(manifest, f, indent=4)
    f.write("\n")

with open("README.md") as f:
    readme = f.read()
readme_new = re.sub(r"raw\.githubusercontent\.com/[^/\s]+/[^/\s]+/main/manifest\.json",
                    f"raw.githubusercontent.com/{slug}/main/manifest.json", readme)
readme_new = re.sub(r"github\.com/[^/\s]+/[^/\s]+/actions/workflows/build\.yml/badge\.svg",
                    f"github.com/{slug}/actions/workflows/build.yml/badge.svg", readme_new)
with open("README.md", "w") as f:
    f.write(readme_new)

print(f"manifest.json: {changed} download URL(s) now point at {slug}")
print(f"Plugin repository URL: https://raw.githubusercontent.com/{slug}/main/manifest.json")
PY
