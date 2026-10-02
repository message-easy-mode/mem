# Using this MEM documentation pack with AI

This pack is generic product documentation for MEM 0.2.0. It is not a live configuration export.

## Recommended input

Use `MEM-KNOWLEDGE-BASE.md` for broad ingestion or selected files under `docs/` for a narrower question.

## Grounding rules

- Treat non-legacy pages as current guidance for 0.2.x.
- Treat pages marked `legacy` as historical context only.
- Confirm commands against the installed MEM version, current UI, runtime evidence, and release-specific notes before production mutation.
- Prefer server-owned Diagnostics, operation IDs, support reports, and safe CLI JSON over guesses from container names or ports.

## Never request or upload secrets

Do not ask users to upload passwords, TOTP secrets, recovery codes, bearer credentials, setup tokens, TURN shared secrets, signing keys, certificate private keys, Secret Service output, raw database dumps, unrestricted environment/configuration files, or backup payload contents.

## Production path context

Current production documentation distinguishes private Control Plane state under `/data`, host-visible MEM data under `/var/lib/message-easy-mode`, and installed software under `/opt/mem`. Do not recommend developer-home paths as production fixes.

## Machine contracts

Command names, flags, environment variables, JSON fields, status values, and error codes remain English machine contracts even when human-facing documentation is German.
