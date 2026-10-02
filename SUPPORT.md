# Message Easy Mode Support

_Last updated: 2026-10-02_

## Support model

Message Easy Mode (MEM) is open-source software. Community support is **best effort**.

There is no guarantee of:

- response time;
- issue turnaround;
- feature delivery;
- environment-specific troubleshooting;
- architecture review on demand; or
- individualized deployment consulting.

The official operator documentation is the first support resource:

- https://messageeasymode.com/docs

## Public source repository boundary

The public GitHub repository is a release-aligned source surface. It exists so released MEM source can be inspected, cloned, built, and reviewed independently.

It is not an emergency-support channel and should not be treated as a substitute for operating-system, network, DNS, firewall, Docker, or Matrix administration skills.

## Good issue reports

Public issues are useful for:

- reproducible project defects;
- focused documentation corrections;
- narrowly scoped feature requests;
- regressions tied to a specific MEM version; and
- problems where enough evidence is available for maintainers to reason about the project behavior.

A useful report normally includes:

- the exact MEM version or source tag;
- the supported host/runtime context;
- what you expected;
- what happened instead;
- steps to reproduce;
- relevant logs or diagnostics with secrets removed; and
- whether the behavior reproduces after following the documented workflow.

## Before opening an issue

Please first:

1. Read the relevant operator documentation.
2. Confirm the MEM version you are running.
3. Check whether the environment is within the supported release contract.
4. Search existing issues and known limitations.
5. Collect reproducible evidence while removing secrets and private data.
6. Reduce the problem to the smallest project-level failure you can demonstrate.

## What public issues are not for

Public issues are not the right place for:

- urgent production escalation;
- general self-hosting consulting;
- open-ended custom-environment debugging;
- requests to design an organization's network or security architecture;
- private account or credential recovery;
- public vulnerability disclosure; or
- logs containing credentials, tokens, private keys, personal data, or sensitive infrastructure details.

For security vulnerabilities, follow `SECURITY.md` instead.

## Environment ownership

Self-hosting means the operator owns the surrounding environment.

Operators are responsible for areas such as:

- host administration;
- backups outside MEM where required;
- DNS delegation and registrar configuration;
- firewall/NAT rules;
- reverse-proxy reachability;
- public IP and routing behavior;
- upstream cloud/provider limitations; and
- organization-specific security policy.

MEM documentation aims to make supported workflows clear, but the project cannot absorb every environment-specific responsibility.

## Issue lifecycle

Maintainers may close issues that are:

- not reproducible;
- missing essential information;
- outside MEM's supported scope;
- duplicates;
- requests for individualized consulting;
- better handled by an upstream project; or
- inconsistent with project direction.

A closed issue is not necessarily a statement that the user's underlying operational concern is unimportant; it may simply be outside the project's actionable support boundary.

## Future support offerings

Paid support, managed assistance, consulting, or enterprise services may be offered separately in the future.

If they are, they should be described separately from community support so the boundary remains clear.
