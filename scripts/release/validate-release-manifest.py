#!/usr/bin/env python3
"""Validate the MEM unified release manifest without third-party dependencies."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from datetime import datetime
from pathlib import Path
from urllib.parse import urlparse

SEMVER = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?"
    r"(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)
HEX_SHA = re.compile(r"^[0-9a-f]{64}$")
DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")
COMMIT = re.compile(r"^[0-9a-f]{40,64}$")
SOURCE_ID = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")
CANONICAL_RELEASE_REPOSITORY = "https://github.com/message-easy-mode/mem-releases"
CANONICAL_SOURCE_REPOSITORY = "https://github.com/message-easy-mode/mem"
CANONICAL_SOURCE_ID = "control-plane"

REQUIRED_ARTIFACTS = {
    "bootstrap",
    "installer",
    "cli",
    "migrate-bootstrap",
    "migrate-manifest",
    "migrate",
    "migrate-checksum",
    "sbom",
}

EXPECTED_KINDS = {
    "bootstrap": "bootstrap-script",
    "installer": "installer-bundle",
    "cli": "cli-binary",
    "migrate-bootstrap": "migrate-bootstrap-script",
    "migrate-manifest": "component-release-manifest",
    "migrate": "migrate-bundle",
    "migrate-checksum": "sha256-sidecar",
    "sbom": "sbom-spdx-json",
}

MUTABLE_WORDS = re.compile(r"(^|[-_.])(latest|stable|current|dev)([-_.]|$)", re.I)


def fail(message: str) -> None:
    raise ValueError(message)


def require_exact_keys(obj: object, expected: set[str], label: str) -> dict:
    if not isinstance(obj, dict):
        fail(f"{label} must be an object")
    keys = set(obj)
    missing = expected - keys
    extra = keys - expected
    if missing:
        fail(f"{label} is missing: {', '.join(sorted(missing))}")
    if extra:
        fail(f"{label} contains unsupported keys: {', '.join(sorted(extra))}")
    return obj


def require_uri(value: object, label: str, *, github_org: bool = False) -> str:
    if not isinstance(value, str) or not value:
        fail(f"{label} must be a non-empty URI")
    parsed = urlparse(value)
    if parsed.scheme != "https" or not parsed.netloc:
        fail(f"{label} must be an https URI")
    if github_org:
        if parsed.netloc.lower() != "github.com" or not parsed.path.startswith("/message-easy-mode/"):
            fail(f"{label} must be a repository under https://github.com/message-easy-mode/")
    return value


def require_sha256(value: object, label: str) -> str:
    if not isinstance(value, str) or not HEX_SHA.fullmatch(value):
        fail(f"{label} must be 64 lowercase hexadecimal characters")
    return value


def load_manifest(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"cannot read valid JSON manifest: {exc}")


def expected_artifact_filename(artifact_id: str, version: str) -> str:
    return {
        "bootstrap": f"install-mem-{version}.sh",
        "installer": f"mem-installer-{version}-ubuntu-24.04-amd64.tar.gz",
        "cli": f"mem-cli-{version}-linux-x64",
        "migrate-bootstrap": f"install-mem-migrate-{version}.sh",
        "migrate-manifest": f"mem-migrate-{version}-release.json",
        "migrate": f"mem-migrate-{version}-linux-x64.tar.gz",
        "migrate-checksum": f"mem-migrate-{version}-linux-x64.tar.gz.sha256",
        "sbom": f"mem-{version}-sbom.spdx.json",
    }[artifact_id]


def validate_manifest(doc: dict) -> None:
    require_exact_keys(
        doc,
        {
            "schemaVersion",
            "product",
            "release",
            "sources",
            "support",
            "controlPlane",
            "artifacts",
            "verification",
        },
        "manifest",
    )

    if doc["schemaVersion"] != 1:
        fail("schemaVersion must be 1")

    product = require_exact_keys(
        doc["product"], {"id", "name", "version", "channel"}, "product"
    )
    if product["id"] != "mem":
        fail("product.id must be mem")
    if product["name"] != "Message Easy Mode":
        fail("product.name must be Message Easy Mode")

    version = product["version"]
    if not isinstance(version, str):
        fail("product.version must be a string")
    match = SEMVER.fullmatch(version)
    if not match:
        fail("product.version must be Semantic Versioning 2.0 syntax")

    channel = product["channel"]
    if channel not in {"stable", "prerelease"}:
        fail("product.channel must be stable or prerelease")
    prerelease = match.group(4)
    if channel == "stable" and prerelease:
        fail("stable channel must not use a prerelease version")
    if channel == "prerelease" and not prerelease:
        fail("prerelease channel requires a semantic-version prerelease suffix")

    release = require_exact_keys(doc["release"], {"generatedAtUtc", "publication"}, "release")
    generated = release["generatedAtUtc"]
    if not isinstance(generated, str) or not generated.endswith("Z"):
        fail("release.generatedAtUtc must be an RFC3339 UTC timestamp ending in Z")
    try:
        datetime.fromisoformat(generated.replace("Z", "+00:00"))
    except ValueError:
        fail("release.generatedAtUtc is not a valid timestamp")

    publication = require_exact_keys(
        release["publication"], {"provider", "repository", "tag"}, "release.publication"
    )
    if publication["provider"] != "github-releases":
        fail("release.publication.provider must be github-releases")
    require_uri(publication["repository"], "release.publication.repository", github_org=True)
    if version.startswith("0.2.0") and publication["repository"] != CANONICAL_RELEASE_REPOSITORY:
        fail(
            "MEM 0.2.0 release.publication.repository must be "
            f"{CANONICAL_RELEASE_REPOSITORY}"
        )
    if publication["tag"] != f"v{version}":
        fail("release.publication.tag must equal v<product.version>")

    sources = doc["sources"]
    if not isinstance(sources, list) or not sources:
        fail("sources must be a non-empty array")
    source_ids: set[str] = set()
    for index, item in enumerate(sources):
        source = require_exact_keys(item, {"id", "repository", "commit"}, f"sources[{index}]")
        source_id = source["id"]
        if not isinstance(source_id, str) or not SOURCE_ID.fullmatch(source_id):
            fail(f"sources[{index}].id is invalid")
        if source_id in source_ids:
            fail(f"duplicate source id: {source_id}")
        source_ids.add(source_id)
        require_uri(source["repository"], f"sources[{index}].repository")
        if not isinstance(source["commit"], str) or not COMMIT.fullmatch(source["commit"]):
            fail(f"sources[{index}].commit must be a lowercase 40-64 hex commit id")

    if version.startswith("0.2.0"):
        if len(sources) != 1:
            fail("MEM 0.2.0 sources must contain exactly the control-plane monorepo")
        source = sources[0]
        if (
            source["id"] != CANONICAL_SOURCE_ID
            or source["repository"] != CANONICAL_SOURCE_REPOSITORY
        ):
            fail(
                "MEM 0.2.0 source must be control-plane at "
                f"{CANONICAL_SOURCE_REPOSITORY}"
            )

    support = require_exact_keys(doc["support"], {"controlPlaneHosts"}, "support")
    hosts = support["controlPlaneHosts"]
    if not isinstance(hosts, list) or not hosts:
        fail("support.controlPlaneHosts must be non-empty")
    normalized_hosts = []
    for index, host_obj in enumerate(hosts):
        host = require_exact_keys(
            host_obj, {"os", "version", "architecture"}, f"support.controlPlaneHosts[{index}]"
        )
        normalized_hosts.append(host)

    if version.startswith("0.2.0"):
        expected_host = {"os": "ubuntu", "version": "24.04", "architecture": "amd64"}
        if normalized_hosts != [expected_host]:
            fail("MEM 0.2.0 Control Plane host support must be exactly Ubuntu 24.04 amd64")

    control_plane = require_exact_keys(doc["controlPlane"], {"sourceId", "image"}, "controlPlane")
    if control_plane["sourceId"] not in source_ids:
        fail("controlPlane.sourceId does not identify a declared source")

    image = require_exact_keys(
        control_plane["image"], {"repository", "tag", "digest", "reference"}, "controlPlane.image"
    )
    expected_repo = "ghcr.io/message-easy-mode/mem-control-plane"
    if image["repository"] != expected_repo:
        fail(f"controlPlane.image.repository must be {expected_repo}")
    if image["tag"] != version:
        fail("controlPlane.image.tag must equal product.version")
    if not isinstance(image["digest"], str) or not DIGEST.fullmatch(image["digest"]):
        fail("controlPlane.image.digest must be sha256:<64 lowercase hex>")
    expected_ref = f"{expected_repo}:{version}@{image['digest']}"
    if image["reference"] != expected_ref:
        fail("controlPlane.image.reference must be the exact version+digest reference")

    artifacts = doc["artifacts"]
    if not isinstance(artifacts, list) or not artifacts:
        fail("artifacts must be a non-empty array")
    artifact_ids: set[str] = set()
    filenames: set[str] = set()

    for index, artifact_obj in enumerate(artifacts):
        if not isinstance(artifact_obj, dict):
            fail(f"artifacts[{index}] must be an object")
        allowed = {"id", "kind", "fileName", "sha256", "sizeBytes", "sourceId", "runtime", "platform"}
        extra = set(artifact_obj) - allowed
        required = {"id", "kind", "fileName", "sha256", "sizeBytes", "sourceId"}
        missing = required - set(artifact_obj)
        if missing:
            fail(f"artifacts[{index}] is missing: {', '.join(sorted(missing))}")
        if extra:
            fail(f"artifacts[{index}] contains unsupported keys: {', '.join(sorted(extra))}")

        artifact_id = artifact_obj["id"]
        if artifact_id in artifact_ids:
            fail(f"duplicate artifact id: {artifact_id}")
        artifact_ids.add(artifact_id)

        if artifact_id in EXPECTED_KINDS and artifact_obj["kind"] != EXPECTED_KINDS[artifact_id]:
            fail(f"artifact {artifact_id} has the wrong kind")

        filename = artifact_obj["fileName"]
        if not isinstance(filename, str) or not filename or "/" in filename or "\\" in filename:
            fail(f"artifact {artifact_id} fileName must be a basename")
        if filename in filenames:
            fail(f"duplicate artifact filename: {filename}")
        filenames.add(filename)
        if MUTABLE_WORDS.search(filename):
            fail(f"artifact {artifact_id} filename contains a mutable channel word: {filename}")
        if version not in filename:
            fail(f"artifact {artifact_id} filename must contain exact release version {version}")
        if artifact_id in REQUIRED_ARTIFACTS:
            expected = expected_artifact_filename(artifact_id, version)
            if filename != expected:
                fail(f"artifact {artifact_id} filename must be {expected}")

        require_sha256(artifact_obj["sha256"], f"artifact {artifact_id} sha256")
        size = artifact_obj["sizeBytes"]
        if not isinstance(size, int) or isinstance(size, bool) or size < 1:
            fail(f"artifact {artifact_id} sizeBytes must be a positive integer")
        if artifact_obj["sourceId"] not in source_ids:
            fail(f"artifact {artifact_id} sourceId does not identify a declared source")

        if artifact_id == "installer":
            platform = artifact_obj.get("platform")
            if platform != {"os": "ubuntu", "version": "24.04", "architecture": "amd64"}:
                fail("installer platform must be Ubuntu 24.04 amd64")
        if artifact_id in {"cli", "migrate"} and artifact_obj.get("runtime") != "linux-x64":
            fail(f"artifact {artifact_id} runtime must be linux-x64")

    missing_required = REQUIRED_ARTIFACTS - artifact_ids
    if missing_required:
        fail(f"manifest is missing required artifacts: {', '.join(sorted(missing_required))}")

    verification = require_exact_keys(
        doc["verification"], {"checksums", "publication", "provenance"}, "verification"
    )
    checksums = require_exact_keys(
        verification["checksums"], {"algorithm", "fileName"}, "verification.checksums"
    )
    if checksums != {"algorithm": "sha256", "fileName": "SHA256SUMS"}:
        fail("verification.checksums must require sha256 / SHA256SUMS")

    pub_policy = require_exact_keys(
        verification["publication"],
        {"provider", "immutableReleaseRequired", "releaseAttestationRequired"},
        "verification.publication",
    )
    if pub_policy != {
        "provider": "github-releases",
        "immutableReleaseRequired": True,
        "releaseAttestationRequired": True,
    }:
        fail("verification.publication does not satisfy the schema v1 immutable-release policy")

    provenance = require_exact_keys(
        verification["provenance"],
        {"provider", "fileArtifactsRequired", "containerImageRequired"},
        "verification.provenance",
    )
    if provenance != {
        "provider": "github-artifact-attestations",
        "fileArtifactsRequired": True,
        "containerImageRequired": True,
    }:
        fail("verification.provenance does not satisfy the schema v1 attestation policy")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_assets(manifest_path: Path, doc: dict, assets_dir: Path) -> None:
    if not assets_dir.is_dir():
        fail(f"assets directory does not exist: {assets_dir}")

    expected_checksums: dict[str, str] = {}

    release_file = assets_dir / "release.json"
    if manifest_path.resolve() != release_file.resolve():
        if not release_file.is_file():
            fail("assets directory must contain release.json")
        if release_file.read_bytes() != manifest_path.read_bytes():
            fail("assets release.json does not match the manifest being validated")
    expected_checksums["release.json"] = sha256_file(release_file)

    for artifact in doc["artifacts"]:
        path = assets_dir / artifact["fileName"]
        if not path.is_file():
            fail(f"missing release artifact: {artifact['fileName']}")
        actual_size = path.stat().st_size
        if actual_size != artifact["sizeBytes"]:
            fail(
                f"size mismatch for {artifact['fileName']}: "
                f"manifest={artifact['sizeBytes']} actual={actual_size}"
            )
        actual_sha = sha256_file(path)
        if actual_sha != artifact["sha256"]:
            fail(f"SHA-256 mismatch for {artifact['fileName']}")
        expected_checksums[artifact["fileName"]] = actual_sha

    checksum_path = assets_dir / "SHA256SUMS"
    if not checksum_path.is_file():
        fail("assets directory is missing SHA256SUMS")

    expected_asset_names = set(expected_checksums) | {"SHA256SUMS"}
    actual_asset_names = {
        path.name
        for path in assets_dir.iterdir()
        if path.is_file()
    }
    missing_assets = expected_asset_names - actual_asset_names
    unexpected_assets = actual_asset_names - expected_asset_names
    if missing_assets:
        fail(
            "assets directory is missing expected files: "
            + ", ".join(sorted(missing_assets))
        )
    if unexpected_assets:
        fail(
            "assets directory contains unexpected public files: "
            + ", ".join(sorted(unexpected_assets))
        )

    parsed: dict[str, str] = {}
    for line_number, raw in enumerate(checksum_path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line:
            continue
        parts = line.split(None, 1)
        if len(parts) != 2:
            fail(f"SHA256SUMS line {line_number} is malformed")
        digest, filename = parts
        filename = filename.lstrip("*")
        require_sha256(digest, f"SHA256SUMS line {line_number}")
        if "/" in filename or "\\" in filename or filename == "SHA256SUMS":
            fail(f"SHA256SUMS line {line_number} contains an unsupported filename")
        if filename in parsed:
            fail(f"SHA256SUMS contains duplicate filename: {filename}")
        parsed[filename] = digest

    if parsed != expected_checksums:
        missing = sorted(set(expected_checksums) - set(parsed))
        extra = sorted(set(parsed) - set(expected_checksums))
        wrong = sorted(
            key for key in set(parsed) & set(expected_checksums)
            if parsed[key] != expected_checksums[key]
        )
        detail = []
        if missing:
            detail.append(f"missing={','.join(missing)}")
        if extra:
            detail.append(f"extra={','.join(extra)}")
        if wrong:
            detail.append(f"wrong={','.join(wrong)}")
        fail("SHA256SUMS does not exactly match the release asset set" + (": " + " ".join(detail) if detail else ""))


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate MEM release.json")
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--assets-dir", type=Path)
    args = parser.parse_args()

    try:
        doc = load_manifest(args.manifest)
        validate_manifest(doc)
        if args.assets_dir is not None:
            validate_assets(args.manifest, doc, args.assets_dir)
    except ValueError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1

    print(f"MEM release manifest valid: {doc['product']['version']} ({doc['product']['channel']})")
    if args.assets_dir is not None:
        print(f"Release asset set valid: {args.assets_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
