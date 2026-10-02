---
id: "chat-servers/voice-and-video"
translationKey: "chat-servers/voice-and-video"
locale: "en"
groupId: "chat-servers"
groupKey: "chat-servers"
groupLabel: "Operate chat servers"
groupOrder: 15
title: "Configure voice and video TURN"
description: "Inspect the authoritative Synapse TURN state and safely connect or disconnect the shared platform coturn service."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["TURN", "coturn", "voice", "video", "Synapse"]
route: "/docs/chat-servers/voice-and-video"
aliases: []
outputPath: "docs/chat-servers/voice-and-video.md"
preserveLegacyBranding: false
---
# Configure voice and video TURN

## Outcome

Inspect whether the stack uses the MEM platform TURN service, then apply a reviewed connect or disconnect operation when required.

Open the stack workspace and select **Voice & video**.

## Why TURN matters

Matrix clients can often connect directly for calls, but users behind restrictive NAT or firewalls may need a relay. MEM exposes the shared platform coturn service to Synapse without displaying its shared secret.

When coturn is ready during stack creation, MEM writes the platform TURN configuration into Synapse. When it is unavailable, the stack can be created without TURN and will show a warning.

## Understand the state

**Connected** means the live Synapse settings match the recorded MEM-managed association.

**Not connected** means no TURN service is configured for the stack.

**External TURN configuration** means Synapse has TURN settings not owned by this MEM platform. MEM will not overwrite or remove them automatically.

**Configuration needs attention** indicates drift between live Synapse configuration, recorded metadata, or the current platform service.

**State unavailable** means MEM could not establish an authoritative state from the active runtime and configuration.

The page also reports platform readiness, Matrix runtime identity, TURN URIs, configuration hash, and recorded metadata. Inspection is read-only and does not place a test call.

## Platform TURN startup supervision

In containerized MEM runtimes, the Control Plane supervises the shared coturn service when the API starts. MEM waits briefly for Docker and the shared gateway network to settle, then inspects the exact owned runtime.

Safe automatic recovery is deliberately narrow. MEM may start the same stopped MEM-owned coturn container and may correct restart-policy-only drift. It then requires exact runtime readiness and a current functional TURN check. If that functional check fails, MEM may perform **one** bounded automatic Restart & Verify attempt. A repeated failure enters cooldown and requires operator review instead of creating an automatic restart loop.

If an operator-requested Coturn install or maintenance operation is already active in the current API process, startup supervision defers to that explicit operation rather than competing with it.

MEM does not silently recreate coturn, pull a new image, rotate the shared secret, replace protected configuration, take over a foreign container, or repair wrong network/mount/command drift. Those states become **Repair required** or **Conflict** with Diagnostics evidence.

Direct Source and Managed Local development intentionally do not receive this protected automatic mutation authority. They can inspect safe runtime/evidence and use the documented Portainer or Docker development recovery path instead.

## Production firewall and NAT

The current production TURN service publishes:

```text
3478/tcp
3478/udp
49160-49200/udp
```

If the MEM server sits behind a firewall/router, public clients need those paths forwarded/allowed to the server. DNS alone does not open ports or create NAT rules.

## Split DNS and local functional checks

On an on-premises LAN, it is often useful for the internal resolver to answer the TURN hostname with the server's private address while public DNS answers with the WAN address. This keeps local platform checks on a direct LAN path rather than making them depend on NAT reflection.

When Coturn is Running and Ready but the allocation check fails, inspect the **Probe-runtime DNS resolution** result on the Coturn service page. If an internal probe unexpectedly resolves `turn.<domain>` to the public WAN address, correct the resolver path before treating the result as a Coturn defect.

A successful local allocation check proves the configured MEM/server path. It does not replace a later real client call test from the network paths you intend users to use.

## Prove that a real call is using TURN relay

A successful Element call does not by itself prove that TURN was used. WebRTC may connect the clients directly and never carry media through Coturn.

For acceptance testing, place a fresh call between clients on genuinely different networks where practical. If `tcpdump` is available on the MEM host and packet capture is acceptable in your environment, observe only the bounded TURN ports during the call:

```bash
sudo tcpdump -ni any \
  '(udp port 3478 or tcp port 3478 or udp portrange 49160-49200)'
```

Interpret the evidence carefully:

- traffic on `3478/tcp` or `3478/udp` shows TURN/STUN negotiation activity;
- sustained UDP traffic in the configured `49160-49200` relay range during the call is strong server-side evidence that Coturn is relaying media;
- browser WebRTC diagnostics showing a selected ICE candidate of type `relay` are useful additional evidence.

Stop the capture after the bounded test. Packet captures can expose client IP addresses and timing information, so review them before sharing. Do not include TURN shared secrets or unrestricted packet captures in ordinary support reports.

## Connect to platform TURN

Select **Connect to platform TURN**. MEM first creates a server-authored review.

The review may:

- configure the owned Synapse TURN block and restart Matrix; or
- adopt an already matching configuration without rewriting Synapse or restarting Matrix.

A configured connection validates the candidate, changes the owned settings atomically, restarts Matrix, and verifies the resulting state. Active calls may be interrupted during restart. Matrix identity, users, rooms, messages, and media remain unchanged.

## Disconnect from platform TURN

Select **Disconnect from platform TURN** only for a verified MEM-managed configuration. MEM reviews the candidate removal, removes only its owned TURN settings, restarts Matrix, and verifies the disconnected state.

Calls may become less reliable for users on restrictive networks.

## Failure and rollback

Candidate rejection occurs before mutation. When a post-mutation step fails, MEM attempts to restore and verify the previous state. A result marked unresolved requires technical recovery before another change.

Do not paste the shared secret into troubleshooting notes. Retain the operation ID, state, configuration hash, and redacted diagnostics instead.
