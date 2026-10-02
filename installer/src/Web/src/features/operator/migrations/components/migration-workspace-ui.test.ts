import { describe, expect, it } from "vitest"

import { translate } from "@/app/i18n/i18n-core"
import { translationCatalog } from "@/app/i18n/messages"
import type { MigrationWorkspaceOperationSummary, MigrationWorkspaceStage } from "@/features/operator/migrations/api/migration-workspace"
import { actionKey, initialOpenMigrationStage, operationSummaryKey } from "./migration-workspace-ui"

function stage(code: string, state: string, unlocked = true): MigrationWorkspaceStage {
  return {
    code,
    state,
    unlocked,
    completedAtUtc: null,
    primaryAction: null,
    secondaryActions: [],
    summaryCode: "",
    problems: [],
    evidenceSummary: { itemCount: 0, latestOccurredAtUtc: null, latestStatus: null },
    operationSummary: null,
    failureOutcome: null,
  }
}

describe("initialOpenMigrationStage", () => {
  it("opens running and failed work before a merely ready stage", () => {
    expect(initialOpenMigrationStage([
      stage("package", "completed"),
      stage("review", "ready"),
      stage("prepare", "running"),
    ])).toBe("prepare")

    expect(initialOpenMigrationStage([
      stage("package", "completed"),
      stage("review", "failed"),
      stage("prepare", "not-started", false),
    ])).toBe("review")
  })

  it("opens the next ready stage and then the latest completed milestone", () => {
    expect(initialOpenMigrationStage([
      stage("package", "completed"),
      stage("review", "ready"),
      stage("prepare", "not-started", false),
    ])).toBe("review")

    expect(initialOpenMigrationStage([
      stage("package", "completed"),
      stage("review", "completed"),
      stage("prepare", "completed"),
    ])).toBe("prepare")
  })
})

function operationSummary(operation: string, status: string, currentStep: string | null = null): MigrationWorkspaceOperationSummary {
  return {
    operationId: "operation-copy-test",
    operation,
    status,
    currentStep,
    requestedAtUtc: "2026-09-21T00:00:00Z",
    startedAtUtc: null,
    completedAtUtc: null,
  }
}

describe("guided action copy", () => {
  it.each(["en", "de"] as const)("aligns the tested-snapshot action with the existing creation button in %s", (language) => {
    expect(actionKey("confirm-tested-data")).toBe(actionKey("create-new-server"))
    const label = translate(language, actionKey("confirm-tested-data"))
    expect(label).toBe(translate(language, "migrationWorkspace.privateServer.create"))
    expect(label).toBe(translate(language, "migrationWorkspace.privateServer.confirmAction"))
  })
})

