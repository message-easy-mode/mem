import type { I18nContextValue } from "@/app/i18n/i18n-context"
import type { ManagedServiceListItem } from "../api/runtime-ui.types"
import type { TemporaryStagingInventoryItem } from "../api/temporary-staging.api"

type Translate = I18nContextValue["t"]

/** A navigation link is accepted only for the exact server-supplied owner. */
export function temporaryStagingWorkspaceHref(item: TemporaryStagingInventoryItem): string | null {
  if (item.ownershipStatus !== "matched" && item.ownershipStatus !== "recorded") return null
  const owner = item.owner
  if (!owner || !/^[A-Za-z0-9_-]{1,160}$/.test(owner.id)) return null
  if (owner.kind !== "migration" && owner.kind !== "restore") return null
  const expected = `/${owner.kind === "migration" ? "migrations" : "restores"}/${encodeURIComponent(owner.id)}`
  return owner.workspaceHref === expected ? expected : null
}

export function makeTemporaryStagingItem(
  item: TemporaryStagingInventoryItem,
  t: Translate,
  stale = false,
): ManagedServiceListItem {
  const owner = item.ownershipStatus === "unresolved" ? null : item.owner
  const href = stale ? null : temporaryStagingWorkspaceHref(item)
  const retirement = href && owner?.kind === "migration" && item.retirementReview?.migrationId === owner.id &&
    /^[A-Za-z0-9_-]{1,160}$/.test(item.retirementReview.stagingRunId) ? item.retirementReview : null
  const ownerDescription = owner
    ? t(owner.kind === "migration" ? "services.staging.owner.migration" : "services.staging.owner.restore", {
        name: owner.displayName,
      })
    : t("services.staging.owner.unresolved")
  const counts = t("services.staging.resources", {
    containers: item.containerCount,
    networks: item.networkCount,
  })
  const runtimeStatus = stale ? "unavailable" : item.runtimeStatus
  const state = stagingRuntimeLabel(runtimeStatus, t)
  const needsAttention = item.recordedStatus === "needs-attention" || item.ownershipStatus === "unresolved"

  return {
    serviceName: `restore-staging-${item.resourceGroupId}`,
    displayName: t("services.item.restoreStaging.name"),
    category: "stack",
    supported: true,
    exists: item.containerCount + item.networkCount > 0,
    running: !stale && item.runningContainerCount > 0,
    state,
    statusLabel: retirement && ["queued", "running"].includes(retirement.status ?? "") ? t("migrationRetirement.inProgress")
      : needsAttention && !stale ? t("services.staging.needsAttention") : state,
    statusTone: stale || runtimeStatus === "unavailable" || (retirement && ["queued", "running"].includes(retirement.status ?? "")) ? "neutral"
      : needsAttention ? "warning" : runtimeStatus === "running" ? "positive" : "neutral",
    image: null,
    containerName: null,
    desiredPorts: [],
    actualPorts: [],
    hostPaths: [],
    warnings: [],
    // The row stays neutral: an explicit action says which workflow will open.
    serviceHref: null,
    retirementReview: retirement ? {
      ...retirement,
      label: t(retirement.status ? "migrationRetirement.view" : "migrationRetirement.review"),
    } : undefined,
    workspaceLink: href && owner ? {
      href,
      label: t(owner.kind === "migration" ? "services.staging.openMigration" : "services.staging.openRestore"),
    } : undefined,
    description: `${ownerDescription} · ${counts}`,
    inventoryNote: stale ? t("services.staging.stale")
      : retirement?.status === "needs-attention" ? t("migrationRetirement.blocker.incomplete")
      : item.recordedStatus === "needs-attention" ? t("services.staging.retirementNeedsAttention")
      : item.ownershipStatus === "unresolved" ? t("services.staging.unresolvedNote")
      : item.runtimeStatus === "not-observed" ? t("services.staging.recordedOnly")
      : retirement ? t(retirement.status === "queued" || retirement.status === "running"
          ? "migrationRetirement.inProgress" : "migrationRetirement.reviewHint")
      : t("services.staging.readOnly"),
    openUiHref: null,
    urls: [],
    notes: [],
    serviceKind: "unknown",
  }
}

function stagingRuntimeLabel(status: string, t: Translate): string {
  switch (status) {
    case "running": return t("services.status.running")
    case "partially-running": return t("services.status.partiallyRunning")
    case "stopped": return t("services.status.stopped")
    case "network-only": return t("services.staging.networkOnly")
    case "not-observed": return t("services.staging.notObserved")
    default: return t("services.staging.unavailable")
  }
}
