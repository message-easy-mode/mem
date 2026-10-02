# MEM Web localisation alignment and migration plan

**Status:** Design and migration note. No React/TypeScript implementation is
included in L17.

**Scope:** Align the existing Web i18n system with the repository-level
localisation boundary introduced for the CLI and forward-only structured API
messages. This plan deliberately avoids a rewrite of the current Web
localisation runtime.

## Why this exists

MEM now has three complementary localisation layers:

```text
API / HostAgent
  stable error codes, raw diagnostic detail, optional structured message code + arguments

CLI
  C# local catalogue renders human-facing CLI-owned output

Web
  TypeScript local catalogue renders browser-owned UI output
```

The API must remain the source of operational truth, not a runtime translation
server. The Web must remain capable of rendering its UI offline from bundled
assets. A structured API message descriptor adds semantic meaning without
making server English prose into a machine contract.

## Web baseline reviewed for this note

The most recent Web source supplied for this design review was the `Web` bundle
from **2 July 2026**. It already has a sound browser-localisation foundation:

```text
Web/src/app/i18n/messages.ts
  typed English/German catalogue and plural message shapes

Web/src/app/i18n/i18n-core.ts
  language selection, English fallback, plural selection, interpolation,
  and en-NZ / de-DE Intl locales

Web/src/app/i18n/i18n-provider.tsx
  React provider, document language, localStorage persistence

Web/src/components/layout/language-select.tsx
  operator language selector
```

The current browser language preference is localStorage key:

```text
mem.ui-language
```

The current supported browser languages are:

```text
en
de
```

That Web source was not included in the L17 source baseline. Before an
implementation slice changes React code, reconfirm the paths, current error
boundaries, and existing message keys against the latest `installer/src/Web`
source. This document does not assert that the 2 July snapshot is still the
current Web implementation.

## Decision

Keep the existing Web i18n runtime and `messages.ts` catalogue as the Web
presentation system. Do **not** make React import `.resx` files, instantiate a
C# localisation library, fetch translated sentences from the API, or replace
the existing provider/selector during this work.

Use the following boundary instead:

```text
C# MemMessageCodes
  stable semantic API codes
        ↓
optional API message descriptor
  { code, arguments }
        ↓
explicit TypeScript code-to-Web-key mapping
        ↓
existing useI18n().t(key, values)
        ↓
English or German browser wording
```

The mapping is intentionally explicit. A server code must never be used as a
dynamic Web translation key, and raw server `detail` must never be parsed to
infer a translated meaning.

## Source-of-truth rules

| Concern | Source of truth | Rule |
|---|---|---|
| HTTP route, JSON field, ID, error code, event code | API contract | Stable English machine contract; never translate. |
| Structured API semantic code | `MemMessageCodes` in C# | Additive and forward-only; immutable after release. |
| API arguments | `LocalizedMessage.Arguments` | Small named scalar data only; never HTML, paths, credentials, tokens, or exception dumps. |
| Browser UI copy | Web `messages.ts` | Remains owned and bundled by Web. |
| CLI output copy | `Mem.Localization` `.resx` | Remains owned by the C# CLI presentation layer. |
| Product terminology | `Locales/Glossary.en-de.md` | Shared governance reference for human wording. |
| Raw diagnostic evidence | Server `detail`, logs, evidence | Preserve verbatim; do not translate or use as a machine contract. |

A human sentence does not need byte-for-byte equivalence between Web and CLI.
They should follow the glossary and convey the same operational meaning in the
appropriate medium.

## API problem rendering contract for Web

L16 introduced an optional additive shape:

```json
{
  "error": "restore_attempt_not_found",
  "detail": "Restore attempt '20260703-071259Z-example' was not found.",
  "message": {
    "code": "restore.attempt.not-found",
    "arguments": {
      "restoreSessionId": "20260703-071259Z-example"
    }
  }
}
```

A future Web adapter should apply these rules:

1. When `message.code` is recognised in the relevant feature boundary, render
   the mapped Web catalogue key using `message.arguments`.
