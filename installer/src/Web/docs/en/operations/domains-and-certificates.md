---
id: "operations/domains-and-certificates"
translationKey: "operations/domains-and-certificates"
locale: "en"
groupId: "operations"
groupKey: "operations"
groupLabel: "Operations"
groupOrder: 30
title: "Operate domains, certificates, and renewal"
description: "Register Domains, issue Domain-owned certificates, manage the active certificate and main Domain, and operate automatic renewal safely."
order: 15
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Domains", "certificates", "deSEC", "ACME", "renewal", "NPM"]
route: "/docs/operations/domains-and-certificates"
aliases: []
outputPath: "docs/operations/domains-and-certificates.md"
preserveLegacyBranding: false
---
# Operate domains, certificates, and renewal

Use **Domains** as the operator workspace for public Domain registration, Domain-owned certificates, active-certificate selection, main-platform Domain selection, and renewal readiness.

The important ownership rule is:

```text
Domain
  → owns certificates
  → has at most one active production certificate
  → has its own renewal policy and renewal credential
```

The main-platform Domain is a separate role. A certificate being active for its Domain does not by itself make that Domain the main platform Domain.

## Register a Domain first

Open **Domains → Add Domain** to create the Domain registry entry.

The MEM 0.2.x guided provider is **deSEC**. Add Domain records the Domain, provider, and DNS zone needed for later work. It is deliberately **registration only**:

- it does not contact deSEC;
- it does not create `_acme-challenge` records;
- it does not request a certificate;
- it does not store a certificate-issuance token;
- it does not enable automatic renewal.

After registration, open the Domain's own workspace to issue or manage certificates.

> [!NOTE]
> First-time Setup has its own reviewed platform-installation certificate plan. The post-install **Add Domain** workflow is intentionally smaller and must not be treated as the old Setup wizard repeated inside Domains.

## Use the global Certificates page as inventory

**Domains → Certificates** is the fleet-wide certificate inventory. It helps you find certificates across all registered Domains and then open the owning Domain or certificate details.

Certificate ownership and normal lifecycle mutations belong to the Domain resource. Do not treat the fleet inventory as a cross-Domain assignment tool.

The collapsed **Advanced ingress diagnostics** area is different: it contains bounded low-level NPM/ingress diagnostic operations for troubleshooting. Those diagnostics do not replace the Domain-owned certificate model.

## Issue a certificate from its owning Domain

Open the Domain, then **Certificates → Issue certificate**.

MEM locks the request to the owning Domain. The guided path derives the wildcard certificate name from the Domain and uses deSEC DNS-01. Supply the one-time credential requested by the issuance form; do not place DNS tokens in support records, screenshots, or ordinary logs.

Certificate issuance is a **server-owned durable operation**. The browser shows a human-readable phase timeline derived from server progress, for example:

```text
Prepare certificate request
Publish DNS-01 challenge
Wait for authoritative DNS readiness
Validate DNS challenge with Let's Encrypt
Finalize and download certificate
Store and validate certificate
Secure production renewal credential   (production when applicable)
```

The exact current phase is authoritative. You can navigate away and return; MEM rediscovers the durable operation rather than making the browser own the work.

Once the server accepts an issuance request, the full request form collapses into a safe request summary plus the durable progress/result view. MEM does **not** redisplay the deSEC token. After a terminal result, **Issue another certificate** or **Try another issuance** returns to a fresh request form with the token blank.

While issuance is running, the global **Certificates** inventory, the owning Domain's **Certificates** page, and Domain detail can show compact active-issuance activity with **View progress**. These surfaces lead back to the same server-owned operation; they do not create another issuance request.

### Technical evidence versus current state

The phase timeline is the normal operator view. Expand **Technical evidence** when troubleshooting.

Evidence rows are historical observations recorded during the operation. A row labelled, for example, **Recorded: Running** means that state was observed when the evidence was emitted. It does **not** mean a terminal operation that now says **Succeeded** is still running.

If issuance fails and MEM creates an incident, use the Diagnostics deep link from the operation result. Preserve the operation/incident identity when asking for support.

A successful historical issuance remains useful evidence if its resulting certificate is later deleted. In that case the issuance workspace records that the certificate no longer exists instead of presenting a dead **Open issued certificate** link.

## Understand staging and production

Use Let's Encrypt **staging** to test DNS-01 behavior without consuming normal production issuance limits.

