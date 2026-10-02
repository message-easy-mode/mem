---
title: Use Seq with MEM
description: Set up and control optional Seq delivery and runtime state without replacing MEM-native Diagnostics or exposing secrets.
section: Operations
order: 35
---

# Use Seq with MEM

Seq is an optional advanced search layer for structured MEM events. MEM-native incidents, the safe event store, the persistent CLEF recorder, and support reports remain the primary product path when Seq is absent, stopped, or disabled.

## Keep delivery and runtime separate

MEM displays two distinct controls:

```text
MEM event delivery to Seq
Seq container runtime
```

Stopping Seq does not disable MEM-native Diagnostics. Disabling delivery stops new MEM events from being sent after the required API restart; it does not remove the Seq container or existing Seq data.

## Set up Seq from MEM

When Seq is absent, a Platform Owner can select **Set up Seq**. The guided workflow:

1. explains the approved image, private runtime, persistent data and no-public-ingress boundary;
2. records explicit Seq EULA acceptance;
3. accepts and confirms a one-time initial administrator password, or asks once for the current administrator password when existing authority must be preserved;
4. creates a frozen review containing no password or secret and includes the operator's event-delivery choice, which defaults to enabled after the next API restart;
5. requires recent identity verification before any one-time administrator password is submitted to the execution endpoint;
6. prepares only the approved exact image when it is not already local;
7. inspects and prepares the server-owned data directory before changing first-run authority;
8. hashes a new administrator password through isolated standard input only when first-run authority is required, while preserving an existing configured administrator hash on retry;
9. creates or starts the MEM-managed container and reports runtime readiness only after Docker and Seq `/health` verification pass;
10. provisions or reuses a dedicated ingest-only MEM credential and sends a harmless verification event;
11. queries Seq for that exact verification event before treating the MEM connection as verified;
12. when event delivery was selected, stages the desired delivery state for the next MEM API start; otherwise it leaves ongoing delivery disabled.

The browser does not submit image references, host paths, container IDs, Docker networks or ports. These deployment targets remain server-owned. Plaintext administrator passwords are used only for the protected execution step and are not stored in browser storage, operation evidence or server files. The dedicated ingestion credential remains server-side and is not returned to the browser.

When MEM finds an existing configured administrator password hash, the wizard preserves that authority rather than replacing it. The current Seq administrator password is used once to authenticate the protected connection/provisioning step and is then discarded. Initialized Seq data without its matching administrator secret blocks guided setup and requires explicit recovery.

The private Seq UI URL is optional. Without one, deployment can still succeed. After deployment, a Platform Owner can use **Configure Seq access** to save or change a server-approved private LAN, VPN, or SSH-forwarded browser URL. MEM then exposes **Open Seq** in a new tab. Do not save `0.0.0.0`; it is a bind address rather than a browser destination.

If the runtime becomes healthy but connection provisioning, verification, or delivery preparation fails, MEM preserves the healthy Seq runtime and its data and reports that setup needs attention. Use the normal **Connect MEM to Seq** or delivery controls to retry the remaining step; do not redeploy a healthy runtime merely to recover the integration step.

When setup completes with event delivery selected, the current API process is not silently reconfigured. The Seq workspace reports that an API restart is required and uses the server-owned restart contract for the active runtime context. Where the server provides an exact restart command, MEM displays it for copying. After the restart, reopen the workspace and verify that current and desired delivery agree and that normal structured events are arriving in Seq.

## Lifecycle controls

Depending on server-provided capabilities, a Platform Owner can:

- deploy the approved local image;
- start, stop, or restart the MEM-managed runtime;
- run a bounded health check;
- enable or disable future event delivery;
- remove the managed container while retaining its data directory.

Deploy, delivery changes, and removal require fresh identity verification. Stop and restart use explicit confirmation. Start and restart report success only after Docker state and Seq health are verified.

An unmanaged same-name container or immutable-identity mismatch fails closed.

## Apply delivery changes

Delivery preference changes are staged. The workspace shows both current and desired state and tells you when an API restart is required.

After changing delivery:

1. confirm the desired state in `/diagnostics/seq`;
2. restart the MEM API deliberately;
3. reopen the workspace;
4. confirm current and desired state now agree;
5. run a health check or pipeline verification.

Do not assume the current process changed delivery merely because the preference was saved.

## Remove Seq safely

Removal is blocked while current or desired delivery remains enabled. A successful removal preserves the configured Seq data directory. Permanent Seq data deletion is not part of this workflow.

## Starter searches

Useful fields include incident ID, operation ID, feature, event code, resource kind, and warning/error level. The workspace provides copyable examples validated for the approved Seq release.

Official references:

- [Seq documentation](https://datalust.co/docs)
- [Run Seq with Docker](https://datalust.co/docs/getting-started?platform=docker)
- [Seq query language](https://datalust.co/docs/the-seq-query-language)
- [Query syntax](https://datalust.co/docs/query-syntax)

## Security boundary

Seq credentials remain server-side. Do not put an ingestion key, administrator password hash, or secret-file content in browser storage, a support report, a screenshot, or an ordinary log message.

Seq is private operator tooling. MEM does not create public ingress for it automatically.

## Rollback

To roll back the Web workspace, hide the Seq management route while leaving the runtime and MEM-native Diagnostics untouched. To stop delivery, set the desired state to disabled and restart the API. Removing the runtime preserves data; data deletion requires a separate future workflow.

## Related documentation

- [Use the Diagnostics command centre](diagnostics-command-centre.md)
- [Use Portainer for advanced container diagnostics](../tools/optional-portainer.md)
