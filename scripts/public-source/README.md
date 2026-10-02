# MEM Public Source Producer

This directory contains the controlled producer for the public source form of Message Easy Mode.

`PUBLIC-SOURCE-01A` implements deterministic **preparation**. `PUBLIC-SOURCE-01B` adds fail-closed exposure scanning and isolated build/test qualification. Neither phase can publish source.

## Prepare

From the MEM repository root, using the exact committed source you intend to qualify:

```bash
SOURCE_COMMIT="$(git rev-parse HEAD)"
OUTPUT_DIR="/tmp/mem-public-source-0.2.0"

./scripts/public-source/prepare-public-source.sh \
  --version 0.2.0 \
  --source-commit "$SOURCE_COMMIT" \
  --output-dir "$OUTPUT_DIR"
```

The output directory must not already exist. An output path inside the repository is accepted only when Git confirms that path is ignored.

## Review

After preparation:

```bash
find "$OUTPUT_DIR/source" -maxdepth 2 -type f -print | sort
cat "$OUTPUT_DIR/public-source.json"
cat "$OUTPUT_DIR/evidence/included-top-level.txt"
cat "$OUTPUT_DIR/evidence/excluded-top-level.txt"
cat "$OUTPUT_DIR/evidence/excluded-paths.txt"
sha256sum -c "$OUTPUT_DIR/SHA256SUMS"
```

The source tree under `source/` is the candidate public tree to review and later qualify. Do not publish it merely because preparation passed.

## Validate manifest

```bash
./scripts/public-source/validate-public-source-manifest.py \
  "$OUTPUT_DIR/public-source.json"
```

## Tests

```bash
./scripts/public-source/tests/public-source-prepare-tests.sh
```

The tests build disposable Git repositories and verify clean-source requirements, explicit top-level classification, reviewed nested exclusions, forbidden tracked files, symlink rejection, exact-commit selection, manifest integrity, and deterministic output.

## Policy changes

`public-source-top-level-policy.tsv` is intentionally source-controlled. If preparation reports an unclassified top-level repository path, do not weaken the producer or add a broad wildcard. Review the path and explicitly decide whether it belongs in the public source contract.

`public-source-path-exclusions.txt` is the separate reviewed policy for tracked paths that live inside an otherwise-public root but should be omitted. Do not use wildcards or broad directory patterns to silence a safety failure.

See `PUBLIC-SOURCE-CONTRACT.md` for the full 01A contract and the 01B/01C boundaries.

## Verify the prepared source (PUBLIC-SOURCE-01B)

Verification consumes the output directory produced by `prepare-public-source.sh`; it does not read source from the private Git worktree.

Start with the exposure scan:

```bash
VERIFY_DIR="/tmp/mem-public-source-0.2.0-verification"
rm -rf -- "$VERIFY_DIR"

./scripts/public-source/verify-public-source.sh \
  --prepared-dir "$OUTPUT_DIR" \
  --output-dir "$VERIFY_DIR" \
  --scan-only
```

The scan re-validates `SHA256SUMS`, the public-source manifest, and every source file against `evidence/file-manifest.tsv` before inspecting text. Blocking findings include known personal/private repository residue, obsolete MEM source-repository identity, private-key material, several high-confidence token/key shapes, npm auth-token assignments, and credential-bearing URLs. RFC1918 addresses are retained as review findings rather than automatic failures.

The exact-path/rule exemptions in `public-source-content-allowlist.tsv` are for deliberately reviewed fixtures only. Wildcards are not supported. Do not add an allowlist entry merely to make a release pass.

After the scan is clean, run the isolated build/test qualification:

```bash
VERIFY_DIR="/tmp/mem-public-source-0.2.0-full-verification"
rm -rf -- "$VERIFY_DIR"

./scripts/public-source/verify-public-source.sh \
  --prepared-dir "$OUTPUT_DIR" \
  --output-dir "$VERIFY_DIR" \
  --full
```

Full mode copies only `source/` into a disposable workspace, uses isolated HOME/NuGet/npm caches, pins npm to the public npm registry and .NET restore to `nuget.org`, and runs:

1. public-source producer fixture regression;
2. `MemInstaller.sln` restore/build/test;
3. MEM Web `npm ci`, tests and production build;
4. `Mem.Migrate.sln` restore/build/test;
5. MEM Migrate Web client `npm ci`, tests and production build.

To additionally prove the production-shaped Control Plane image can be built from only the exported installer and Migrate trees:

```bash
./scripts/public-source/verify-public-source.sh \
  --prepared-dir "$OUTPUT_DIR" \
  --output-dir "$VERIFY_DIR" \
  --full \
  --container-build
```

Full verification may use public dependency/container registries. It still performs no Git commit, tag, remote write, GitHub API action, or publication.

Verification evidence is written separately from the prepared artifact so the 01A archive/tree evidence remains immutable.

## Verification tests

```bash
./scripts/public-source/tests/public-source-verification-tests.sh
```

These fixture tests prove integrity re-checking, blocker/review classification, exact allowlist behaviour, scan-only orchestration, and isolated full-build orchestration with a fake toolchain.