A staging certificate:

- is not trusted by normal browsers;
- remains owned by its Domain;
- cannot become the Domain's active production certificate;
- cannot make the Domain eligible to become Main;
- does not create or replace the production renewal credential or renewal policy;
- must not be treated as a normal Matrix/Element ingress certificate.

Use **production** for real public service. MEM must complete the production validation and activation contract before the Domain's active-certificate pointer changes. A successful production issuance normally stores the verified deSEC credential in protected Domain storage, records the ACME contact, and enables the default automatic-renewal policy. If that final renewal-credential step returns a warning, the certificate itself can still be valid, but the Renewal workspace must be repaired before unattended renewal is considered ready.

## Active certificate and main Domain are separate

A Domain can own certificate history while one eligible production certificate is active.

When changing the active certificate, MEM uses the Domain-scoped operation and validates that the certificate belongs to that Domain and is eligible for production use.

A non-main Domain can be promoted to **Main** only when the current readiness contract allows it. Staging certificates are never a shortcut around that requirement.

## Use Renewal as a fleet view, then open one Domain

Open **Domains → Renewal** for a fleet-level status view. It summarizes renewal readiness across Domains, including whether automatic renewal is enabled, whether a protected DNS renewal credential is present, certificate expiry, and the next automatic attempt when one is scheduled.

The fleet page is intentionally read-only for sensitive renewal configuration. Select **Open renewal** for a Domain to use its detailed renewal workspace.

The Domain renewal workspace contains the protected operations, including:

- enrol or rotate the deSEC renewal credential for a historical Domain, recovery, or planned credential rotation;
- enable or disable automatic renewal when allowed;
- **Renew now** or retry a failed renewal when allowed;
- current/last operation state;
- durable renewal history and Diagnostics links.

The deSEC renewal token is verified and stored protected server-side. MEM does not return the stored token to the browser. Successful production issuance normally performs this enrolment automatically; manual enrolment is the recovery/rotation path when the workspace reports that a credential is required.

## How automatic renewal behaves

Automatic renewal is server-owned. When an eligible production certificate enters its renewal window, MEM can issue and validate a replacement, activate it in NPM when required, verify ingress, and only then switch the Domain to the renewed certificate.

The previous certificate is retained as history. A failure before safe activation must not silently move the active pointer to an unverified replacement.

If renewal is disabled or the Domain has no renewal credential, the fleet and detail pages should say so explicitly rather than implying that unattended renewal is configured.

## Delete certificates and Domains explicitly

Certificate deletion belongs to the owning Domain. Open the certificate detail and use **Delete certificate** when the certificate is no longer required. MEM deliberately blocks deletion when the certificate is the main-platform certificate, is still required by a live Chat Server, or is still consumed by NPM ingress. Staging certificates and otherwise-unused production certificates can be removed safely.

Deleting the active production certificate from an unused non-main Domain deliberately clears that Domain's active-certificate pointer; it does not delete the Domain. A cleanup failure must leave the registry/storage state intact rather than reporting a false success.

Domain deletion is not a hidden certificate cascade. Delete Domain-owned certificates explicitly first, then use **Delete domain** from Domain detail. The main Domain and Domains still referenced by active Chat Servers remain protected.

Deleting an active production certificate does not automatically erase a verified renewal credential or policy. Until another production certificate exists, Renewal can therefore report that the policy and credential are configured while a production certificate is still required.

## A safe operator sequence

For a new post-install Domain:

1. open **Domains → Add Domain** and register the Domain;
2. open that Domain;
3. issue a staging certificate first when DNS delegation or provider access is uncertain, and confirm it remains non-active;
4. issue the production certificate;
5. confirm the eligible production certificate is active for the Domain and Renewal reports the protected credential/policy state expected from successful production issuance;
6. use manual renewal credential enrolment or policy controls only when the Renewal workspace reports recovery/rotation is required or you intentionally change the policy;
7. set the Domain as **Main** only when the readiness contract permits it;
8. use Diagnostics/evidence if any durable operation fails.

## Do not infer more than the state proves

Keep these distinctions clear:

- **deSEC registered as provider** does not mean a reusable renewal credential is enrolled;
- **certificate exists** does not mean it is the active certificate;
- **active for Domain** does not automatically mean **main platform certificate**;
- **staging succeeded** does not make a staging certificate production-eligible;
- an old evidence row does not override the current terminal operation state.
