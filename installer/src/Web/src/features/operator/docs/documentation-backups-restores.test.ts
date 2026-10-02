import manifestJson from "./release-pack/metadata/manifest.json?raw"

import { describe, expect, it } from "vitest"

import { getDocumentationView } from "./documentation-release-pack"

type ManifestDocument = Readonly<{
  id: string
  translationKey: string
  locale: "en" | "de"
  groupKey: string
  status: string
  appliesTo: readonly string[]
}>

type DocumentationManifest = Readonly<{
  sourceContentVersion: string
  documents: readonly ManifestDocument[]
}>

const backupRestoreKeys = [
  "backups-restores",
  "backups-restores/create-backup",
  "backups-restores/backup-catalog",
  "backups-restores/portable-export",
  "backups-restores/import-backup",
  "backups-restores/restore-workspace",
  "backups-restores/private-test",
  "backups-restores/standard-recreate",
  "backups-restores/target-claims",
  "backups-restores/verify-and-complete",
  "backups-restores/cancel-retry-failure",
  "backups-restores/evidence-logs-support",
  "backups-restores/deletion-retention",
  "backups-restores/restore-or-migrate",
] as const

describe("MEM 0.2.0 backup and restore documentation", () => {
  it("ships one supported English and German variant for every backup and restore topic", () => {
    const manifest = JSON.parse(manifestJson) as DocumentationManifest
    const documents = manifest.documents.filter(
      (document) => document.groupKey === "backups-restores",
    )

    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(documents).toHaveLength(backupRestoreKeys.length * 2)

    for (const key of backupRestoreKeys) {
      const variants = documents.filter((document) => document.translationKey === key)

      expect(variants.map((document) => document.locale).sort()).toEqual(["de", "en"])
      expect(variants.every((document) => document.status === "supported")).toBe(true)
      expect(variants.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
    }
  })

  it("documents the current catalog, portability, workspace, recovery, verification, and safety boundaries", () => {
    const view = getDocumentationView("en")
    const overview = view.documentByKey.get("backups-restores")!
    const capture = view.documentByKey.get("backups-restores/create-backup")!
    const catalog = view.documentByKey.get("backups-restores/backup-catalog")!
    const portable = view.documentByKey.get("backups-restores/portable-export")!
    const importGuide = view.documentByKey.get("backups-restores/import-backup")!
    const workspace = view.documentByKey.get("backups-restores/restore-workspace")!
    const privateTest = view.documentByKey.get("backups-restores/private-test")!
    const recreate = view.documentByKey.get("backups-restores/standard-recreate")!
    const claims = view.documentByKey.get("backups-restores/target-claims")!
    const verification = view.documentByKey.get("backups-restores/verify-and-complete")!
    const failure = view.documentByKey.get("backups-restores/cancel-retry-failure")!
    const evidence = view.documentByKey.get("backups-restores/evidence-logs-support")!
    const retention = view.documentByKey.get("backups-restores/deletion-retention")!
    const decision = view.documentByKey.get("backups-restores/restore-or-migrate")!

    expect(overview.markdown).toContain("Backup Catalog is the only supported restore source")
    expect(overview.markdown).toContain("validation ID")
    expect(capture.markdown).toContain("PostgreSQL dump")
    expect(capture.markdown).toContain("media_store")
    expect(capture.markdown).toContain("Matrix signing key")
    expect(catalog.markdown).toContain("local-captured")
    expect(catalog.markdown).toContain("imported-zip")
    expect(catalog.markdown).toContain("advisories")
    expect(portable.markdown).toContain("off-host")
    expect(portable.markdown).toContain("signing key")
    expect(importGuide.markdown).toContain("validation ID")
    expect(importGuide.markdown).toContain("catalog entry ID")
    expect(importGuide.markdown).toContain("restore-session ID")
    expect(workspace.markdown).toContain("five operator steps")
    expect(workspace.markdown).toContain("durable")
    expect(privateTest.markdown).toContain("internal Docker network")
    expect(privateTest.markdown).toContain("no public routes")
    expect(recreate.markdown).toContain("no automatic rollback")
    expect(recreate.markdown).toContain("TURN fidelity")
    expect(claims.markdown).toContain("not a reservation")
    expect(claims.markdown).toContain("claims the values atomically")
    expect(verification.markdown).toContain("Doctor workflow")
    expect(verification.markdown).toContain("Complete and hand over")
    expect(failure.markdown).toContain("queued or running")
    expect(failure.markdown).toContain("releases temporary restore target claims")
    expect(evidence.markdown).toContain("connection strings")
    expect(evidence.markdown).toContain("PostgreSQL dump")
    expect(retention.markdown).toContain("downloaded off-host ZIP")
    expect(decision.markdown).toContain("MEM Migrate")
    expect(decision.markdown).toContain("MEM 0.1.0")
  })
})
