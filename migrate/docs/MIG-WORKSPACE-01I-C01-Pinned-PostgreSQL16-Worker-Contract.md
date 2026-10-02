# MIG-WORKSPACE-01I-C01 — Pinned PostgreSQL 16 Worker Contract

The conversion worker request schema is version 2. It requires `postgresImage` to be an immutable local Docker image identity:

- `sha256:<64 hex>` local image ID; or
- `<repository>@sha256:<64 hex>` repository digest.

Mutable tags such as `postgres:16` are rejected. The source-captured PostgreSQL image remains provenance only; it is not selected as the target conversion runtime.
