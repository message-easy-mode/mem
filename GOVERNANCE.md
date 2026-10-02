# Message Easy Mode Governance

_Last updated: 2026-10-02_

## Governance model

Message Easy Mode (MEM) is a **maintainer-led open-source project**.

The source is open for inspection, use, modification, and contribution under the project license, while product direction, architecture, roadmap, release priorities, security posture, and merge decisions remain the responsibility of the maintainers.

This model is intentional.

## Why MEM uses this model

MEM is an operational product for self-hosted communication infrastructure. Keeping it coherent requires:

- clear technical direction;
- consistent architecture;
- conservative handling of security and destructive operations;
- release discipline;
- reliable operator documentation; and
- sustainable maintainer boundaries.

Open source does not require consensus governance or roadmap voting.

## Maintainer responsibilities

Maintainers are responsible for:

- setting product and roadmap direction;
- preserving architectural coherence;
- reviewing and accepting or declining changes;
- defining release and support boundaries;
- maintaining security and publication practices;
- keeping operator documentation aligned with supported behavior; and
- deciding when source and release states are ready for public publication.

Maintainers may delegate implementation, review, triage, documentation, testing, or release tasks without changing the overall governance model.

## Development and public-source publication

MEM separates active development from public source publication.

The public repository at:

- https://github.com/message-easy-mode/mem

is a release-aligned source surface. Day-to-day development may occur on a separate maintainer-selected development forge. Reviewed source states are prepared, scanned, built, tested, and then published deliberately.

Release artifacts are distributed separately through:

- https://github.com/message-easy-mode/mem-releases

This separation is part of the project's release and provenance model, not a separate governance tier.

## Contributor expectations

Contributors can expect:

- technically serious review where maintainer capacity permits;
- decisions based on project fit as well as code quality;
- clear boundaries around security, support, and release scope; and
- preference for small, reviewable changes.

Contributors should not expect:

- automatic merge of technically valid work;
- roadmap voting rights;
- architectural control through persistence or contribution volume;
- consensus decision-making; or
- guaranteed response or review times.

See `CONTRIBUTING.md` for the contribution workflow and expectations.

## Decision-making

Major project decisions are made by the maintainers.

Feedback and evidence are welcome, but issue counts, reactions, comment volume, or contributor seniority do not determine the roadmap by themselves.

When tradeoffs exist, maintainers may prioritize:

- operator safety;
- product coherence;
- maintainability;
- security;
- release quality;
- operational simplicity; and
- realistic execution capacity.

Maintainer decisions on project scope, release readiness, architecture, and merge acceptance are final for the purposes of project direction.

## Support and governance are separate

Using MEM, reporting bugs, contributing code, or helping other users does not automatically confer governance authority.

Likewise, maintainer control of project direction does not create an obligation to provide individualized support. Community support boundaries are defined separately in `SUPPORT.md`.

## Commercial relationship

MEM may coexist with paid support, hosted services, enterprise packaging, commercial licensing, or other related offerings.

Any such offerings should be described separately and should not obscure the identity or licensing of the open-source project.

The existence of commercial offerings does not by itself change this governance model.

## Project continuity

The Message Easy Mode 0.2.x line succeeds the Matrix Easy Mode 0.1.x line. The product rename does not reset the project's governance, licensing, or release lineage.
