import type { RuntimeStackDestroyRequest } from "../api/stacks.types"

export const TRACKED_DESTROY_STORAGE_KEY = "mem.stack-destroy.pending-operation"

export type TrackedRuntimeStackDestroy = Readonly<{
  operationId: string
  runtimeStackId: string
  slug: string
  request: RuntimeStackDestroyRequest
}>

export function readTrackedDestroy(): TrackedRuntimeStackDestroy | null {
  try {
    const raw = window.localStorage.getItem(TRACKED_DESTROY_STORAGE_KEY)
    if (!raw) return null

    const value = JSON.parse(raw) as Partial<TrackedRuntimeStackDestroy>
    if (
      typeof value.operationId !== "string" ||
      typeof value.runtimeStackId !== "string" ||
      typeof value.slug !== "string" ||
      typeof value.request !== "object" ||
      value.request === null ||
      typeof value.request.removeContainers !== "boolean" ||
      typeof value.request.removeRoutes !== "boolean" ||
      typeof value.request.removeDatabase !== "boolean" ||
      typeof value.request.removeFiles !== "boolean" ||
      typeof value.request.force !== "boolean" ||
      typeof value.request.idempotencyKey !== "string"
    ) {
      return null
    }

    return value as TrackedRuntimeStackDestroy
  } catch {
    return null
  }
}

export function persistTrackedDestroy(value: TrackedRuntimeStackDestroy | null) {
  try {
    if (value) {
      window.localStorage.setItem(TRACKED_DESTROY_STORAGE_KEY, JSON.stringify(value))
    } else {
      window.localStorage.removeItem(TRACKED_DESTROY_STORAGE_KEY)
    }
  } catch {
    // Current-page tracking remains available if browser storage is unavailable.
  }
}
