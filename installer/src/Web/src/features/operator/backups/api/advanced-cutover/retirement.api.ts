import type {
  RuntimeStackBackupProductionRecreateCleanupAssessment,
  RuntimeStackBackupProductionRecreateCleanupResult,
} from "../types/backups.types";
import {
  backupsApiRoutes,
  controlPlaneGet,
  controlPlanePost,
} from "../transport/host-agent";

const RETIREMENT_BASE = backupsApiRoutes.advancedCutover.retirement;

export function assessProductionRecreateCandidateCleanup({
  recreateId,
  candidateId,
}: {
  recreateId: string;
  candidateId?: string | null;
}) {
  const query = new URLSearchParams();
  if (candidateId?.trim()) query.set("candidateId", candidateId.trim());
  const suffix = query.size > 0 ? `?${query.toString()}` : "";

  return controlPlaneGet<RuntimeStackBackupProductionRecreateCleanupAssessment>(
    `${RETIREMENT_BASE}/standard-recreate-runs/${encodeURIComponent(recreateId)}/assessment${suffix}`,
  );
}

export function executeProductionRecreateCandidateCleanup({
  recreateId,
  candidateId,
  retirementMode,
  acknowledgeRetireCandidate,
}: {
  recreateId: string;
  candidateId?: string | null;
  retirementMode: "auto" | "operator-confirmed";
  acknowledgeRetireCandidate: boolean;
}) {
  const query = new URLSearchParams();
  query.set("retirementMode", retirementMode);
  query.set("acknowledgeRetireCandidate", acknowledgeRetireCandidate ? "true" : "false");
  if (candidateId?.trim()) query.set("candidateId", candidateId.trim());

  return controlPlanePost<void, RuntimeStackBackupProductionRecreateCleanupResult>(
    `${RETIREMENT_BASE}/standard-recreate-runs/${encodeURIComponent(recreateId)}/execute?${query.toString()}`,
  );
}
