# MEM English–German Product Glossary

**Status:** Working product glossary for MEM localisation. It is the canonical
source for reviewed CLI, Web, documentation, and support-copy terminology.
The German terms below are approved for current engineering use, but should be
reviewed by a native German technical translator before a public release is
presented as linguistically final.

## Rules

- Use the English product term in source code, message keys, API/JSON fields,
  CLI commands and flags, event codes, and documentation anchors.
- Use the listed German term consistently in human-facing text. Do not create a
  second translation only because a synonym feels natural in one screen.
- Preserve literal IDs, URLs, paths, commands, flags, environment variables,
  status/error codes, and raw diagnostic text unchanged.
- Distinguish a **Backup Catalog** record from an uploaded **ZIP archive**.
  A catalog entry is a durable managed restore source; an archive is only an
  upload/export artifact or provenance record.
- Add a glossary entry before introducing a new capitalised product term to an
  operator-facing workflow.

## Core recovery vocabulary

| Canonical English | German | Usage rule |
|---|---|---|
| Backup | Sicherung | General operator-facing term for a recoverable backup. |
| Backup Catalog | Sicherungskatalog | Use for the durable inventory of managed backup sources. Do not substitute `Archiv` for the catalog. |
| Catalog Entry | Katalogeintrag | A durable source record identified by `catalogEntryId`. |
| Source Backup | Quellsicherung | The recovery source represented by a catalog entry. |
| Portable Export | Portabler Export | A portable MEM ZIP produced from a catalog entry. |
| Uploaded ZIP Archive | hochgeladenes ZIP-Archiv | The retained uploaded artifact/provenance record, not the catalog source itself. |
| Original Archive | Ursprünglich hochgeladenes ZIP-Archiv | Use when distinguishing an original upload from a freshly generated portable export. |
| Payload | Nutzlast | Material held by a catalog entry and needed for restore. |
| Payload State | Nutzlaststatus | Status of source material availability, not an API enum translation. |
| Integrity | Integrität | Validation/integrity result for a source or exported material. |
| Lifecycle | Lebenszyklus | Catalog entry state and deletion eligibility. |
| Local Capture | lokal erfasst | Use for a backup created from a local runtime stack. |
| Imported ZIP | importierte ZIP-Datei | Use for a catalog entry materialised from a portable upload. |
| Validation ID | Validierungs-ID | Uploaded archive provenance identifier only; never a Restore Workspace identity. |
| Catalog Entry ID | Katalogeintrags-ID | Human label for the stable `catalogEntryId` value. |
| Deleted Backup | Gelöschte Sicherung | Historical source display after permanent catalog deletion. |

## Restore workflow vocabulary

| Canonical English | German | Usage rule |
|---|---|---|
| Restore Attempt | Wiederherstellungsversuch | Durable internal/audit record for an attempted restore. |
| Restore Workspace | Wiederherstellungsarbeitsbereich | Operator-facing session/workspace used to inspect and execute restoration stages. |
| Restore Session ID | Wiederherstellungssitzungs-ID | Human label for `restoreSessionId`; retain the ID unchanged. |
| Private Test | Privater Test | Isolated, non-production recovery test. Avoid `private Wiederherstellung` as the product action label. |
| Standard Recreate | Standard-Neuerstellung | Production-adjacent recreate path; pair with clear safety explanation where used. |
| Preflight | Vorabprüfung | Read-only readiness evaluation; do not imply a target has been reserved. |
| Target Claim | Zielreservierung | Temporary protection for a recovery target. |
| Handover | Übergabe | Explicit completion/ownership stage after recovery verification. |
| Evidence | Nachweise | Structured operator/audit evidence produced by MEM. Preserve raw imported evidence verbatim. |
| Support Report | Supportbericht | Generated structured operator-support report. |

## Safety and operational vocabulary

| Canonical English | German | Usage rule |
|---|---|---|
| Permanent Delete | Dauerhaftes Löschen | Use only for irreversible catalog/archive removal actions. |
| Delete Block | Löschsperre | Reason a requested deletion cannot proceed. |
| Active Restore | Aktive Wiederherstellung | An active operation that prevents source deletion. |
| Warning | Warnung | Product-authored operator warning. |
| Detail | Details | Label for raw server-provided detail text; the content itself remains verbatim. |
| Source | Quelle | Generic origin/source label when a more precise term is unnecessary. |
| Provenance | Herkunftsnachweis | Metadata showing where a source came from; not proof that it remains executable. |
| Materialised | materialisiert | Use for archive material copied/registered into managed catalog storage. |

## Non-translated machine terms

The following remain invariant even in German human presentation:

```text
mem backups list
--language
--json
catalogEntryId
restoreSessionId
validationId
MEM_CLI_LANGUAGE
source_deleted
HTTP 404
```
