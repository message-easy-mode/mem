---
title: Choose the public domain and certificate plan
description: Validate the public domain and DNS access, protect the DNS credential, and prepare the wildcard certificate plan without changing public DNS.
section: Installation
order: 50
---

# Choose the public domain and certificate plan

The **Public domain** stage determines the domain and wildcard-certificate intent that will appear in Review.

This stage is **validation and planning only**. It does not publish the private MEM Control Plane and it does not yet create DNS challenges or request a certificate.

## Information required

Provide:

- the base domain, such as `example.org`;
- an ACME contact email;
- a deSEC API token with permission for the DNS zone;
- whether to use Let's Encrypt staging during testing.

MEM derives the wildcard name `*.example.org`. Future stack hostnames are created beneath the chosen zone.

## What validation does now

When you validate the public-domain plan, MEM:

1. validates the base domain and ACME email;
2. performs a **read-only** deSEC zone-access probe;
3. stores the deSEC credential protected server-side;
4. prepares the wildcard-certificate and ACME-environment plan;
5. records safe planning evidence for Review.

Before Review and `Install platform`, MEM does **not**:

- create `_acme-challenge` TXT records;
- place an ACME order;
- issue or store a certificate/private key;
- create or bootstrap Nginx Proxy Manager;
- import a certificate into NPM;
- configure public ingress.

The page should report that external mutation is `none` after successful validation.

## deSEC is the current guided provider

MEM 0.2.0's guided DNS-01 workflow supports deSEC. Treat its API token as a secret and use the minimum permissions practical for the zone.

Other DNS providers and arbitrary existing proxy systems are not yet guided choices.

## Prepare deSEC delegation and DNSSEC

Before relying on MEM certificate issuance, make sure the public DNS authority for the zone is already correct:

1. add or prepare the DNS zone in deSEC;
2. delegate the domain at the registrar to the authoritative nameservers shown by deSEC;
3. when DNSSEC is enabled, publish the **DS values supplied by deSEC at the registrar/parent zone**;
4. do not create an apex DS record inside the deSEC child zone as a substitute for the registrar DS delegation;
5. allow nameserver and DNSSEC changes to propagate before starting production issuance.

A DNS-01 TXT challenge can be correct on the deSEC nameservers while ACME still rejects the order if the parent DNSSEC DS is wrong. Repair the registrar/delegation state rather than weakening MEM's DNS or ACME safety checks.

## After first-time Setup

The normal post-install **Domains → Add Domain** workflow is intentionally different from this first-time Setup stage. It creates a Domain registry entry only; it does not contact deSEC, issue a certificate, or store an issuance token. Certificate issuance, active-certificate selection, main-Domain readiness, and automatic renewal are managed from the Domain-owned operator workspace.

See [Operate domains, certificates, and renewal](../operations/domains-and-certificates.md).

## Staging versus production

Use Let's Encrypt staging while proving a new DNS setup if you are uncertain about provider access. A staging certificate is not trusted by normal browsers.

For real public Matrix and Element services, Review should show a Let's Encrypt production wildcard certificate.

## DNS propagation and Retry

Authoritative DNS servers do not always converge at exactly the same moment. During DNS-01 issuance, MEM waits for the authoritative servers to show the expected `_acme-challenge` value before it continues to ACME validation.

If MEM reports an authoritative DNS visibility timeout:

- keep the existing Domain and durable operation;
- check the authoritative DNS servers rather than immediately deleting and recreating the Domain;
- allow propagation to settle or correct delegation/DNSSEC if it is wrong;
- use the supported **Retry** path after the cause is resolved.

Several minutes can be normal for DNS readiness, ACME validation, finalization, protected storage, and NPM import. A timeout is a fail-closed result, not evidence that MEM should skip the authoritative-DNS check.

## What happens after Review

Only after you accept the exact reviewed plan and explicitly choose **Install platform** does MEM begin external mutation. The installation worker then:

1. prepares the managed Docker services;
2. creates the DNS-01 challenge through deSEC;
3. waits for DNS visibility;
4. requests and validates the wildcard certificate through ACME;
5. stores the certificate and private key in protected MEM-managed storage;
6. validates the stored certificate;
7. imports or resolves the certificate in Nginx Proxy Manager;
8. preserves durable evidence for Retry, verification, and support reports.

If certificate issuance fails, use the same durable installation and Retry when the reported cause is corrected. Do not delete successful earlier platform steps merely to restart certificate work.
