---
title: Reset passwords and manage account lifecycle
description: Establish Matrix admin authority, reset passwords, deactivate accounts, and reactivate them safely.
section: Operate chat servers
order: 40
---

# Reset passwords and manage account lifecycle

## Outcome

Use guarded server-admin workflows to reset a Matrix password, deactivate an account, or reactivate it with a new password.

## Establish password-reset authority

MEM requires validated authority from an existing active Matrix server administrator before it can reset passwords.

Select **Set reset authority** or **Replace authority**. The normal mode uses a Matrix administrator ID and password. MEM uses the password to obtain and validate authority and does not store that password. An access token is available as an advanced recovery option and is encrypted with the MEM Data Protection key ring.

Setting or replacing authority requires a recent Control Plane step-up verification.

## Reset a password

Select **Reset password** beside an active Matrix user, enter and confirm the new password, and complete step-up when requested.

Synapse signs the user out of existing Matrix devices when the password changes. MEM cannot display or recover the previous password.

> [!WARNING]
> A password reset does not recreate end-to-end encryption keys. A user who has no verified device and cannot unlock server-side key backup may lose access to old encrypted history even though the account itself is usable again.

When the password being changed belongs to the administrator used as reset authority, MEM invalidates that authority. Establish it again before the next reset.

## Deactivate an account

Select **Deactivate account** and review the consequences. The current MEM workflow deactivates without requesting data erasure.

Synapse removes devices, encryption keys, access tokens, room memberships, third-party IDs, and the password. Existing room messages remain in room history. The last active Matrix administrator cannot be deactivated.

Deactivation is a high-risk action and requires recent step-up verification.

## Reactivate an account

A deactivated account can be reactivated with a new password. Select **Reactivate account**, enter the password twice, and complete step-up when requested.

Reactivation restores account access. It does not reconstruct removed devices, encryption keys, room memberships, or missing recovery material.

## Evidence to retain

Record the affected Matrix user ID, action time, operator, and any operation or error reference. Do not copy passwords, access tokens, recovery keys, or TOTP secrets into support reports.
