# CLI-AUDIT-02 — Safe Error and Auth-State Audit

**Status:** release-audit slice

**Purpose:** confirm that MEM CLI authentication and local-session failures are safe, actionable, and stable for automation without reviving retired installer-token or Host Agent secret authority.

## Release contract

Normal operational commands use this chain:

```text
selected local profile
  -> OS Secret Service credential lookup
  -> Authorization: Bearer <stored device credential>
  -> control-plane role/policy/step-up enforcement
```

The CLI must fail before constructing the operational control-plane client when any local prerequisite is missing or unsafe.

## Audited states

| State | Human behaviour | JSON code / status | Safety requirement |
|---|---|---|---|
| No selected profile | Explain that a named profile is required and suggest creating one | `cli_profile_required` | Do not construct the secret store or operational client |
| Missing device credential | Explain that device login is required | `cli_device_login_required` | Do not construct the operational client |
| Secret store unavailable | Explain Secret Service/keyring action | `cli_secret_store_unavailable` | Do not construct the operational client |
| Invalid local credential | Ask operator to logout and login again | `cli_device_credential_invalid` | Do not send invalid credential to the control plane |
| Explicit `--installer-token` | Refuse as retired | `installer_token_retired` | Do not construct auth/session/operational clients |
| Explicit `--agent-secret` | Refuse as retired | `agent_secret_retired` | Do not construct operational clients |
| `mem account show` signed out | Show signed-out profile/server status | JSON `status: signed_out` | Do not contact the control plane |
| `mem account show` rejected by server | Explain stored session was rejected | JSON `status: unauthenticated` | Do not print bearer credential |
| `mem logout` server unavailable | Remove local credential and report unconfirmed server revocation | `serverRevocationStatus: unavailable` | Local logout remains safe and useful |

## Machine-readable contract

JSON output keeps stable English fields:

```json
{
  "source": "mem-cli",
  "status": "error",
  "error": "cli_device_login_required",
  "profile": "home",
  "server": "https://mem.example.internal"
}
```

No JSON response may contain a bearer credential, installer token, password, TOTP value, recovery code, cookie, raw exception body, or raw control-plane response detail.

## Human-output contract

Human output may be English or German according to the existing language precedence, but safe error and auth-state messaging must remain actionable:

- create/select a profile;
- run `mem login --device --profile <name>`;
- unlock/install a Secret Service keyring;
- run `mem logout --profile <name>` before logging in again;
- check private management connectivity when the control plane is unavailable.

## Regression coverage added in this slice

- Operational commands require a selected profile before constructing the secret store or Host Agent client.
- Secret-store unavailable state returns safe stable JSON and does not construct the Host Agent client.
- Invalid local device-session state returns safe stable JSON and does not construct the Host Agent client.
- Profile-selection validation now reuses the same length-aware profile-name rules as profile creation.

## Deferred follow-up

CLI step-up UX remains a separate implementation slice. This audit only ensures existing auth-state failures are safe and stable.


## CLI-RELEASE-01 follow-up

Release help now advertises only the current operator-facing server naming:
`--server` and `MEM_SERVER_URL`. The legacy `--host-agent-url` and
`MEM_HOST_AGENT_URL` compatibility inputs remain parsed for older local scripts,
but they are hidden from normal help and should not appear in new documentation
or examples.

Additional release-audit regression coverage confirms that JSON error output
keeps stable English machine fields even when the selected profile language is
German, and that stored device credentials are not reflected in command output.
