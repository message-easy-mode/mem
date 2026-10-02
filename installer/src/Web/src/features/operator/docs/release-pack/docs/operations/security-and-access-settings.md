---
title: Security and access settings
description: Operate the MEM 0.2.x private Control Plane, named accounts, passwords, MFA, recovery, roles, and high-risk step-up policy.
section: Operations
order: 40
---

# Security and access settings

The MEM Control Plane is a privileged administration surface with Docker authority. Network privacy, named authentication, MFA, roles, and server-side authorization work together; none replaces the others.

## Keep administration private

Use an explicitly selected Trusted LAN address, VPN, or SSH/local-only forwarding. Do not publish the Control Plane through NPM, public DNS, or Internet-facing NAT.

## Named operator identity

Platform Owners and other MEM operators use Control Plane accounts. These accounts are separate from Matrix users who sign in to Element.

## MFA and recovery

The first Platform Owner establishes TOTP MFA and receives recovery codes. Store recovery codes offline and treat them as credentials.

## Operator passwords and forgotten-password recovery

Use **Change password** from the account menu or the current row in **Operator access** when you still know your password. MEM requires fresh current-password and authenticator verification, then revokes existing browser and CLI/device sessions after the password changes.

If a Platform Owner has forgotten the password, use the host-authoritative console recovery command rather than a browser reset flow. See [Change or recover an operator password](operator-password-change-and-recovery.md) for the exact command, preservation rules, and recovery boundary.

## High-risk step-up

MEM can require fresh identity verification for destructive/high-risk browser actions. The configured reuse window controls how long a successful verification remains valid for the same server-managed session.

Disabling high-risk step-up does not disable login, TOTP MFA, roles, server-side authorization, or audit evidence. Changing the step-up policy itself remains a sensitive operation.

## Session and role changes

Password, MFA, role, account-status, session, and security-policy changes should invalidate authority where the server contract requires it. Do not rely on browser-local flags to extend privileged authority.

## CLI

The CLI uses named-device authority and OS Secret Service where supported. It must not become a command-line bypass for browser step-up or accept reusable secrets merely to make a high-risk operation easier.
