---
title: Deploy MEM on a DigitalOcean Ubuntu droplet
description: Deploy MEM 0.2.x on a DigitalOcean Ubuntu 24.04 Droplet with private SSH administration, public Matrix/Element HTTPS, and proven TURN relay networking.
section: Guides
order: 10
---

# Deploy MEM on a DigitalOcean Ubuntu droplet

This is the current cloud deployment guide for MEM 0.2.x on DigitalOcean. DigitalOcean is not a special MEM runtime mode: use the same official Ubuntu bootstrap and first-time Setup as other supported servers, but keep the Control Plane private and configure the cloud firewall deliberately.

## 1. Create an Ubuntu 24.04 amd64 Droplet

Use **Ubuntu 24.04 LTS on amd64/x86_64**.

For the current MEM bootstrap:

- fewer than **2 CPU cores** produces a warning;
- less than **3,500 MB RAM** is rejected;
- about **7,800 MB RAM** is the recommended level;
- less than **20 GB free on `/`** is rejected;
- about **50 GB free on `/`** is recommended.

A DigitalOcean 2 GB Droplet does not satisfy the current 3,500 MB RAM floor. A 4 GB Droplet can satisfy the hard admission check, but it remains below MEM's roughly 7.8 GB recommendation. For a less constrained production starting point, choose approximately 8 GB RAM or more and size storage for Matrix history, media, backups, restores, and migrations.

Review [Requirements and supported environment](../start/requirements.md) before creating the server.

## 2. Understand the DigitalOcean network boundary

A normal DigitalOcean Droplet is different from an on-premises VM behind a home or office router. You normally configure a **Cloud Firewall**, not a separate WAN-to-LAN NAT port-forward rule.

```text
Internet
    |
DigitalOcean Cloud Firewall
    |
Droplet public interface
    |
MEM public services
```

The Droplet's public address is **not** a Trusted LAN address. Use **SSH / local-only** administration for the Control Plane:

```text
mem-control-plane -> 127.0.0.1:8443
operator -> SSH local port forward -> droplet
```

Do not create a public NPM route or public DNS name for the Control Plane.

## 3. Configure the Cloud Firewall

A proven minimal inbound shape for the current deSEC DNS-01 path is:

```text
22/tcp                  SSH — restrict to the operator network/IP where practical
443/tcp                 Matrix and Element HTTPS
3478/tcp                TURN
3478/udp                TURN
49160-49200/udp          TURN relay media
```

The following administration/data ports should remain non-public:

```text
81/tcp                  Nginx Proxy Manager administration
8443/tcp                MEM Control Plane
5432/tcp                PostgreSQL
9443/tcp                Portainer, when installed
```

TCP **80 is not required for MEM's current deSEC DNS-01 certificate issuance path**. Open it only when you deliberately want a public HTTP/redirect topology that uses it. HTTPS service traffic still requires TCP 443.

Keep outbound access sufficient for DNS, ACME, Ubuntu/Docker package sources, container registries, and the external services you intentionally use.

## 4. Prepare deSEC, registrar delegation, and DNSSEC

Before asking MEM to issue a production certificate, make sure the domain's public DNS authority is correct.

For the current guided deSEC path:

1. add or prepare the zone in deSEC;
2. delegate the domain at the registrar to the authoritative nameservers shown by deSEC;
3. when DNSSEC is enabled, publish the **DS values supplied by deSEC at the registrar/parent zone**;
4. do not create an apex DS record inside the deSEC child zone as a substitute for registrar delegation;
5. allow delegation and DNSSEC changes to propagate before treating certificate failures as a MEM defect.

A deSEC TXT challenge can be present while ACME validation still fails if the registrar/parent DNSSEC chain is wrong.

Point the public Matrix, Element, and TURN service names at the Droplet's public address as your deployment plan requires. The current guided certificate flow uses deSEC DNS-01 and Nginx Proxy Manager.

See [Choose the public domain and certificate plan](../installation/domain-and-certificate.md).

## 5. Run the official bootstrap

