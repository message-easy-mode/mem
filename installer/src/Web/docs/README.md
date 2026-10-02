# MEM documentation source

This directory is the canonical authored source for the documentation bundled with the MEM Control Plane.

- `en/` contains English documents.
- `de/` contains German documents.
- `legacy/` contains historical documentation that must retain its original product identity.
- `assets/` is reserved for documentation images and other portable assets.
- `documentation-pack.json` provides deterministic pack-level metadata.

Do not hand-edit `src/features/operator/docs/release-pack/docs` or its generated metadata. Run:

```bash
npm run docs:build
```

Before committing documentation changes, verify the checked-in generated pack:

```bash
npm run docs:check
```
