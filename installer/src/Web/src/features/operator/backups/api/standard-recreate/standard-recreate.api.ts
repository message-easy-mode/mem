import type {
  RuntimeStackBackupProductionRecreatePreflightRequest,
  RuntimeStackBackupProductionRecreatePreflightResponse,
} from "../types/backups.types";
import { backupsApiRoutes, controlPlanePost } from "../transport/host-agent";

/**
 * Read-only target assessment for the canonical Restore Workspace. The HostAgent
 * does not reserve targets here; execute repeats checks and claims targets
 * atomically through the workspace action.
 */
export function preflightProductionRecreate(
  request: RuntimeStackBackupProductionRecreatePreflightRequest,
) {
  const query = new URLSearchParams();
  query.set("targetStackSlug", request.targetStackSlug);
  query.set("elementHost", request.elementHost);

  if (request.matrixHost?.trim()) {
    query.set("matrixHost", request.matrixHost.trim());
  }

  if (request.requestedDomainId?.trim()) {
    query.set("requestedDomainId", request.requestedDomainId.trim());
  }

  return controlPlanePost<
    void,
    RuntimeStackBackupProductionRecreatePreflightResponse
  >(
    `${backupsApiRoutes.restores}/${encodeURIComponent(request.restoreSessionId)}/standard-recreate/preflight?${query.toString()}`,
  );
}
