# MEM Public Source Contract

**Status:** PUBLIC-SOURCE-01A preparation + PUBLIC-SOURCE-01B verification  
**Scope:** deterministic public-source preparation, exposure scanning, and isolated build/test qualification  
**Publication:** deliberately out of scope until PUBLIC-SOURCE-01C

## 1. Purpose

Message Easy Mode development may contain working history, local development state, internal-only repository roots, and release engineering material that must not become public merely because it exists in the engineering repository.

`PUBLIC-SOURCE-01A` establishes an explicit source-production boundary:

```text
approved clean MEM source commit
        |
        | prepare-public-source.sh
        v
classified tracked source
        |
        | deterministic export
        v
reviewable public-source tree + archive + evidence
```

The prepared tree is an artifact. It is **not publication**.

## 2. Non-negotiable preparation invariants

The producer must:

1. run against a readable Git worktree;
2. require a clean source worktree;
3. require the selected full source commit to equal `HEAD`;
4. derive its source timestamp from that Git commit;
5. classify every tracked top-level repository path as `include` or `exclude`;
6. apply only explicitly reviewed nested-path exclusions inside included roots;
7. fail when a tracked top-level path is unclassified or an unsafe nested path is not explicitly excluded;
8. export bytes from the selected Git commit, not from mutable working-tree copies;
9. reject symlinks, special files, obvious local-state/secret-file classes, and generated build trees inside the public selection;
10. generate deterministic archive bytes for the same commit and policy;
11. emit manifest/checksum/file-list evidence;
12. re-check source HEAD and cleanliness before completion; and
13. perform no network, Git commit, Git tag, Git remote write, or publication operation.

## 3. Top-level publication policy

`public-source-top-level-policy.tsv` is the publication boundary for tracked repository roots.

The initial MEM 0.2.0 intent is:

### Included

- `bootstrap/`
- `cli/`
- `dev/`
- `images/`
- `installer/`
- `migrate/`
- `scripts/`
- public root project/license/support documents
- the public installation entrypoint `install.sh` when tracked

### Excluded

- `.github/` — hosting/release automation is not copied merely because GitHub is used as a publication surface;
- `.mem-slice-backups/` — local slice recovery state;
- `.vscode/` — workstation/editor configuration;
- `apply-slice.sh` — internal slice-delivery tooling rather than released product source;
- `legacy/` — historical/legacy source is not part of the MEM 0.2.0 public build surface.

An excluded path remains listed deliberately so that, if it is tracked, the producer records that it was considered and omitted.

If the real repository contains another tracked top-level path, preparation fails until that path is reviewed and classified. This is intentional.

## 4. Explicit nested exclusions

`public-source-path-exclusions.txt` records reviewed tracked paths that live inside an otherwise public top-level root but are not part of the public product source. Directory exclusions end with `/`; wildcards are deliberately unsupported.

The reviewed MEM 0.2.0 nested exclusions are:

- `installer/.vscode/` — editor/workstation configuration inside the otherwise-public installer tree.
- `installer/src/.vscode/` — editor/workstation configuration inside the otherwise-public installer source tree.

The producer removes only these exact reviewed paths before running its fail-closed source-surface safety scan. A new nested `.vscode`, build-state, or similar path elsewhere therefore still fails until it is deliberately reviewed.

## 5. Baseline forbidden path classes

01A rejects tracked material within included roots when it contains ordinary local/build-state path segments such as:

- `.state`, `.vscode`, `.vs`, `.git`, `.mem-slice-backups`;
- `node_modules`, `bin`, `obj`, `dist`, `coverage`, `test-results`, `TestResults`;
- `.env` / `.env.*` files;
- database/state files (`*.db`, `*.sqlite*`);
- private-key/certificate-container-like files (`*.key`, `*.p12`, `*.pfx`, `*.pem`).

These checks are deliberately conservative. A legitimate public file that collides with a forbidden class should be reviewed and the contract deliberately amended; the producer must not silently bypass the check.

Deeper secret/token/content scanning is `PUBLIC-SOURCE-01B` work.

## 6. Output contract

For version `<version>`, a successful preparation produces a new output directory:

```text
<output>/
├── source/                         # reviewable public source tree
├── mem-<version>-source.tar.gz     # deterministic archive of source/
├── public-source.json              # source/export/artifact evidence
├── SHA256SUMS
└── evidence/
    ├── file-manifest.tsv           # SHA-256, size, mode, path
    ├── included-top-level.txt
    ├── excluded-top-level.txt
    └── excluded-paths.txt
```