Acquire the approved release artifacts and run the normal MEM bootstrap on the Droplet. During bootstrap choose **SSH / local-only** administration.

After bootstrap, use the installer-provided SSH tunnel command from your workstation and open the private Control Plane through the local forwarded address. Complete:

1. first Platform Owner setup;
2. server checks;
3. public-domain and certificate review;
4. platform installation;
5. final platform verification and handoff.

Do not publish `8443` to the Internet to avoid using the SSH tunnel.

## 6. Allow certificate issuance to settle

DNS-01 issuance is not necessarily instant. MEM waits for the authoritative DNS servers to agree on the challenge before continuing to ACME validation.

It is possible for one authoritative nameserver to return the new `_acme-challenge` TXT record before another. If MEM reports an authoritative DNS visibility timeout:

- keep the existing Domain/installation state;
- check the authoritative nameservers rather than deleting and recreating the Domain;
- allow DNS to converge;
- use the supported Retry path on the same durable operation.

A successful production issuance can take several minutes while DNS readiness, ACME validation, finalization, protected storage, and NPM import complete.

## 7. Create and verify the first chat server

Do not stop at **Platform setup complete**. Create the first Matrix + Element stack and require:

- the stack to reach **Healthy**;
- public Matrix and Element routes to work over HTTPS;
- users to be able to sign in and exchange a real message;
- Diagnostics to contain no unexplained urgent incident;
- PostgreSQL, NPM, and Coturn to remain healthy.

Run **Doctor** from the stack workspace when available and retain the result if this is an acceptance test.

## 8. Prove TURN relay, not only call success

A successful Element call does **not** by itself prove that TURN was used. WebRTC may establish a direct peer-to-peer path.

Use two clients on genuinely different networks where practical. During a fresh call, an operator can observe the server with `tcpdump` if it is installed and packet capture is acceptable in the environment:

```bash
sudo tcpdump -ni any \
  '(udp port 3478 or tcp port 3478 or udp portrange 49160-49200)'
```

Interpret the result carefully:

- traffic on `3478/tcp` or `3478/udp` shows TURN/STUN negotiation activity;
- **sustained UDP traffic in `49160-49200` during the call is strong evidence that Coturn is relaying media**;
- browser WebRTC diagnostics showing a selected ICE candidate of type `relay` are useful additional evidence.

Packet captures can expose client IP addresses and timing information. Stop the capture after the bounded test and do not publish it as an unrestricted support artifact.

See [Configure voice and video TURN](../chat-servers/voice-and-video.md).

## 9. Reboot and recovery acceptance

Before treating a new production server as fully accepted, schedule one controlled Droplet reboot after the platform and first stack are healthy.

After reboot confirm:

- the Control Plane is again available only through the private SSH/local path;
- PostgreSQL, NPM, Coturn, and the managed stack recover without manual reconstruction;
- Matrix and Element return to Healthy;
- an existing user can send a new message;
- Diagnostics does not show an unexplained recovery failure.

This verifies durable host/runtime recovery rather than only first-boot success.

## 10. Create the first off-host backup

Create a native MEM backup after the first stack is accepted. Export a portable ZIP and store at least one copy away from the Droplet.

For important deployments, a **Private Restore Test** provides stronger evidence that the backup is usable without replacing production.

See [Back up and restore](../backups-and-restores/index.md).

## Acceptance checklist

A DigitalOcean deployment is ready for normal operation when you have confirmed:

- Ubuntu 24.04 amd64 and the MEM bootstrap resource floor;
- SSH/local-only Control Plane access on `127.0.0.1:8443`;
- only the intended public Cloud Firewall ports are open;
- deSEC delegation and DNSSEC/DS state are correct;
- a production wildcard certificate is active;
- PostgreSQL, NPM, and Coturn are healthy;
- the first Matrix + Element stack is Healthy and publicly reachable;
- a real off-network call has evidence of TURN relay use;
- a controlled reboot recovers the platform and stack;
- the first native backup has been exported off-host;
- Diagnostics contains no unexplained urgent incident.
