import { describe, expect, it } from "vitest"
import { translate } from "@/app/i18n/i18n-core"
import type { I18nContextValue } from "@/app/i18n/i18n-context"
import type { TemporaryStagingInventoryItem } from "../api/temporary-staging.api"
import { makeTemporaryStagingItem, temporaryStagingWorkspaceHref } from "./temporary-staging-item"

const t: I18nContextValue["t"] = (key, values) => translate("en", key, values)
const fixture = (): TemporaryStagingInventoryItem => ({
  resourceGroupId: "stage-one", stagingId: "stage-one", ownershipStatus: "matched",
  owner: { kind: "migration", id: "mig_one", displayName: "Example", workspaceHref: "/migrations/mig_one" },
  runtimeStatus: "running", recordedStatus: "retained", containerCount: 3,
  runningContainerCount: 3, networkCount: 1, containerIds: ["pg", "syn", "el"], reasonCodes: [], canRetire: false,
})

describe("temporary staging navigation", () => {
  it("links only to the exact proven owning workspace", () => {
    const item = makeTemporaryStagingItem(fixture(), t)
    expect(item.workspaceLink).toEqual({ href: "/migrations/mig_one", label: "Open migration" })
    expect(item.serviceHref).toBeNull()
    expect(item.description).toContain("Migration · Example")
    expect(item.description).toContain("Containers: 3 · Networks: 1")
  })

  it.each(["https://example.test/migrations/mig_one", "/stacks/mig_one", "/migrations/mig_other", "/migrations/mig_one?delete=true", "//evil.test"]) (
    "refuses unexpected workspace href %s", (href) => {
      const input = fixture()
      expect(temporaryStagingWorkspaceHref({ ...input, owner: { ...input.owner!, workspaceHref: href } })).toBeNull()
    },
  )

  it("refuses unresolved ownership even when a link is supplied", () => {
    const input = { ...fixture(), ownershipStatus: "unresolved" as const }
    expect(temporaryStagingWorkspaceHref(input)).toBeNull()
    const item = makeTemporaryStagingItem(input, t)
    expect(item.workspaceLink).toBeUndefined()
    expect(item.description).toContain("Ownership unresolved")
  })

  it("honours the server withholding workspace permission", () => {
    const input = fixture()
    input.owner!.workspaceHref = null
    expect(makeTemporaryStagingItem(input, t).workspaceLink).toBeUndefined()
  })

  it("keeps a network-only group and a record-only group truthful", () => {
    const input = { ...fixture(), containerCount: 0, runningContainerCount: 0, containerIds: [] }
    expect(makeTemporaryStagingItem({ ...input, runtimeStatus: "network-only" }, t).statusLabel).toBe("Network remains")
    const item = makeTemporaryStagingItem({ ...input, runtimeStatus: "not-observed", networkCount: 0, ownershipStatus: "recorded" }, t)
    expect(item.statusLabel).toBe("Runtime not observed")
    expect(item.inventoryNote).toContain("does not confirm that temporary files were removed")
  })

  it("disables stale workspace links and does not claim the cached runtime is running", () => {
    const item = makeTemporaryStagingItem(fixture(), t, true)
    expect(item.workspaceLink).toBeUndefined()
    expect(item.running).toBe(false)
    expect(item.statusLabel).toBe("Runtime unavailable")
    expect(item.inventoryNote).toContain("last inventory")
  })

  it("shows retirement attention without inventing a destructive action", () => {
    const item = makeTemporaryStagingItem({ ...fixture(), recordedStatus: "needs-attention" }, t)
    expect(item.statusLabel).toBe("Needs attention")
    expect(item.openUiHref).toBeNull()
    expect(item.actions).toBeUndefined()
    expect(item.inventoryNote).toContain("has not removed anything")
  })

  it("uses German ownership, counts and workspace wording", () => {
    const german: I18nContextValue["t"] = (key, values) => translate("de", key, values)
    const input = fixture()
    input.owner = { kind: "restore", id: "rs_one", displayName: "Beispiel", workspaceHref: "/restores/rs_one" }
    const item = makeTemporaryStagingItem(input, german)
    expect(item.workspaceLink?.label).toBe("Wiederherstellung öffnen")
    expect(item.description).toContain("Wiederherstellung · Beispiel")
    expect(item.description).toContain("Container: 3 · Netzwerke: 1")
  })
  it("offers a review only for a current, exact Migration owner and run", () => {
    const input = { ...fixture(), retirementReview: { migrationId: "mig_one", stagingRunId: "mst_one", status: null } }
    expect(makeTemporaryStagingItem(input, t).retirementReview).toEqual({
      migrationId: "mig_one", stagingRunId: "mst_one", status: null, label: "Review retirement",
    })
    expect(makeTemporaryStagingItem(input, t, true).retirementReview).toBeUndefined()
    expect(makeTemporaryStagingItem({ ...input, ownershipStatus: "unresolved" }, t).retirementReview).toBeUndefined()
    expect(makeTemporaryStagingItem({ ...input, retirementReview: { ...input.retirementReview, migrationId: "mig_other" } }, t).retirementReview).toBeUndefined()
    expect(makeTemporaryStagingItem({ ...input, retirementReview: { ...input.retirementReview, stagingRunId: "../mst_one" } }, t).retirementReview).toBeUndefined()
  })

  it("keeps accepted and partially removed states honest", () => {
    const input = { ...fixture(), retirementReview: { migrationId: "mig_one", stagingRunId: "mst_one", status: "running" } }
    expect(makeTemporaryStagingItem(input, t).statusLabel).toBe("Staging retirement in progress")
    expect(makeTemporaryStagingItem(input, t).retirementReview?.label).toBe("View retirement")
    const failed = makeTemporaryStagingItem({ ...input, recordedStatus: "needs-attention",
      retirementReview: { ...input.retirementReview, status: "needs-attention" } }, t)
    expect(failed.inventoryNote).toContain("Already removed resources stay removed")
    expect(failed.inventoryNote).not.toContain("has not removed anything")
  })

})