2. Keep `detail` available as technical diagnostic information. It should not
   disappear merely because a friendly localised explanation was rendered.
3. When `message` is absent, malformed, or unknown, preserve the existing Web
   behaviour and use the server `detail` or the feature's established generic
   error copy.
4. Never translate arbitrary server text, dynamically construct a translation
   key from `message.code`, or treat `error`/`detail` as a translation key.
5. Treat argument values as data. React text rendering escapes values by
   default; do not use `dangerouslySetInnerHTML` for them.
6. Do not localise IDs, paths, URLs, commands, flags, environment variables,
   error codes, or raw evidence embedded in an argument value.

The result is graceful version skew in both directions:

```text
new Web + old API
  message absent -> existing detail/generic fallback

new Web + unknown future API code
  unknown code -> existing detail/generic fallback

old Web + new API
  unknown JSON property ignored -> existing detail/generic behaviour
```

## Proposed TypeScript adapter shape

This is an implementation sketch, not code to add in L17:

```ts
type ApiStructuredMessage = Readonly<{
  code: string
  arguments?: Readonly<Record<string, string | number | boolean | null>>
}>

type ApiProblem = Readonly<{
  error?: string
  detail?: string
  message?: ApiStructuredMessage
}>

type ApiMessageTranslation = Readonly<{
  key: TranslationKey
  acceptedArguments: readonly string[]
}>
```

The feature-local adapter should use an explicit mapping, for example:

```ts
const restoreApiMessageTranslations = {
  "restore.attempt.not-found": {
    key: "restoreWorkspace.api.attemptNotFound",
    acceptedArguments: ["restoreSessionId"],
  },
} as const
```

A small renderer can then return either:

```text
localised user-facing explanation + retained technical detail
```

or, for an unrecognised descriptor:

```text
existing server detail / established generic error
```

The mapping should be feature-local at first. Do not create a global registry
until at least two Web areas need shared behaviour and the duplication is real.

## Initial mapping candidates from L16

The following codes are eligible only where the relevant Web route/action is
being touched. They do not require an immediate Web implementation.

| API code | Required arguments | Suggested Web key namespace |
|---|---|---|
| `backup-catalog.entry.not-found` | `catalogEntryId` | `backupCatalog.api.entryNotFound` |
| `backup-catalog.entry.unavailable` | `catalogEntryId`, `payloadState` | `backupCatalog.api.entryUnavailable` |
| `backup-catalog.restore-request.invalid` | `catalogEntryId` | `backupCatalog.api.restoreRequestInvalid` |
| `restore.attempt.not-found` | `restoreSessionId` | `restoreWorkspace.api.attemptNotFound` |
| `restore.attempt-request.invalid` | `restoreSessionId` | `restoreWorkspace.api.attemptRequestInvalid` |
| `restore.workspace-request.invalid` | `restoreSessionId` | `restoreWorkspace.api.requestInvalid` |
| `restore.private-test.not-available` | `restoreSessionId` | `restoreWorkspace.api.privateTestNotAvailable` |
| `restore.cancel.acknowledgement-required` | `restoreSessionId` | `restoreWorkspace.cancel.acknowledgementRequired` |
| `restore.cancel.not-available` | `restoreSessionId` | `restoreWorkspace.cancel.notAvailable` |
| `restore.handover.acknowledgement-required` | `restoreSessionId` | `restoreWorkspace.handover.acknowledgementRequired` |
| `restore.handover.not-available` | `restoreSessionId` | `restoreWorkspace.handover.notAvailable` |
| `restore.private-test.started` | `restoreSessionId`, `sourceKind`, `catalogEntryId` | `restoreWorkspace.event.privateTestStarted` |

Create an English and German Web message for a mapping in the same Web slice
that introduces its renderer. Do not add unused strings to `messages.ts` now.

## Planned migration sequence

### W1 — typed Web problem parser and single feature adapter

When the latest Web source is supplied, add a minimal TypeScript representation
of optional structured API messages and a feature-local adapter for one
canonical restore-workspace error boundary.

