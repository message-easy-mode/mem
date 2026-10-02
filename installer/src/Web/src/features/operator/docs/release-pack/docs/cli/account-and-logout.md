---
title: Inspect account status and sign out
description: Verify the named CLI session safely, understand expiry, revoke it server-side, and remove the local credential.
section: CLI and automation
order: 44
---

# Inspect account status and sign out

## Inspect the current session

```bash
mem account show --profile home
mem account show --profile home --json | jq
```

A successful result may show only safe session facts:

- profile and server;
- `authenticated` status;
- operator display name;
- assigned roles;
- idle expiry;
- absolute expiry.

It must not expose the device credential, browser cookie, installation identity, password, TOTP value, recovery code, raw claims, or secret-store entry.

`account show` verifies both local credential presence and current server acceptance. A locally stored credential is not proof that the server still accepts it.

## Interpret results

| Status/error | Meaning |
|---|---|
| `authenticated` | The server accepts the stored session |
| `signed_out` | No credential exists for this profile and server |
| `unauthenticated` | A local credential exists but the server rejected or expired it |
| `cli_account_secure_store_unavailable` | The local secret store could not be read safely |
| `cli_account_unavailable` | The Control Plane could not verify the session |

A signed-out or rejected result exits non-zero so scripts do not mistake it for authenticated state.

## Sign out

```bash
mem logout --profile home
```

MEM attempts to revoke the current server-side device session, then removes the local Secret Service credential.

Possible outcomes:

- `revoked` — server revocation was confirmed;
- `unauthenticated` — the server already rejected or expired the session;
- `unavailable` — server revocation could not be confirmed, but the local credential was removed;
- no stored session — logout is idempotent and succeeds without a server request.

If local deletion fails, logout fails rather than claiming the credential was removed.

## Verify logout

```bash
mem account show --profile home --json | jq
mem host status --profile home --json | jq
```

The account command should report signed out, and operational commands should require device login. MEM must not fall back to installer tokens or agent secrets.

## Replace a stale session

```bash
mem logout --profile home
mem login --device --profile home
mem account show --profile home
```

A Control Plane reinstall or change of the current installation identity invalidates old CLI device credentials even when the server URL is unchanged.

## Related documentation

- [Sign in with device login](device-login.md)
- [CLI troubleshooting](troubleshooting.md)
- [CLI security model](security-model.md)
