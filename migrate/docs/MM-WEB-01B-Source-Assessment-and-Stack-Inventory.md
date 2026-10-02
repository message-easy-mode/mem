# MM-WEB-01B — Source assessment and stack inventory

## Purpose

MM-WEB-01B connects the secured Source Assistant shell to the existing,
read-only MEM 0.1.0 assessment engine.

The browser can now:

- inspect source-host preflight;
- run a fresh assessment;
- recover the latest durable assessment from `mem-migrate.sqlite`;
- display the supported-source classification and capture eligibility;
- display the redacted source fingerprint;
- list stacks from the supported legacy application database;
- show Matrix, Element and required source-file readiness;
- select one intended migration stack for the current Source Assistant process;
- download the existing redacted public Markdown assessment report.

The CLI and Web host use the same assessment composition through
`Mem.Migrate.Application`.

## Read-only boundary

Assessment uses the existing `V010AssessmentService`. It inspects Docker,
legacy PostgreSQL, public system configuration and stack files. It does not:

- stop or restart containers;
- mutate the legacy application database;
- freeze the source;
- create a migration capture;
- encrypt a migration package;
- change public routes.

The operation writes only to the configured private workspace and artifact
roots.

## Package-scope disclosure

Selecting a stack records operator intent for the current Source Assistant
process. It does not change the current capture format.

The current MEM 0.1.0 archive remains installation-wide and may contain every
captured source stack. A later target workflow will convert only the selected
stack.

## API

Authenticated endpoints:

```text
GET  /api/preflight
GET  /api/assessments/latest
POST /api/assessments
GET  /api/assessments/latest/report
POST /api/source-stacks/{sourceStackId}/select
```

All POST requests remain protected by the process-local session and CSRF token.
Only the redacted public assessment report can be downloaded.

## Selection lifetime

The selected stack is process-local in MM-WEB-01B:

- it survives browser refresh while the Source Assistant process remains open;
- it is cleared by a fresh assessment;
- it is not yet written to a durable source workflow record;
- it is lost when the temporary process stops.

Durable source workflow state belongs to MM-WEB-01C.

## Focused validation

```bash
cd src/Mem.Migrate.Web/ClientApp
npm run test
npm run build

cd ../../..

dotnet test \
  tests/Mem.Migrate.Web.Tests/Mem.Migrate.Web.Tests.csproj

dotnet test \
  tests/Mem.Migrate.UnitTests/Mem.Migrate.UnitTests.csproj \
  --filter "FullyQualifiedName~V010ClassifierTests|FullyQualifiedName~SourceFingerprintTests"

dotnet test \
  tests/Mem.Migrate.IntegrationTests/Mem.Migrate.IntegrationTests.csproj \
  --filter "FullyQualifiedName~SqliteAssessmentJournalTests|FullyQualifiedName~AssessmentReportWriterTests"

dotnet build src/Mem.Migrate.Web/Mem.Migrate.Web.csproj
```

## Manual proof

On a supported disposable MEM 0.1.0 source:

1. Start `mem-migrate-web` on loopback.
2. Sign in with the generated access code.
3. Confirm preflight reports Docker and private-root state honestly.
4. Run a source assessment.
5. Compare classification and source fingerprint with the CLI assessment.
6. Inspect every detected stack.
7. Select one stack and refresh the browser.
8. Confirm the selection remains visible.
9. Download the assessment report.
10. Confirm private absolute paths, credentials and tokens are not exposed.
11. Stop the Source Assistant and confirm no listener remains.
