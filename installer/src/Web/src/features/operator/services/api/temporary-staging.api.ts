import { getJson } from "@/lib/api"

export type TemporaryStagingInventoryItem = {
  resourceGroupId: string
  stagingId: string | null
  ownershipStatus: "matched" | "recorded" | "unresolved"
  owner: {
    kind: "migration" | "restore"
    id: string
    displayName: string
    workspaceHref: string | null
  } | null
  runtimeStatus: "running" | "partially-running" | "stopped" | "network-only" | "not-observed" | "unavailable"
  recordedStatus: "retained" | "retired" | "needs-attention" | "unavailable"
  containerCount: number
  runningContainerCount: number
  networkCount: number
  containerIds: string[]
  reasonCodes: string[]
  // Inventory never grants destructive authority; retirement is a separate workflow.
  canRetire: false
  retirementReview?: { migrationId: string; stagingRunId: string; status: string | null } | null
}

export type TemporaryStagingInventoryResponse = {
  source: "control-plane"
  observedAtUtc: string
  status: "complete" | "partial"
  warningCodes: string[]
  items: TemporaryStagingInventoryItem[]
}

export function getTemporaryStagingInventory() {
  return getJson<TemporaryStagingInventoryResponse>("/api/operator/services/temporary-staging")
}
