#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import stat
import sys
from dataclasses import dataclass
from typing import Iterable


@dataclass(frozen=True)
class Rule:
    rule_id: str
    severity: str
    kind: str
    expression: str
    description: str
    secret: bool = False


def fail(message: str) -> None:
    raise SystemExit(f"ERROR: {message}")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_path(path: pathlib.Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def rules() -> tuple[Rule, ...]:
    # Build environment-specific literals from components so this scanner does
    # not flag its own implementation when it scans the public source tree.
    personal_home = "/" + "home" + "/" + "master" + "/"
    legacy_repo = "MatrixEasyMode" + "/Internal/" + "matrix-easy-mode-deploy"
    private_host = "dev" + "." + "vs4" + "." + "one"
    obsolete_source_repo = (
        "https://github.com/message-easy-mode/" + "mem-control-plane"
    )
    return (
        Rule(
            "personal-home-master",
            "BLOCK",
            "literal",
            personal_home,
            "personal workstation home path",
        ),
        Rule(
            "legacy-repository-path",
            "BLOCK",
            "literal",
            legacy_repo,
            "legacy private repository path/name",
        ),
        Rule(
            "historical-private-scm-host",
            "BLOCK",
            "literal",
            private_host,
            "historical private SCM hostname",
        ),
        Rule(
            "obsolete-public-source-repository",
            "BLOCK",
            "literal",
            obsolete_source_repo,
            "obsolete GitHub source repository URL",
        ),
        Rule(
            "private-key-header",
            "BLOCK",
            "regex",
            r"-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----",
            "private-key PEM/OpenSSH header",
            secret=True,
        ),
        Rule(
            "github-token",
            "BLOCK",
            "regex",
            r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b",
            "GitHub access token shape",
            secret=True,
        ),
        Rule(
            "aws-access-key",
            "BLOCK",
            "regex",
            r"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b",
            "AWS access-key identifier shape",
            secret=True,
        ),
        Rule(
            "npm-auth-token",
            "BLOCK",
            "regex",
            r"(?i)_authToken\s*=\s*[^\s#]+",
            "npm authentication token assignment",
            secret=True,
        ),
        Rule(
            "credentialed-url",
            "BLOCK",
            "regex",
            r"https?://[^/\s:@]+:[^/\s@]+@[^\s/]+",
            "URL containing embedded username/password credentials",
            secret=True,
        ),
        Rule(
            "rfc1918-address",
            "REVIEW",
            "regex",
            r"(?<![0-9])(?:10(?:\.[0-9]{1,3}){3}|192\.168(?:\.[0-9]{1,3}){2}|172\.(?:1[6-9]|2[0-9]|3[01])(?:\.[0-9]{1,3}){2})(?![0-9])",
            "private IPv4 address",
        ),
    )


def parse_manifest(path: pathlib.Path) -> dict:
    try:
        doc = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Could not read public-source manifest: {exc}")
    if not isinstance(doc, dict):
        fail("Public-source manifest must be a JSON object.")
    return doc


def parse_file_manifest(path: pathlib.Path) -> list[tuple[str, int, str, str]]:
    rows: list[tuple[str, int, str, str]] = []
    seen: set[str] = set()
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        fail(f"Could not read file manifest: {exc}")
    for index, line in enumerate(lines, 1):
        parts = line.split("\t")
        if len(parts) != 4:
            fail(f"Malformed file-manifest row {index}.")
        digest, size_text, mode, rel = parts
        if not re.fullmatch(r"[0-9a-f]{64}", digest):
            fail(f"Malformed SHA-256 in file-manifest row {index}.")
        try:
            size = int(size_text)
        except ValueError:
            fail(f"Malformed size in file-manifest row {index}.")
        if size < 0 or mode not in ("0644", "0755"):
            fail(f"Malformed size/mode in file-manifest row {index}.")
        if not rel or rel.startswith("/") or "\\" in rel:
            fail(f"Unsafe path in file-manifest row {index}: {rel!r}")
        pure = pathlib.PurePosixPath(rel)
        if any(part in ("", ".", "..") for part in pure.parts):
            fail(f"Unsafe path in file-manifest row {index}: {rel!r}")
        if rel in seen:
            fail(f"Duplicate path in file manifest: {rel}")
        seen.add(rel)
        rows.append((digest, size, mode, rel))
    if not rows:
        fail("File manifest is empty.")
    if [row[3] for row in rows] != sorted(row[3] for row in rows):
        fail("File manifest paths are not in canonical bytewise order.")
    return rows


def verify_source_tree(
    source_root: pathlib.Path,
    manifest_doc: dict,
    file_manifest_path: pathlib.Path,
) -> list[tuple[str, int, str, str]]:
    rows = parse_file_manifest(file_manifest_path)
    expected_paths = {row[3] for row in rows}
    actual_paths: set[str] = set()

    for dirpath, dirnames, filenames in os.walk(source_root, followlinks=False):
        current = pathlib.Path(dirpath)
        for name in list(dirnames):
            path = current / name
            rel = path.relative_to(source_root).as_posix()
            if path.is_symlink():
                fail(f"Public source contains a symlink directory: {rel}")
            if not path.is_dir():
                fail(f"Public source contains an unsupported directory entry: {rel}")
        for name in filenames:
            path = current / name
            rel = path.relative_to(source_root).as_posix()
            if path.is_symlink():
                fail(f"Public source contains a symlink file: {rel}")
            mode = path.lstat().st_mode
            if not stat.S_ISREG(mode):
                fail(f"Public source contains an unsupported filesystem entry: {rel}")
            actual_paths.add(rel)

    if actual_paths != expected_paths:
        missing = sorted(expected_paths - actual_paths)
        extra = sorted(actual_paths - expected_paths)
        details: list[str] = []
        if missing:
            details.append("missing=" + ", ".join(missing[:10]))
        if extra:
            details.append("extra=" + ", ".join(extra[:10]))
        fail("Prepared source tree no longer matches file manifest: " + "; ".join(details))

    for digest, expected_size, expected_mode, rel in rows:
        path = source_root / rel
        data = path.read_bytes()
        if len(data) != expected_size:
            fail(f"Prepared source size drift: {rel}")
        if sha256_bytes(data) != digest:
            fail(f"Prepared source SHA-256 drift: {rel}")
        actual_mode = "0755" if path.stat().st_mode & stat.S_IXUSR else "0644"
        if actual_mode != expected_mode:
            fail(f"Prepared source executable-mode drift: {rel}")

    raw_manifest = file_manifest_path.read_bytes()
    tree_sha = sha256_bytes(raw_manifest)
    contents = manifest_doc.get("contents")
    if not isinstance(contents, dict):
        fail("public-source.json contents must be an object.")
    if contents.get("fileCount") != len(rows):
        fail("public-source.json contents.fileCount does not match prepared tree.")
    if contents.get("treeSha256") != tree_sha:
        fail("public-source.json contents.treeSha256 does not match file manifest.")
    return rows


def parse_allowlist(path: pathlib.Path, known_rules: set[str]) -> set[tuple[str, str]]:
    allowed: set[tuple[str, str]] = set()
    if not path.is_file() or path.is_symlink():
        fail(f"Content allowlist is missing or unsafe: {path}")
    for index, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = raw.split("\t")
        if len(parts) != 2:
            fail(f"Malformed allowlist row {index}; expected rule-id<TAB>path.")
        rule_id, rel = (part.strip() for part in parts)
        if rule_id not in known_rules:
            fail(f"Unknown allowlist rule id on row {index}: {rule_id}")
        if not rel or rel.startswith("/") or "\\" in rel or "*" in rel or "?" in rel:
            fail(f"Allowlist paths must be exact repository-relative paths: {rel!r}")
        pure = pathlib.PurePosixPath(rel)
        if any(part in ("", ".", "..") for part in pure.parts):
            fail(f"Unsafe allowlist path on row {index}: {rel!r}")
        key = (rule_id, rel)
        if key in allowed:
            fail(f"Duplicate allowlist entry: {rule_id} {rel}")
        allowed.add(key)
    return allowed


def match_rule(rule: Rule, line: str) -> bool:
    if rule.kind == "literal":
        return rule.expression in line
    if rule.kind == "regex":
        return re.search(rule.expression, line) is not None
    raise AssertionError(rule.kind)


def safe_preview(rule: Rule, line: str) -> str:
    if rule.secret:
        return "[redacted secret-like content]"
    compact = " ".join(line.strip().split())
    return compact[:180]


def write_tsv(path: pathlib.Path, findings: Iterable[dict]) -> None:
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write("severity\truleId\tpath\tline\tdisposition\tpreview\n")
        for item in findings:
            preview = str(item["preview"]).replace("\t", " ").replace("\n", " ")
            handle.write(
                f"{item['severity']}\t{item['ruleId']}\t{item['path']}\t"
                f"{item['line']}\t{item['disposition']}\t{preview}\n"
            )


def main() -> int:
    parser = argparse.ArgumentParser(description="Audit a prepared MEM public-source tree.")
    parser.add_argument("--source-root", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument("--file-manifest", required=True)
    parser.add_argument("--allowlist", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--summary", required=True)
    args = parser.parse_args()

    source_root = pathlib.Path(args.source_root).resolve()
    manifest_path = pathlib.Path(args.manifest).resolve()
    file_manifest_path = pathlib.Path(args.file_manifest).resolve()
    allowlist_path = pathlib.Path(args.allowlist).resolve()
    report_path = pathlib.Path(args.report).resolve()
    summary_path = pathlib.Path(args.summary).resolve()

    if not source_root.is_dir() or source_root.is_symlink():
        fail(f"Prepared source root is missing or unsafe: {source_root}")
    if not manifest_path.is_file() or manifest_path.is_symlink():
        fail(f"Public-source manifest is missing or unsafe: {manifest_path}")
    if not file_manifest_path.is_file() or file_manifest_path.is_symlink():
        fail(f"File manifest is missing or unsafe: {file_manifest_path}")

    report_path.parent.mkdir(parents=True, exist_ok=True)
    summary_path.parent.mkdir(parents=True, exist_ok=True)

    manifest_doc = parse_manifest(manifest_path)
    rows = verify_source_tree(source_root, manifest_doc, file_manifest_path)

    active_rules = rules()
    known_rule_ids = {rule.rule_id for rule in active_rules}
    allowlist = parse_allowlist(allowlist_path, known_rule_ids)
    used_allowlist: set[tuple[str, str]] = set()

    findings: list[dict] = []
    text_files = 0
    binary_files = 0

    for _, _, _, rel in rows:
        path = source_root / rel
        data = path.read_bytes()
        if b"\x00" in data:
            binary_files += 1
            continue
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError:
            binary_files += 1
            continue
        text_files += 1
        for line_number, line in enumerate(text.splitlines(), 1):
            for rule in active_rules:
                if not match_rule(rule, line):
                    continue
                key = (rule.rule_id, rel)
                allowed = key in allowlist
                if allowed:
                    used_allowlist.add(key)
                findings.append(
                    {
                        "severity": rule.severity,
                        "ruleId": rule.rule_id,
                        "path": rel,
                        "line": line_number,
                        "disposition": "ALLOWLISTED" if allowed else "ACTIVE",
                        "preview": safe_preview(rule, line),
                    }
                )

    stale_allowlist = sorted(allowlist - used_allowlist)
    if stale_allowlist:
        fail(
            "Stale content allowlist entries do not suppress current findings: "
            + ", ".join(f"{rule}:{path}" for rule, path in stale_allowlist)
        )

    write_tsv(report_path, findings)
    block_active = [
        item for item in findings
        if item["severity"] == "BLOCK" and item["disposition"] == "ACTIVE"
    ]
    review_active = [
        item for item in findings
        if item["severity"] == "REVIEW" and item["disposition"] == "ACTIVE"
    ]
    allowed_count = sum(1 for item in findings if item["disposition"] == "ALLOWLISTED")

    source = manifest_doc.get("source", {})
    contents = manifest_doc.get("contents", {})
    summary = {
        "schemaVersion": 1,
        "status": "fail" if block_active else "pass",
        "sourceCommit": source.get("commit"),
        "sourceTree": source.get("tree"),
        "publicTreeSha256": contents.get("treeSha256"),
        "fileCount": len(rows),
        "textFilesScanned": text_files,
        "binaryFilesSkipped": binary_files,
        "blockFindings": len(block_active),
        "reviewFindings": len(review_active),
        "allowlistedFindings": allowed_count,
        "report": report_path.name,
    }
    summary_path.write_text(json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(
        "Public-source exposure scan: "
        f"{len(block_active)} block, {len(review_active)} review, "
        f"{allowed_count} allowlisted findings across {text_files} text files."
    )
    if block_active:
        for item in block_active[:20]:
            print(
                f"BLOCK {item['ruleId']}: {item['path']}:{item['line']} "
                f"({item['preview']})",
                file=sys.stderr,
            )
        if len(block_active) > 20:
            print(f"... {len(block_active) - 20} more blocking findings; see {report_path}", file=sys.stderr)
        return 3
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
