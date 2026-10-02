---
title: Inspect the host and chat servers
description: Use host status, stack inventory, safe inspection, doctor checks, and operation history without inventing unsupported lifecycle commands.
section: CLI and automation
order: 45
---

# Inspect the host and chat servers

The current CLI provides observation and diagnostic commands for the Control Plane host and managed chat servers. It does not currently create, start, stop, or remove stacks.

## Check Control Plane readiness

```bash
mem host status --profile home
mem host status --profile home --json | jq
```

Exit code `0` requires the returned host status to be `ready`. A reachable but unready result exits `2` so automation can distinguish operational health from invocation errors.

## List chat servers

```bash
mem stack list --profile home
mem stack list --profile home --json | jq
```

The list includes stable stack identity, last verified state, and public Matrix and Element URLs where recorded.

## Inspect one stack safely

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
```

The CLI deliberately projects public service facts rather than raw Docker container IDs, host paths, environment values, or secret-bearing runtime configuration.

## Run doctor checks

```bash
mem stack doctor <slug-or-id> --profile home
mem stack doctor <slug-or-id> --profile home --json | jq
```

Doctor records or reads a server-owned diagnostic operation and returns structured checks. Exit code `0` means all returned checks passed. Exit code `2` means at least one check failed or the requested operational result was not successful.

A failed doctor check is evidence, not permission to mutate Docker manually. Review the check code, URL, status, operation ID, report ID, and safe detail before choosing a repair.

## Review operation history

```bash
mem stack operations <slug-or-id> --profile home --json | jq
```

Operation history can show requested action, status, requester, mutation level, current step, timestamps, idempotency key, and a safe last-error summary.

## Unsupported commands

The following are not in the current release command inventory:

```text
mem stack create
mem stack start
mem stack stop
mem stack delete
mem stack compare
```

Use the current Control Plane UI and documented operator workflow for lifecycle changes.

## Evidence bundle

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Share only the evidence required for the incident. Remove private hostnames where necessary and never include credentials, passwords, TOTP or recovery codes, signing keys, private keys, raw backup contents, or Secret Service output.

## Related documentation

- [JSON output and scripting](json-and-scripting.md)
- [CLI troubleshooting](troubleshooting.md)
- [Operate chat servers](../chat-servers/index.md)