Scope:

```text
one API client/problem type
one explicit code-to-key map
one error boundary/action path
English and German messages
unit tests for known, unknown, and absent descriptors
```

Do not change API routes, C# contracts, or the global i18n provider.

### W2 — technical-detail presentation

Where an affected error boundary currently shows only server prose, retain the
raw `detail` in the existing technical-details affordance or add a modest,
collapsed technical-details section. It must preserve diagnostic text without
presenting it as translated UI copy.

Do not expose sensitive fields. L16 structured descriptors intentionally carry
only safe scalar values; raw detail should already be redacted by the server.

### W3 — restore structured-event projection

When a restore timeline/log projection is next touched, render recognised
`eventCode` + `details` values through a feature-local Web mapping. Preserve
raw event text and NDJSON/history compatibility. Do not rewrite historical
records or introduce translated text into stored evidence.

### W4 — progressively adopt other touched workflows

Adopt the same pattern only as a Web feature is materially changed:

```text
Backup Catalog
Restore Workspace
Security
Diagnostics
Stack operations
Setup
```

There is no requirement to retrofit every existing error response or screen.

### W5 — reassess shared assets only after evidence

After several Web and CLI modules use the same semantic messages, reassess
whether a neutral, generated catalogue format would reduce maintenance. Until
then, keep:

```text
Web: typed TypeScript catalogue
CLI: .resx catalogue
API: codes + arguments
Glossary: shared terminology governance
```

Do not introduce code generation, an external translation-management service,
or a runtime asset download merely to remove a small amount of deliberate
catalogue duplication.

## Language preference policy

The Web and CLI currently choose language locally:

```text
Web
  saved mem.ui-language -> browser preference -> English

CLI
  --language -> MEM_CLI_LANGUAGE -> operating-system language -> English
```

Keep these preferences independent. The API should not infer, persist, or
broadcast an operator language as part of an operational request. Different
operator surfaces may be used by different people or automation contexts.

A future authenticated operator-profile preference could be considered only as
an optional Web convenience; it must not alter API, CLI, JSON, audit, or
support contracts.

## Test plan for the first Web implementation slice

At minimum, add focused Vitest coverage proving:

1. A recognised descriptor renders the correct English and German Web message.
2. Its named arguments interpolate without changing the literal ID/value.
3. An absent descriptor preserves the existing server-detail/generic fallback.
4. An unknown descriptor preserves the existing server-detail/generic fallback.
5. The raw diagnostic detail remains available through the chosen technical
   details path.
6. English/German catalogue completeness remains enforced by TypeScript's
   `satisfies TranslationCatalog` check and the focused tests.
7. A real affected page/action works under the current browser E2E setup only
   when suitable non-mutating live data is available; record intentional skips
   honestly.

Use the existing local E2E environment variables when an E2E test is added:

```text
MEM_E2E_BASE_URL
MEM_E2E_SETUP_TOKEN
MEM_E2E_AGENT_SECRET
```

## Pseudo-locale follow-up for Web

`qps-ploc` remains a test-only C# localisation tool. Do not add it to the
production Web language selector.

A future Web-only test harness may derive a pseudo rendering from English
messages to detect hard-coded English and layout clipping. It should be a
separate Web test/tooling slice and must retain the same protections for
placeholders, IDs, URLs, commands, flags, environment variables, and language
codes.

## Explicit non-goals

L17 does not:

- modify `installer/src/Web`;
- replace the existing Web i18n provider, selector, catalogue, or localStorage
  preference;
- make the Web consume `.resx` at runtime or build time;
- make the API translate server prose;
- translate historical logs, evidence, exception text, Docker output, database
  output, paths, or support attachments;
- introduce a third production language;
- synchronise Web and CLI language preferences;
- add an external translation service or network dependency.

## Completion criterion

L17 is complete when this document is present beside the C# localisation
conventions, linked from `Mem.Localization/README.md`, and used as the
starting design reference for the first future Web implementation slice.
