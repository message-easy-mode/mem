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

const migrationKeys = [
  "migrate",
  "migrate/decide-and-prepare",
  "migrate/install-source-assistant",
  "migrate/assess-and-select",
  "migrate/create-package",
  "migrate/upload-and-review",
  "migrate/private-test",
  "migrate/create-new-server",
  "migrate/go-live",
  "migrate/finish",
  "migrate/rollback",
  "migrate/cleanup",
  "migrate/session-lifecycle",
  "migrate/evidence-and-support",
  "migrate/migration-or-restore",
] as const

describe("MEM 0.2.0 migration documentation", () => {
  it("ships one supported English and German variant for every migration topic", () => {
    const manifest = JSON.parse(manifestJson) as DocumentationManifest
    const documents = manifest.documents.filter((document) => document.groupKey === "migrate")

    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(documents).toHaveLength(migrationKeys.length * 2)

    for (const key of migrationKeys) {
      const variants = documents.filter((document) => document.translationKey === key)

      expect(variants.map((document) => document.locale).sort()).toEqual(["de", "en"])
      expect(variants.every((document) => document.status === "supported")).toBe(true)
      expect(variants.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
    }
  })

  it("documents the source, package, staging, cutover, acceptance, rollback, and lifecycle boundaries", () => {
    const view = getDocumentationView("en")
    const overview = view.documentByKey.get("migrate")!
    const preparation = view.documentByKey.get("migrate/decide-and-prepare")!
    const sourceAssistant = view.documentByKey.get("migrate/install-source-assistant")!
    const assessment = view.documentByKey.get("migrate/assess-and-select")!
    const packageGuide = view.documentByKey.get("migrate/create-package")!
    const upload = view.documentByKey.get("migrate/upload-and-review")!
    const privateTest = view.documentByKey.get("migrate/private-test")!
    const target = view.documentByKey.get("migrate/create-new-server")!
    const goLive = view.documentByKey.get("migrate/go-live")!
    const finish = view.documentByKey.get("migrate/finish")!
    const rollback = view.documentByKey.get("migrate/rollback")!
    const cleanup = view.documentByKey.get("migrate/cleanup")!
    const lifecycle = view.documentByKey.get("migrate/session-lifecycle")!
    const evidence = view.documentByKey.get("migrate/evidence-and-support")!
    const decision = view.documentByKey.get("migrate/migration-or-restore")!

    expect(overview.markdown).toContain("Migrate **one stack at a time**")
    expect(overview.markdown).toContain("Create and upload package")
    expect(preparation.markdown).toContain("another trusted and verified device")
    expect(sourceAssistant.markdown).toContain("127.0.0.1:7391")
    expect(sourceAssistant.markdown).toContain("Public internet exposure is unsupported")
    expect(sourceAssistant.markdown).toContain("mem-migrate-start")
    expect(assessment.markdown).toContain("The selection is authoritative")
    expect(packageGuide.markdown).toContain(".memmigration.zip.age")
    expect(packageGuide.markdown).toContain("target private decryption identity")
    expect(upload.markdown).toContain("does not offer a second stack choice")
    expect(privateTest.markdown).toContain("no public routes")
    expect(privateTest.markdown).toContain("Backup Catalog entry")
    expect(target.markdown).toContain("Matrix public address is locked")
    expect(target.markdown).toContain("final-frozen")
    expect(goLive.markdown).toContain("does not create DNS records")
    expect(goLive.markdown).toContain("does not automatically roll back")
    expect(finish.markdown).toContain("first native MEM backup")
    expect(finish.markdown).toContain("acceptance remains durable")
    expect(rollback.markdown).toContain("sourceFrozen=true")
    expect(rollback.markdown).toContain("does not synchronize those changes back")
    expect(cleanup.markdown).toContain("never automatically deletes the old host")
    expect(lifecycle.markdown).toContain("Restore to active list")
    expect(lifecycle.markdown).toContain("exact Migration ID")
    expect(evidence.markdown).toContain("Route ownership")
    expect(evidence.markdown).toContain("authorization headers")
    expect(decision.markdown).toContain("must not appear as ordinary Backup Catalog entries")
    expect(decision.markdown).toContain("Not supported by this migration guide")
  })
})
