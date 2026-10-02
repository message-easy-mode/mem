# Contributing to Message Easy Mode

_Last updated: 2026-10-02_

Thank you for your interest in contributing to Message Easy Mode (MEM).

MEM is open source and intentionally **maintainer-led**. Contributions are welcome, but contribution does not imply roadmap influence, merge acceptance, or a guaranteed review timeline.

The goal is to keep the project coherent, secure, operationally predictable, and maintainable over time.

## Development and publication model

MEM deliberately separates active development from public source publication:

```text
maintainer development workflow
        ↓
reviewed source state
        ↓
public release-aligned source
https://github.com/message-easy-mode/mem
```

The public GitHub repository is a release-aligned source surface, not the canonical day-to-day development checkout.

Before investing significant effort in a change, open a focused issue or discussion in the public project surface and describe what you want to change. Maintainers may confirm the current development/review route or ask that the work be coordinated another way.

Do not assume that a direct GitHub pull request is the preferred path for a substantial change.

## Project posture

MEM is not roadmap-by-committee.

Maintainers are responsible for product direction, architecture, release priorities, security posture, and merge decisions. In practice this means:

- major architectural changes require alignment first;
- feature requests may be declined even when technically reasonable;
- broad unsolicited refactors are unlikely to be accepted;
- maintainability and operational clarity take priority over merge volume; and
- opening an issue or contribution does not create an obligation to adopt it.

See `GOVERNANCE.md` for the project decision-making model.

## Contributions that are especially useful

The most useful contributions are usually:

- focused bug fixes;
- regression tests;
- small reliability or usability improvements;
- precise documentation corrections;
- deployment and operational improvements with clear evidence; and
- narrowly scoped changes with an obvious user or maintainer benefit.

Small, reviewable changes are strongly preferred over multi-concern rewrites.

## Before contributing

Before doing substantial work:

1. Read `README.md`, `GOVERNANCE.md`, `SUPPORT.md`, and relevant project documentation.
2. Search existing issues and known limitations.
3. Describe non-trivial work before implementing it.
4. Wait for maintainer alignment on architecture, dependencies, major UI changes, or breaking changes.

Prior alignment is especially important for:

- architecture changes;
- large refactors;
- dependency replacements;
- persistence/schema changes;
- release or supply-chain workflow changes;
- major UI redesigns;
- new top-level features; and
- breaking changes.

## Change quality

A contribution should be:

- focused and understandable;
- consistent with the existing architecture;
- safe for self-hosted operators;
- accompanied by relevant tests where practical;
- documented when behavior or operator expectations change; and
- free of unrelated formatting or dependency churn.

A useful change description explains:

- what changed;
- why it changed;
- important tradeoffs;
- how it was tested; and
- anything reviewers should inspect carefully.

Run the narrowest relevant tests first, then the appropriate broader regression gate before presenting a change as complete.

## Repository hygiene

Never submit credentials, API tokens, private keys, recovery codes, production databases, runtime state, local `.env` files, personal workstation paths, or generated build output.

Keep source changes separate from local evidence and operator state.

The project has dedicated public-source preparation and exposure-scanning tooling under `scripts/public-source/`; do not treat passing ordinary build tests as proof that a tree is safe to publish.

## Documentation changes

Operator documentation is maintained through the Message Easy Mode documentation workflow and synchronized into the Control Plane as a reviewed snapshot.

If you find a documentation problem, report or propose the correction rather than assuming every generated or embedded copy is the canonical editing location.

Public documentation is available at:

- https://messageeasymode.com/docs

## Communication

Keep project communication:

- respectful;
- direct;
- technically grounded; and
- focused on evidence, reproducibility, and project fit.

Disagreement is fine. Personal attacks, harassment, and deliberately disruptive behavior are not.

## Support requests

The public source repository is not a general consulting or emergency-support channel.

Before opening a support-related issue, read `SUPPORT.md` and the operator documentation. Reproducible project defects are useful; open-ended environment debugging may be outside the community support boundary.

## Security issues

Do not disclose suspected vulnerabilities through public issues, discussions, or pull requests.

Follow `SECURITY.md` for private reporting instructions.

## Contribution licensing

This repository does not currently use a separate Contributor License Agreement (CLA).

Unless explicitly agreed otherwise in writing before merge, code, documentation, configuration, tests, assets, and other material submitted for inclusion in MEM are provided under the repository's open-source license terms and this contribution policy.

By submitting a contribution, you grant the Message Easy Mode project maintainers and applicable copyright holders a perpetual, worldwide, non-exclusive, irrevocable, royalty-free license to use, reproduce, modify, adapt, publish, distribute, sublicense, relicense, and otherwise make the contribution available as part of Message Easy Mode or related and successor works.

This permission includes use in open-source releases and in separately offered hosted, managed, enterprise, commercial, or dual-licensed distributions where the maintainers have the legal right to do so.

For clarity:

- you retain ownership of your contribution unless a separate written agreement says otherwise;
- you must have the legal right to submit the contribution;
- submission does not require the maintainers to merge, publish, support, or continue distributing the contribution; and
- if MEM later adopts a CLA or other contributor agreement, that requirement will be stated before affected contributions are merged.

If you cannot grant these permissions, do not submit the contribution.

## Contributor representations

By submitting a contribution, you represent, to the best of your knowledge, that:

- you have the legal right and authority to submit it;
- it is your original work or you have the necessary rights and attribution;
- it does not knowingly include confidential, proprietary, or improperly licensed material; and
- if an employer or another party has rights in the work, you have obtained any required permission.

## Maintainer discretion

Maintainers may decline or close contributions that are out of scope, insufficiently specified, too broad to review efficiently, duplicative, inconsistent with project direction, or disproportionate to the benefit they provide.

This is part of keeping MEM sustainable and technically coherent.
