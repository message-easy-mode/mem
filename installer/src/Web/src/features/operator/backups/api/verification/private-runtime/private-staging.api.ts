import type {
  RuntimeStackBackupRestoreStagingResult,
} from "../../types/backups.types";
import {
  backupsApiRoutes,
  controlPlanePost,
} from "../../transport/host-agent";

const PRIVATE_STAGING_BASE = backupsApiRoutes.verification.privateStaging;

/**
 * Private staging is created by the canonical Restore Workspace. This endpoint
 * remains only for explicit teardown of an already-recorded private runtime.
 */
export function destroyRestoreStagingRun(
  stagingId: string,
): Promise<RuntimeStackBackupRestoreStagingResult> {
  return controlPlanePost<void, RuntimeStackBackupRestoreStagingResult>(
    `${PRIVATE_STAGING_BASE}/${encodeURIComponent(stagingId)}/destroy`,
  );
}
