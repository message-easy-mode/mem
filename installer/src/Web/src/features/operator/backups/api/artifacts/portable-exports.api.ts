import type {
  RuntimeStackBackupExportResult,
} from "../types/backups.types";
import {
  backupsApiRoutes,
  controlPlaneDownload,
  controlPlanePost,
} from "../transport/host-agent";

const PORTABLE_EXPORTS_BASE = backupsApiRoutes.artifacts.portableExports;

export function exportBackup(stackSlug: string, backupId: string) {
  return controlPlanePost<void, RuntimeStackBackupExportResult>(
    `${PORTABLE_EXPORTS_BASE}/from-local-backups/stacks/${encodeURIComponent(stackSlug)}/${encodeURIComponent(backupId)}`,
  );
}

export function downloadBackupExport(exportId: string) {
  return controlPlaneDownload(
    `${PORTABLE_EXPORTS_BASE}/${encodeURIComponent(exportId)}/download`,
  );
}
