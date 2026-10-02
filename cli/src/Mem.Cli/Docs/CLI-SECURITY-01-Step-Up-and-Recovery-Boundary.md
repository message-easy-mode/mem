# CLI-SECURITY-01 — Step-Up and Recovery Boundary Close-Out

## Purpose

This release close-out records the current MEM CLI security posture for two
sensitive surfaces:

1. server-enforced recent identity verification for high-risk operations; and
2. the future host-local `sudo mem auth arm-recovery` recovery command.

The CLI must not create a second authentication system while those server and
host-local contracts mature. It therefore fails closed when the control plane
requires stronger authority and keeps the recovery command transport-free until
the SEC-AUTH-07A local bridge exists.

## Current release behavior

Normal operational commands use the named device session created by:

```bash
mem login --device --profile <name>
```

The stored bearer credential is read from the operating-system Secret Service
provider for the selected profile and sent to the private MEM control plane.
The control plane remains authoritative for roles, capabilities, audit, and any
recent step-up requirement.

When a protected operation returns HTTP 403, the CLI presents a safe failure
message. It does not parse or echo raw control-plane response bodies. The
message tells the operator that the operation may require a stronger role or
recent identity verification, and that MEM CLI fails closed.

The CLI intentionally does not accept any of the following as command-line
options:

```text
--password
--totp
--totp-code
--recovery-code
--device-credential
--bearer-token
```

It also does not define environment-variable or stdin-based shortcuts for those
values. A high-risk automation/service-principal model remains a separate
security design, not a CLI flag.

## Recovery command boundary

The spelling remains reserved:

```bash
sudo mem auth arm-recovery
```

In this build the command is not implemented. It returns a stable failure
without constructing the normal HTTP/control-plane client.

The command also rejects remote/profile options such as:

```text
--server
--host-agent-url
--profile
```

That preserves the intended future shape: recovery arming is a local-host-only
operation backed by the SEC-AUTH-07A Unix-socket/named-pipe bridge. It must not
become a remote HTTPS action, browser-terminal privilege, installer-token
resurrection, raw recovery-grant printout, or direct SQLite edit.

## Definition of done for this slice

- `mem auth arm-recovery --json` fails safely with a stable recovery-specific
  code.
- `mem auth arm-recovery --server ... --json` rejects remote options without a
  network request.
- Operational commands reject password/TOTP/recovery/token material supplied as
  options before constructing credential stores or HTTP clients.
- HTTP 403 results are safe and step-up-aware without echoing raw response
  bodies.
- Release docs clearly state that interactive CLI step-up is not shipped in
  this build; the CLI fails closed until an approved flow exists.
