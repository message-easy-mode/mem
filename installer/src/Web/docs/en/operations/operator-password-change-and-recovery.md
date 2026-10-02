---
id: "operations/operator-password-change-and-recovery"
translationKey: "operations/operator-password-change-and-recovery"
locale: "en"
groupId: "operations"
groupKey: "operations"
groupLabel: "Operations"
groupOrder: 30
title: "Change or recover an operator password"
description: "Change a known MEM operator password or recover a forgotten Platform Owner password from the host console."
order: 45
status: "supported"
appliesTo: ["0.2.x"]
tags: ["operator password", "password change", "forgot password", "password reset", "account recovery", "MFA"]
route: "/docs/operations/operator-password-change-and-recovery"
aliases: []
outputPath: "docs/operations/operator-password-change-and-recovery.md"
preserveLegacyBranding: false
---
# Change or recover an operator password

MEM separates a normal self-service password change from host-authoritative recovery. Use the browser when you still know your current password. Use the host console only when a Platform Owner has forgotten the password and cannot complete the normal change flow.

If you are locked out of the Control Plane and cannot open its built-in Documentation page, open the public documentation on **messageeasymode.com** and search for **Change or recover an operator password**. The public documentation is synchronized from the same MEM documentation source.

## Change a password while signed in

For your own named operator account:

1. Open the account menu or **Operator access**.
2. Choose **Change password** for the current operator.
3. Verify your identity with your **current password** and a current **authenticator-app code**.
4. Enter the new password twice. Use **Show password** when needed to verify complex characters before submitting.
5. Choose **Change password**.

The new password must satisfy the password policy shown by MEM. In MEM 0.2.x the form requires at least 14 characters including uppercase, lowercase, a number, a symbol, and at least four unique characters.

After a successful change, MEM:

- invalidates existing MEM browser sessions and CLI/device sessions for that operator;
- returns the current browser to sign-in;
- rejects the old password;
- preserves the existing TOTP authenticator configuration;
- preserves unused recovery codes;
- preserves roles and normal account state.

Sign in again with the new password and the existing authenticator.

A recovery code cannot replace the current password for this operation and cannot satisfy high-risk step-up.

## Recover a forgotten Platform Owner password

MEM 0.2.x does not provide an email reset link, security-question flow, or Web administrator button for a forgotten operator password.

For a **Platform Owner** who has forgotten the password, use host-authoritative recovery from the MEM server console.

Run this on the MEM host:

```bash
sudo docker exec -it mem-control-plane \
  dotnet Api.dll operator reset-password PLATFORM_OWNER_USERNAME
```

The command is interactive. It asks you to type the selected Platform Owner username to confirm the target, then prompts for the replacement password twice without echoing it to the terminal.

Do **not** place the new password in the command line, shell history, a script argument, or a support report.

On success, the command reports that the password was reset. Existing TOTP configuration, unused recovery codes, roles, and platform data are preserved, while existing MEM sessions for that account are revoked.

Then:

1. Return to the MEM sign-in page.
2. Sign in with the replacement password.
3. Complete MFA with the existing authenticator code, or use an existing recovery code at the MFA stage when appropriate.

## Current recovery boundary

The host command above is deliberately a **Platform Owner recovery** command in MEM 0.2.x. It is not a general Web or console mechanism for one operator to reset another named operator's password.

Do not attempt to work around that boundary by editing the MEM SQLite database, replacing password hashes manually, or copying Identity fields between accounts.

## If the authenticator is also unavailable

Host password recovery preserves the current MFA configuration. It does not remove or replace TOTP.

If the password is known but the authenticator is unavailable, use an unused MEM recovery code at the MFA stage. If both password authority and all MFA recovery authority are unavailable, stop and use the supported recovery/support procedure rather than modifying authentication data directly.

## Related guidance

See [Security and access settings](security-and-access-settings.md) for the wider authentication, role, session, and high-risk step-up model.
