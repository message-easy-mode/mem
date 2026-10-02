import { controlPlanePost } from "../transport/host-agent"

/**
 * Runs the existing runtime-stack doctor against a registered restored stack.
 * The normal Restore Workspace discards the raw doctor response and refreshes
 * its safe, projection-backed verification summary instead.
 */
export function runRestoredServerChecks(targetStackSlug: string) {
  return controlPlanePost<void, unknown>(
    `/internal/host-agent/runtime-stacks/${encodeURIComponent(targetStackSlug)}/doctor`,
  )
}
