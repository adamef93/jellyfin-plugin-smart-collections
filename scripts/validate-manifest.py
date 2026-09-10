#!/usr/bin/env python3
"""Validate manifest.json for the Smart Collections Jellyfin plugin repository.

Checks (always):
  - manifest.json is valid JSON
  - exactly one plugin object in the top-level array
  - guid equals the expected constant
  - required top-level fields are present and non-empty
  - every version entry has version/targetAbi/sourceUrl/checksum/timestamp/changelog
  - checksum matches ^[a-f0-9]{32}$
  - versions are strictly descending by numeric 4-tuple (implies no duplicates)

Checks (when --new-version/--zip are given, at release time):
  - the newest entry's version equals --new-version
  - the newest entry's checksum equals the md5 of the built zip (--zip)

Exits non-zero with a message on any failure.
"""

import argparse
import hashlib
import json
import re
import sys

EXPECTED_GUID = "09612e52-0f93-41ab-a6ab-5a19479f5315"
REQUIRED_TOP_LEVEL_FIELDS = ["guid", "name", "description", "overview", "owner", "category", "versions"]
REQUIRED_VERSION_FIELDS = ["version", "targetAbi", "sourceUrl", "checksum", "timestamp", "changelog"]
CHECKSUM_RE = re.compile(r"^[a-f0-9]{32}$")
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+\.\d+$")


def fail(message):
    print(f"MANIFEST VALIDATION FAILED: {message}", file=sys.stderr)
    sys.exit(1)


def version_tuple(version):
    return tuple(int(part) for part in version.split("."))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", default="manifest.json")
    parser.add_argument("--new-version", help="Tag being released; must match the newest manifest entry")
    parser.add_argument("--zip", help="Path to the built zip; its md5 must match the newest entry's checksum")
    args = parser.parse_args()

    try:
        with open(args.manifest, encoding="utf-8") as handle:
            manifest = json.load(handle)
    except FileNotFoundError:
        fail(f"{args.manifest} not found")
    except json.JSONDecodeError as error:
        fail(f"{args.manifest} is not valid JSON: {error}")

    if not isinstance(manifest, list) or len(manifest) != 1:
        fail("manifest must be an array containing exactly one plugin object")
    plugin = manifest[0]
    if not isinstance(plugin, dict):
        fail("plugin entry must be an object")

    for field in REQUIRED_TOP_LEVEL_FIELDS:
        if field not in plugin or plugin[field] in (None, "", []):
            fail(f"missing or empty top-level field: {field}")
    if plugin["guid"] != EXPECTED_GUID:
        fail(f"guid mismatch: expected {EXPECTED_GUID}, got {plugin['guid']}")

    versions = plugin["versions"]
    if not isinstance(versions, list) or not versions:
        fail("versions must be a non-empty array")

    for entry in versions:
        if not isinstance(entry, dict):
            fail("every version entry must be an object")
        for field in REQUIRED_VERSION_FIELDS:
            if field not in entry or not isinstance(entry[field], str) or not entry[field]:
                fail(f"version entry {entry.get('version', '<unknown>')} missing or empty field: {field}")
        if not VERSION_RE.match(entry["version"]):
            fail(f"version is not a numeric 4-tuple: {entry['version']}")
        if not CHECKSUM_RE.match(entry["checksum"]):
            fail(f"checksum for {entry['version']} does not match ^[a-f0-9]{{32}}$: {entry['checksum']}")

    tuples = [version_tuple(entry["version"]) for entry in versions]
    for previous, current in zip(tuples, tuples[1:]):
        if previous <= current:
            fail(f"versions must be strictly descending with no duplicates; found {previous} then {current}")

    if args.new_version:
        newest = versions[0]
        if newest["version"] != args.new_version:
            fail(f"newest entry is {newest['version']}, expected released tag {args.new_version}")
        if not args.zip:
            fail("--new-version requires --zip")
        with open(args.zip, "rb") as handle:
            actual_md5 = hashlib.md5(handle.read()).hexdigest()
        if newest["checksum"] != actual_md5:
            fail(f"newest entry checksum {newest['checksum']} != md5 of {args.zip} ({actual_md5})")

    print(f"manifest OK: {len(versions)} version entries, newest {versions[0]['version']}")


if __name__ == "__main__":
    main()
