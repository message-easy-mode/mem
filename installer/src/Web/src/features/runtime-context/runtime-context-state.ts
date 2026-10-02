import type { ControlPlaneRuntimeContext } from "./api/runtime-context.types"

const unsafeOwnershipStates = new Set(["conflict", "unavailable"])

export function runtimeContextNeedsAttention(data: ControlPlaneRuntimeContext) {
  return (
    data.validationState !== "valid" ||
    data.warnings.length > 0 ||
    Boolean(data.dockerOwnership.warningCode) ||
    unsafeOwnershipStates.has(data.dockerOwnership.state) ||
    data.controlPlaneExposure.state === "needs-attention" ||
    data.controlPlaneExposure.state === "unavailable" ||
    Boolean(data.controlPlaneExposure.warningCode) ||
    !data.mutationsAllowed
  )
}
