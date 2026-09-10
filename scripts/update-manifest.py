#!/usr/bin/env python3
"""Prepend a new version entry to manifest.json for a release.

Rules:
  - manifest.json in the checkout (git) is the source of truth; nothing is
    fetched over the network.
  - If manifest.json is missing: start from an empty skeleton, with a loud
    warning (this should only ever happen on a fresh repository).
  - If manifest.json exists but is invalid JSON: fail the release.
  - targetAbi is read from build.yaml (jellyfin_version), the single source
    of truth, normalized to a numeric 4-tuple.
  - The new entry is prepended; entries are deduped by version (new wins)
    and sorted descending by numeric 4-tuple.
  - Existing entries are never rewritten; historical targetAbi/checksums are
    preserved exactly as loaded.
"""

import argparse
import datetime
import json
import re
import sys

EXPECTED_GUID = "09612e52-0f93-41ab-a6ab-5a19479f5315"
SKELETON = {
    "guid": EXPECTED_GUID,
    "name": "Smart Collections",
    "overview": "Enables creation of Smart Collections based on Tag",
    "description": "Enables creation of Smart Collections based on Tag",
    "owner": "johnpc",
    "category": "General",
    "versions": [],
}


def fail(message):
    print(f"MANIFEST UPDATE FAILED: {message}", file=sys.stderr)
    sys.exit(1)


def version_tuple(version):
    return tuple(int(part) for part in version.split("."))


def read_target_abi(build_yaml_path):
    with open(build_yaml_path, encoding="utf-8") as handle:
        content = handle.read()
    match = re.search(r'^jellyfin_version:\s*"?([0-9][0-9.]*[0-9])"?', content, re.MULTILINE)
    if not match:
        fail(f"could not read jellyfin_version from {build_yaml_path}")
    parts = match.group(1).split(".")
    if not 2 <= len(parts) <= 4 or not all(part.isdigit() for part in parts):
        fail(f"jellyfin_version in {build_yaml_path} is not a numeric version: {match.group(1)}")
    parts += ["0"] * (4 - len(parts))
    return ".".join(parts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="manifest.json")
    parser.add_argument("--build-yaml", default="build.yaml")
    parser.add_argument("--version", required=True)
    parser.add_argument("--checksum", required=True)
    parser.add_argument("--source-url", required=True)
    parser.add_argument("--changelog", required=True)
    args = parser.parse_args()

    if not re.match(r"^\d+\.\d+\.\d+\.\d+$", args.version):
        fail(f"version is not a numeric 4-tuple: {args.version}")
    if not re.match(r"^[a-f0-9]{32}$", args.checksum):
        fail(f"checksum is not a 32-char lowercase hex md5: {args.checksum}")

    try:
        with open(args.manifest, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except FileNotFoundError:
        print("!" * 72, file=sys.stderr)
        print(f"WARNING: {args.manifest} not found; starting from an EMPTY manifest.", file=sys.stderr)
        print("WARNING: all historical version entries will be missing!", file=sys.stderr)
        print("!" * 72, file=sys.stderr)
        manifest = [dict(SKELETON, versions=[])]
    except json.JSONDecodeError as error:
        fail(f"{args.manifest} exists but is invalid JSON; refusing to release: {error}")

    if not isinstance(manifest, list) or len(manifest) != 1 or not isinstance(manifest[0], dict):
        fail("manifest must be an array containing exactly one plugin object")
    plugin = manifest[0]
    if plugin.get("guid") != EXPECTED_GUID:
        fail(f"guid mismatch: expected {EXPECTED_GUID}, got {plugin.get('guid')}")

    new_entry = {
        "version": args.version,
        "changelog": args.changelog,
        "targetAbi": read_target_abi(args.build_yaml),
        "sourceUrl": args.source_url,
        "checksum": args.checksum,
        "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }

    versions = [entry for entry in plugin.get("versions", []) if entry.get("version") != args.version]
    versions.insert(0, new_entry)
    versions.sort(key=lambda entry: version_tuple(entry["version"]), reverse=True)
    plugin["versions"] = versions

    with open(args.manifest, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")
    print(f"manifest updated: added {args.version} (targetAbi {new_entry['targetAbi']}), {len(versions)} entries total")


if __name__ == "__main__":
    main()
