# Message Easy Mode Security Policy

_Last updated: 2026-10-02_

## Reporting a vulnerability

Do **not** report suspected security vulnerabilities through public GitHub issues, discussions, pull requests, screenshots, or logs.

Report them privately to:

**security@thedeltacore.com**

Include, where practical:

- a concise description of the issue;
- the affected MEM version, source tag, image reference, or component;
- reproduction steps or a proof of concept;
- the security impact you believe is possible;
- relevant logs with credentials and private data removed; and
- any mitigation or fix you have already tested.

Do not send live production credentials, private keys, recovery codes, signing keys, or complete sensitive configuration unless they are genuinely required to understand the issue and a secure transfer method has been agreed.

## Scope

This policy covers security issues in the maintained Message Easy Mode project surfaces, including where applicable:

- public source in `message-easy-mode/mem`;
- the MEM Control Plane and Web application;
- HostAgent and runtime-management behavior;
- MEM CLI;
- MEM Migrate and the Source Assistant;
- bootstrap/install tooling;
- published Control Plane images;
- release artifacts produced for MEM; and
- security-sensitive operator guidance maintained by the project.

Third-party products that MEM integrates with have their own security policies. A vulnerability in an upstream project should normally be reported to that upstream project as well.

## Coordinated disclosure

Please allow reasonable time for investigation, remediation, release preparation, and operator notification before public disclosure.

The maintainers may ask for additional evidence, a reduced reproduction, or confirmation against a corrected build.

## High-value security surfaces

MEM manages infrastructure and secrets. Security-sensitive areas include:

- Docker socket and host-level runtime access;
- authentication and step-up authorization;
- Matrix signing identity and recovery material;
- DNS-provider credentials and certificate private keys;
- reverse-proxy and routing configuration;
- database credentials and persistent state;
- backup and restore artifacts;
- migration intake and cutover workflows;
- CLI credential storage; and
- release/publication provenance.

Treat access to these surfaces as privileged.

## Operator responsibilities

MEM reduces operational complexity but does not remove the need for normal host and network security.

Operators remain responsible for, among other things:

- restricting administrative access;
- protecting host and Docker privileges;
- keeping credentials and recovery material private;
- applying supported updates;
- maintaining appropriate firewall and routing rules;
- protecting backups; and
- reviewing deployment-specific risks.

Use the current operator documentation at https://messageeasymode.com/docs for supported installation and security guidance.

## Public issue hygiene

If you are unsure whether a report is security-sensitive, use the private security contact first.

Do not post exploit details publicly merely to ask whether an issue is valid.
