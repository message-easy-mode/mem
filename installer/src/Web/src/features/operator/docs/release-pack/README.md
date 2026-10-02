# MEM Offline Documentation Pack — 0.2.0

This package contains the documentation bundled with the MEM Control Plane. The current non-legacy documentation is reviewed for 0.2.x; historical MatrixEasyMode material is packaged only as explicitly legacy content.

## Contents

- `docs/` — portable Markdown used by the in-product reader.
- `metadata/manifest.json` — document identity, status, version applicability, routes and hashes.
- `metadata/navigation.json` — sidebar/navigation model.
- `metadata/search-index.json` — offline search records.
- `MEM-KNOWLEDGE-BASE.md` — one-file concatenation for offline/AI-assisted reading.
- `AI-USAGE.md` — safe-use guidance for AI-assisted documentation work.
- `CONVERSION_REPORT.md` — preserved historical conversion notes.
- `SHA256SUMS` — checksums for the complete pack.

## Current status

- Source content version: **0.2.0**
- Current product line: **0.2.x**
- Current English documents: **89**
- Current German documents: **78**
- Explicit legacy documents: **1**
- Review required: **no**

Current non-legacy English and German documentation is reviewed for MEM 0.2.x. Historical MatrixEasyMode material is preserved only as explicitly legacy documentation.

## Operator rule

Prefer the exact installed release identity, current Diagnostics/runtime evidence, and current supported pages over historical commands. Never use a legacy Compose or `stack.sh` procedure as a MEM 0.2.x instruction unless a release-specific migration guide explicitly requires it.

## Rendering

Portable pages require CommonMark-style Markdown plus GitHub-style alerts. Raw HTML should remain disabled and local assets/links should be treated as documentation content, not executable configuration. Image references are rewritten to portable relative paths so extracted Markdown continues to render its bundled images offline.
