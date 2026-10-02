---
title: Understand storage and media
description: Use the fleet Storage inventory and each chat server's Services workspace to review Matrix media and recovery-relevant storage evidence.
section: Operate chat servers
order: 80
---

# Understand storage and media

## Outcome

Use the top-level **Storage** workspace for a read-only fleet inventory of Matrix media across managed chat servers.

Open a chat server and select **Services** for detailed storage and media evidence owned by that stack.

The Storage surfaces are inspection tools. They are not file managers and do not edit, prune, move, or delete media.

## Fleet Storage workspace

Open **Storage** from the main operator navigation to compare managed chat servers without expanding every technical filesystem detail.

The fleet workspace starts with aggregate MEM-managed Matrix-media totals across all successfully inspected chat servers and the safely observed capacity of the filesystem that backs MEM data. The capacity figure is authoritative for the configured MEM data filesystem; it may be a dedicated volume and is not necessarily the Ubuntu host's entire disk. If MEM cannot prove the backing filesystem safely, the capacity is shown as unavailable instead of inferring the Control Plane container overlay. The overview includes:

- percentage and bytes still available on the MEM data filesystem;
- managed Matrix-media size;
- total media-file count;
- local-upload usage;
- available media stores;
- remote-media cache, generated-thumbnail, and URL-preview-cache usage.

The inventory below those totals shows each chat server's Matrix-media size, local uploads, remote cache, media-store availability, and a link to the owning chat server's detailed **Services** workspace.

The page refreshes read-only runtime evidence automatically and also provides one fleet Refresh action. Technical filesystem paths are intentionally kept out of the fleet table.

## Detailed storage evidence

Open **Chat servers → <server> → Services** to review the detailed storage evidence for one chat server.

The Services workspace can show:

- Matrix data path;
- media-store path and total size;
- media file count;
- `homeserver.yaml` presence and size;
- the Matrix signing key presence and size;
- Element data and config paths;
- media-section inventory such as local uploads, remote cache, thumbnails, and URL previews.

Technical filesystem paths are support and recovery evidence. They remain in the detailed stack workspace instead of the fleet inventory.

## Recovery boundary

Matrix media works as a pair:

- PostgreSQL stores event, metadata, and media references;
- the Synapse media directory stores the file bytes.

A recoverable backup therefore needs both the stack database dump and the media directory. Synapse configuration, signing key, and Element configuration are also recovery-relevant.

> [!WARNING]
> Copying only the media directory is not a complete Matrix backup. Copying only PostgreSQL can leave media references without their files.

## Capacity planning

Use the **MEM data storage available** figure to watch the filesystem that actually carries MEM's configured data root. The Storage workspace reports available bytes, total capacity, and the percentage remaining when that filesystem can be observed safely in the active runtime. Home uses the same authoritative MEM-data capacity projection.

Watch media growth together with database, backup, and migration workspace usage. The installation preflight threshold is not a lifetime capacity guarantee.

Remote media cache and generated derivatives can consume space even when local users upload little. Use the fleet inventory to identify growth, then open the chat server's Services workspace for the detailed section evidence.

## Avoid manual cleanup

Do not delete the signing key, `homeserver.yaml`, database volume, or media folders to reclaim space. Manual cleanup can break identity, history, federation, or recovery fidelity.

Create a verified backup and use an explicit supported lifecycle workflow for removal or restore work.
