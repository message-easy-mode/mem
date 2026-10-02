import { getJson } from "@/lib/api"

import type { ControlPlaneRuntimeContext } from "./runtime-context.types"

export function getControlPlaneRuntimeContext(): Promise<ControlPlaneRuntimeContext> {
  return getJson<ControlPlaneRuntimeContext>("/api/operator/runtime-context")
}
