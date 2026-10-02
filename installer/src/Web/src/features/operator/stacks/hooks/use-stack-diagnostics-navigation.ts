import { useCallback } from "react"
import { useNavigate } from "react-router-dom"

export type StackDiagnosticsNavigationState = {
  runDoctor?: boolean
}

/**
 * Sends a stack-scoped Doctor request to the Diagnostics workspace rather than
 * leaving the operator on the page that launched it. The Diagnostics route
 * consumes the state and starts the existing Doctor mutation after it mounts.
 */
export function useRunDoctorInStackDiagnostics(slugOrId: string | undefined) {
  const navigate = useNavigate()

  return useCallback(() => {
    if (!slugOrId) return

    navigate(`/stacks/${encodeURIComponent(slugOrId)}/diagnostics`, {
      state: { runDoctor: true } as StackDiagnosticsNavigationState,
    })
  }, [navigate, slugOrId])
}

export function isStackDiagnosticsDoctorRequest(
  value: unknown,
): value is StackDiagnosticsNavigationState {
  return (
    typeof value === "object" &&
    value !== null &&
    "runDoctor" in value &&
    (value as StackDiagnosticsNavigationState).runDoctor === true
  )
}
