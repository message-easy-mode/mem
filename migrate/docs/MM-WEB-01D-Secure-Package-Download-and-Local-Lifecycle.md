# MM-WEB-01D — Secure Package Download and Local Package Lifecycle

## Purpose

MM-WEB-01D completes the first Source Assistant package handoff.

After a verified source capture has been encrypted for the target Migration
Session, the operator can:

1. download the encrypted `.memmigration.zip.age` package through the browser;
2. download a browser-safe package report;
3. verify the displayed SHA-256 after transfer;
4. delete the local encrypted package and package reports after the target has
   accepted the upload.

The verified plaintext source capture, source assessment, migration journal,
Matrix data and old MEM server are not deleted by the local package cleanup
action.

## Download contract

The authenticated Source Assistant exposes:

```http
GET /api/workflows/{workflowId}/package/download
GET /api/workflows/{workflowId}/package/report
DELETE /api/workflows/{workflowId}/package
```

The package response:

- streams directly from the configured private package root;
- uses `Content-Disposition: attachment`;
- uses `application/octet-stream`;
- enables HTTP range processing;
- returns `Cache-Control: no-store`;
- returns the durable package SHA-256 in `X-MEM-Package-SHA256`;
- never returns a source filesystem path.

The browser uses an ordinary authenticated download link rather than loading
the package into React memory.

## Package-root safety

The application resolves package artifacts only beneath:

```text
<artifact-root>/packages
```

The encrypted package and package-report paths must:

- be direct children of the configured package root;
- use the expected file suffix;
- be regular files;
- not be symbolic links;
- not be multiply hard-linked on Linux;
- retain the expected encrypted-package byte length.

Unsafe or externally modified package files are not served.

## Active-download deletion guard

The Web host holds a process-local download lease for the lifetime of every
package response.

While any browser download is active, the delete operation returns a conflict
and does not remove package artifacts.

This prevents Linux unlink semantics from deleting the visible package path
while an HTTP response is still streaming from an open file.

## Browser-safe package report

The report download is generated from durable package evidence.

It includes:

- Intake and Package Revision IDs;
- recipient fingerprint;
- migration and capture identity;
- source archive SHA-256;
- verification counts;
- encrypted-package filename, SHA-256 and size.

It omits:

- source archive paths;
- encrypted-package paths;
- target paths;
- the target private age identity;
- Matrix E2EE recovery secrets.

## Local deletion

The delete confirmation states exactly what is removed.

Deleted:

```text
encrypted .memmigration.zip.age package
local JSON package report
local Markdown package report
```

Retained:

```text
verified plaintext source capture
assessment history
source workflow journal
old MEM installation
Matrix data
```

Deletion updates the durable workflow stage to:

```text
PackageDeleted
```

The package metadata remains visible as historical evidence, but download and
delete controls are disabled.

## External changes

If package artifacts are removed or replaced outside Source Assistant, the
workflow projects one of:

```text
Available
Partial
Missing
Unsafe
Deleted
```

The browser does not claim that an unavailable or unsafe file can be
downloaded.

## Security

All endpoints require the Source Assistant access-code session.

The delete endpoint is state changing and therefore also requires the existing
same-origin and CSRF checks.

No EF migration is required.

No source SQLite schema migration is required. The existing workflow stage and
timestamp fields record local package deletion.