describe("operationSummaryKey", () => {
  // Operation/status pairs emitted by the supplied workspace projection. English
  // and German are checked explicitly, so English fallback cannot hide a gap.
  it.each([
    ["prepare-migration-data", "pending", "Migration data preparation queued", "Vorbereitung der Migrationsdaten eingereiht"],
    ["prepare-migration-data", "running", "Preparing migration data", "Migrationsdaten werden vorbereitet"],
    ["prepare-migration-data", "completed", "Migration data prepared", "Migrationsdaten vorbereitet"],
    ["prepare-migration-data", "completed-with-warnings", "Migration data prepared with warnings", "Migrationsdaten mit Warnungen vorbereitet"],
    ["prepare-migration-data", "failed", "Migration data preparation failed", "Vorbereitung der Migrationsdaten fehlgeschlagen"],
    ["private-test", "running", "Testing a private copy", "Private Kopie wird getestet"],
    ["private-test", "verified", "Private test passed", "Privater Test bestanden"],
    ["private-test", "failed", "Private test failed", "Privater Test fehlgeschlagen"],
    ["private-test", "failed-cleaned", "Private test failed; temporary resources cleaned up", "Privater Test fehlgeschlagen; temporäre Ressourcen bereinigt"],
    ["private-test", "destroyed", "Private test removed", "Privater Test entfernt"],
    ["prepare-new-server", "prepared", "New server details ready", "Angaben zum neuen Server bereit"],
    ["prepare-new-server", "blocked", "New server details need attention", "Angaben zum neuen Server müssen geprüft werden"],
    ["create-new-server", "materializing", "Creating the new server", "Neuer Server wird erstellt"],
    ["create-new-server", "failed", "Private server creation failed", "Erstellung des privaten Servers fehlgeschlagen"],
    ["make-server-live", "private-runtime-ready", "New server created privately", "Neuer Server privat erstellt"],
    ["make-server-live", "cutover-preview-ready", "Public route review ready", "Prüfung der öffentlichen Routen bereit"],
    ["make-server-live", "executing", "Changing public routes", "Öffentliche Routen werden geändert"],
    ["make-server-live", "public-awaiting-verification", "Public routes changed; verification pending", "Öffentliche Routen geändert; Prüfung ausstehend"],
    ["make-server-live", "failed", "Public route change failed", "Änderung der öffentlichen Routen fehlgeschlagen"],
    ["live-verification", "running", "Checking the live server", "Live-Server wird geprüft"],
    ["live-verification", "passed", "Live server checks passed", "Prüfungen des Live-Servers bestanden"],
    ["live-verification", "failed", "Live server checks failed", "Prüfungen des Live-Servers fehlgeschlagen"],
    ["migration-rollback", "preview-ready", "Rollback review ready", "Rücksetzungsprüfung bereit"],
    ["migration-rollback", "preview-blocked", "Rollback review blocked", "Rücksetzungsprüfung blockiert"],
    ["migration-rollback", "executing", "Rolling back the migration", "Migration wird zurückgesetzt"],
    ["migration-rollback", "target-rolled-back-awaiting-source", "Target rolled back; source recovery pending", "Ziel zurückgesetzt; Wiederherstellung der Quelle ausstehend"],
    ["migration-rollback", "failed", "Migration rollback failed", "Zurücksetzen der Migration fehlgeschlagen"],
    ["migration-rollback", "coordinated-rollback-complete", "Coordinated rollback completed", "Koordinierte Rücksetzung abgeschlossen"],
    ["baseline-backup", "pending", "First native backup pending", "Erste native Sicherung ausstehend"],
    ["baseline-backup", "created", "First native backup created", "Erste native Sicherung erstellt"],
    ["baseline-backup", "failed", "First native backup failed", "Erste native Sicherung fehlgeschlagen"],
  ])("localizes %s / %s without exposing the last technical step", (operation, status, english, german) => {
    const key = operationSummaryKey(operationSummary(operation, status, "last-technical-step"))
    expect(translate("en", key)).toBe(english)
    expect(translate("de", key)).toBe(german)
    expect(translationCatalog.en[key]).toBe(english)
    expect(translationCatalog.de[key]).toBe(german)
  })

  it.each([
    ["future-operation", "created"],
    ["create-new-server", "created"],
    ["private-test", "completed"],
    ["baseline-backup", "completed"],
    ["baseline-backup", "future-status"],
    ["prepare-migration-data", "candidate-created"],
    ["", ""],
    ["constructor", "name"],
    ["baseline-backup", "toString"],
    ["__proto__", "created"],
  ])("keeps an unknown %s / %s neutral even when its last step looks successful", (operation, status) => {
    const key = operationSummaryKey(operationSummary(operation, status, "created"))
    expect(key).toBe("migrationWorkspace.guide.operationStatus.technical")
    expect(translate("en", key)).toBe("See technical evidence for activity details")
    expect(translate("de", key)).toBe("Aktivitätsdetails finden Sie in den technischen Nachweisen")
  })

  it.each([
    ["prepare-migration-data", "candidate-created", "Migration data preparation failed"],
    ["private-test", "private-verification-complete", "Private test failed"],
    ["create-new-server", "private-runtime-ready", "Private server creation failed"],
    ["make-server-live", "cutover-preview-ready", "Public route change failed"],
    ["live-verification", "production-verification-passed", "Live server checks failed"],
    ["migration-rollback", "coordinated-rollback-complete", "Migration rollback failed"],
    ["baseline-backup", "created", "First native backup failed"],
  ])("does not replace a failed %s with stale step %s", (operation, currentStep, expected) => {
    expect(translate("en", operationSummaryKey(operationSummary(operation, "failed", currentStep)))).toBe(expected)
  })

  it("uses a known status when the detailed step is absent or not recognized", () => {
    for (const currentStep of [null, "", "future-worker-phase", "candidate-created"]) {
      expect(translate("en", operationSummaryKey(operationSummary("prepare-migration-data", "running", currentStep))))
        .toBe("Preparing migration data")
    }
  })

  it("does not mutate or replace the server-authored operation evidence", () => {
    const summary = Object.freeze(operationSummary("baseline-backup", "created", "created"))
    const before = { ...summary }
    operationSummaryKey(summary)
    expect(summary).toEqual(before)
  })
})
