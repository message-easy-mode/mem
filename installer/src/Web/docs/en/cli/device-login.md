---
id: "cli/device-login"
translationKey: "cli/device-login"
locale: "en"
groupId: "cli"
groupKey: "cli"
groupLabel: "CLI and automation"
groupOrder: 80
title: "Sign in with device login"
description: "Authorize a named CLI device through the private browser UI and store its opaque session credential in the operating-system secret store."
order: 43
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "device login", "browser approval", "Secret Service", "MFA"]
route: "/docs/cli/device-login"
aliases: []
outputPath: "docs/cli/device-login.md"
preserveLegacyBranding: false
---
# Sign in with device login

Device login is the normal MEM CLI authority path. It connects a selected local profile to a named MEM operator without placing passwords, TOTP codes, or bearer credentials on the command line.

## Before you begin

- Create or select a valid profile.
- Confirm the profile reaches the private Control Plane.
- Run the command as the normal non-root operator.
- Ensure `secret-tool` and an unlocked Secret Service-compatible keyring are available.
- Have an existing browser session for a named MEM operator with TOTP configured.

## Start login

```bash
mem login --device --profile home
```

`--json` is intentionally not supported because the command is an interactive, multi-step browser approval flow.

## What MEM does

1. Validates the selected profile and server.
2. Performs a write/read/delete probe against the OS secret store.
3. Reuses an already stored valid credential without creating another server authorization.
4. Creates a high-entropy verifier held only in CLI process memory.
5. Asks the server for a browser approval URL and short code.
6. Prints the private `/cli/authorize` URL and code.
7. Polls while the operator reviews and approves the device in the browser.
8. Stores the issued opaque device credential only after the original CLI proves its verifier.

The browser never receives the device credential. The displayed code cannot replace the verifier.

## Approve in the browser

Open the URL printed by the CLI, enter the short code, review the device label and expiry, and approve only the device you started. MEM uses the current named browser operator and requests fresh identity verification when policy requires it.

The current server default gives a pending authorization 10 minutes. The server owns this lifetime and may change it. If the code expires, start login again.

## Verify success

```bash
mem account show --profile home
mem host status --profile home
```

The current server defaults are:

- 8 hours maximum idle lifetime;
- 7 days absolute lifetime.

The CLI prints the actual idle and absolute expiry returned by the server. Server configuration remains authoritative.

## Common failures

| Result | Meaning | Action |
|---|---|---|
| `cli_login_profile_required` | No named profile was selected | Create or select a profile |
| `cli_login_secure_store_unavailable` | Secret Service probe or read failed | Unlock/configure the non-root keyring |
| `cli_login_rate_limited` | Shared authorization-start budget was exceeded | Wait, then retry once |
| `cli_login_authorization_denied` | Browser operator denied the device | Review the device and start again |
| `cli_login_authorization_expired` | Approval window ended | Start a new login |
| `cli_login_unreachable` | Control Plane became unreachable | Restore private connectivity and retry |
| `cli_login_secure_store_write_failed` | Approval succeeded but local secure storage failed | Fix the keyring and start a new login |

## Safety boundary

Do not approve a device you did not start. Do not paste approval codes into public channels. Never extract or copy the stored credential from Secret Service. MEM rejects password, TOTP, recovery-code, bearer-token, and device-credential command options.

## Related documentation

- [Profiles and servers](profiles.md)
- [Account status and logout](account-and-logout.md)
- [CLI security model](security-model.md)