`contents.treeSha256` in `public-source.json` is the SHA-256 of the normalized `file-manifest.tsv` record stream. It identifies the prepared public tree independent of a future public Git commit.

## 7. Determinism

For the same:

- exact source commit;
- exact publication policy; and
- producer implementation from that commit,

the source archive and evidence must be byte-for-byte reproducible.

Archive metadata is normalized: file ordering, owner/group, mtime, and gzip timestamp are deterministic. Executable/non-executable mode is preserved at Git-level semantics (`0755`/`0644`).

## 8. Future verification and publication boundaries

01A deliberately stops after preparation and review.

Planned follow-on boundaries:

```text
PUBLIC-SOURCE-01B
    deeper exposure/secret scanning
    isolated build/test qualification from exported source only

PUBLIC-SOURCE-01C
    safe separate-public-clone preparation
    sandbox publication proof against:
        https://github.com/message-easy-mode/mem-sandbox
    final production destination after qualification:
        https://github.com/message-easy-mode/mem
```

The production repository must not be enabled merely by changing a URL in 01A. Publication authority belongs to 01C after sandbox proof.

## 9. PUBLIC-SOURCE-01B verification contract

01B consumes an already prepared 01A artifact. It must not silently regenerate or replace that artifact.

Before content scanning it must:

1. verify the prepared `SHA256SUMS`;
2. validate `public-source.json`;
3. prove that every regular file beneath `source/` still has the exact path, SHA-256, size and executable mode recorded by `evidence/file-manifest.tsv`;
4. prove there are no additional/missing files, symlinks or special filesystem entries; and
5. prove `contents.fileCount` and `contents.treeSha256` still identify that exact tree.

### 9.1 Exposure scan

The mandatory built-in scan is deliberately narrow and high-confidence. Blocking rules cover:

- the known personal development home prefix used during MEM development;
- the retired private repository path/name;
- the retired private SCM hostname;
- the obsolete GitHub `mem-control-plane` source-repository URL now superseded by `message-easy-mode/mem`;
- private-key headers;
- high-confidence GitHub token and AWS access-key shapes;
- npm `_authToken` assignments; and
- URLs containing embedded username/password credentials.

RFC1918 IPv4 addresses are reported for human review because private addresses are legitimate in network tests and deployment examples and therefore cannot safely be rejected globally.

`public-source-content-allowlist.tsv` may contain only exact `rule-id + repository-relative-path` exemptions. Wildcards are prohibited. An exemption is appropriate only for a reviewed security/negative-test fixture whose sensitive-looking content is intentionally synthetic. Production/configuration/documentation residue must be corrected rather than allowlisted.

### 9.2 Isolated build/test qualification

A successful scan is a hard prerequisite for full verification.

Full verification copies only the prepared `source/` tree into a disposable workspace. It deliberately relocates HOME, .NET CLI state, NuGet packages and npm cache away from the developer workstation and forces dependency restore through public `nuget.org` / `registry.npmjs.org` endpoints.

The minimum full gate is:

```text
public-source prepare fixture regression
MemInstaller.sln restore -> build -> test
MEM Web npm ci -> test -> build
Mem.Migrate.sln restore -> build -> test
MEM Migrate Web client npm ci -> test -> build
```

An optional `--container-build` gate uses only the copied exported `installer/` tree plus the exported `migrate/` named BuildKit context to build the production-shaped Control Plane image. No private source checkout is mounted into the build.

The verifier writes logs and JSON/TSV evidence to a separate verification output directory. It may contact public package/container registries in full mode, but it has no Git remote-write or publication operation.

### 9.3 Expected first-run behaviour

01B is intentionally capable of failing the current tree. A first real scan is expected to expose any remaining workstation-specific paths, old source-repository identity, documentation residue, or deliberately synthetic security fixtures that still need either correction or an exact reviewed allowlist entry. Those findings are a cleanup queue, not a reason to weaken the verifier.

## 10. Publication remains PUBLIC-SOURCE-01C

01B still cannot create a Git commit/tag, alter a remote, call GitHub publication APIs, or push source. Sandbox publication to `message-easy-mode/mem-sandbox` remains 01C work after 01B passes on the exact release source.

