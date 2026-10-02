#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import json
import os
import pathlib
import re
import sys
from datetime import datetime

SEMVER = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?$")
HEX_COMMIT = re.compile(r"^[0-9a-f]{40,64}$")
HEX_SHA256 = re.compile(r"^[0-9a-f]{64}$")


def fail(message: str) -> None:
    raise SystemExit(f"ERROR: {message}")


def digest(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def require_dict(value, name: str) -> dict:
    if not isinstance(value, dict):
        fail(f"{name} must be an object.")
    return value


def require_list_of_strings(value, name: str) -> list[str]:
    if not isinstance(value, list) or any(not isinstance(item, str) or not item for item in value):
        fail(f"{name} must be an array of non-empty strings.")
    if value != sorted(value) or len(value) != len(set(value)):
        fail(f"{name} must be sorted and unique.")
    return value


def validate_artifact(base: pathlib.Path, doc: dict, expected_name: str, label: str) -> None:
    if doc.get("fileName") != expected_name:
        fail(f"{label} fileName must be {expected_name}.")
    sha = doc.get("sha256")
    size = doc.get("sizeBytes")
    if not isinstance(sha, str) or not HEX_SHA256.fullmatch(sha):
        fail(f"{label} sha256 must be 64 lowercase hex characters.")
    if not isinstance(size, int) or size < 0:
        fail(f"{label} sizeBytes must be a non-negative integer.")
    path = base / expected_name
    if not path.is_file() or path.is_symlink():
        fail(f"{label} file is missing or unsafe: {expected_name}")
    if digest(path) != sha:
        fail(f"{label} sha256 does not match {expected_name}.")
    if path.stat().st_size != size:
        fail(f"{label} sizeBytes does not match {expected_name}.")


def main() -> None:
    if len(sys.argv) != 2:
        fail("Usage: validate-public-source-manifest.py <public-source.json>")
    manifest = pathlib.Path(sys.argv[1]).resolve()
    if not manifest.is_file() or manifest.is_symlink():
        fail(f"Manifest is missing or unsafe: {manifest}")
    try:
        doc = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Could not read JSON manifest: {exc}")

    if doc.get("schemaVersion") != 1:
        fail("schemaVersion must be 1.")

    product = require_dict(doc.get("product"), "product")
    if product.get("id") != "mem" or product.get("name") != "Message Easy Mode":
        fail("product identity must be Message Easy Mode / mem.")
    version = product.get("version")
    if not isinstance(version, str) or not SEMVER.fullmatch(version):
        fail("product.version must be an ordinary semantic version or prerelease version.")
    if any(word in version for word in ("latest", "stable", "current", "dev")):
        fail("product.version may not contain a mutable channel/development label.")

    source = require_dict(doc.get("source"), "source")
    for key in ("commit", "tree"):
        value = source.get(key)
        if not isinstance(value, str) or not HEX_COMMIT.fullmatch(value):
            fail(f"source.{key} must be a full lowercase Git object id.")
    if not isinstance(source.get("sourceDateEpoch"), int) or source["sourceDateEpoch"] < 0:
        fail("source.sourceDateEpoch must be a non-negative integer.")
    if source.get("workingTreeDirty") is not False:
        fail("source.workingTreeDirty must be false.")

    export = require_dict(doc.get("export"), "export")
    if export.get("format") != "mem-public-source-v1":
        fail("export.format must be mem-public-source-v1.")
    generated = export.get("generatedAtUtc")
    if not isinstance(generated, str):
        fail("export.generatedAtUtc must be a string.")
    try:
        datetime.strptime(generated, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError:
        fail("export.generatedAtUtc must use YYYY-MM-DDTHH:MM:SSZ.")
    policy = require_dict(export.get("policy"), "export.policy")
    if policy.get("path") != "scripts/public-source/public-source-top-level-policy.tsv":
        fail("export.policy.path is not the canonical public-source policy path.")
    if not isinstance(policy.get("sha256"), str) or not HEX_SHA256.fullmatch(policy["sha256"]):
        fail("export.policy.sha256 must be 64 lowercase hex characters.")
    path_exclusions = require_dict(export.get("pathExclusions"), "export.pathExclusions")
    if path_exclusions.get("path") != "scripts/public-source/public-source-path-exclusions.txt":
        fail("export.pathExclusions.path is not the canonical nested-exclusion policy path.")
    if not isinstance(path_exclusions.get("sha256"), str) or not HEX_SHA256.fullmatch(path_exclusions["sha256"]):
        fail("export.pathExclusions.sha256 must be 64 lowercase hex characters.")
    included = require_list_of_strings(export.get("includedTopLevel"), "export.includedTopLevel")
    excluded = require_list_of_strings(export.get("excludedTopLevel"), "export.excludedTopLevel")
    excluded_paths = require_list_of_strings(export.get("excludedPaths"), "export.excludedPaths")
    if not included:
        fail("export.includedTopLevel may not be empty.")
    if set(included) & set(excluded):
        fail("includedTopLevel and excludedTopLevel may not overlap.")
    for path in excluded_paths:
        trimmed = path[:-1] if path.endswith("/") else path
        parts = pathlib.PurePosixPath(trimmed).parts
        if len(parts) < 2 or parts[0] not in included:
            fail(f"export.excludedPaths entry must be nested below an included top-level root: {path}")

    contents = require_dict(doc.get("contents"), "contents")
    if not isinstance(contents.get("fileCount"), int) or contents["fileCount"] <= 0:
        fail("contents.fileCount must be a positive integer.")
    if not isinstance(contents.get("treeSha256"), str) or not HEX_SHA256.fullmatch(contents["treeSha256"]):
        fail("contents.treeSha256 must be 64 lowercase hex characters.")

    artifacts = require_dict(doc.get("artifacts"), "artifacts")
    base = manifest.parent
    validate_artifact(base, require_dict(artifacts.get("archive"), "artifacts.archive"), f"mem-{version}-source.tar.gz", "archive")
    validate_artifact(base, require_dict(artifacts.get("fileManifest"), "artifacts.fileManifest"), "evidence/file-manifest.tsv", "fileManifest")

    print(f"Public-source manifest is valid: {manifest}")


if __name__ == "__main__":
    main()
