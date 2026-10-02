import type { TranslationKey } from "@/app/i18n/messages"

const blockers: Record<string, TranslationKey> = {
  "ownership-unproven": "migrationRetirement.blocker.ownership",
  "ownership-changed": "migrationRetirement.blocker.ownership",
  "history-unavailable": "migrationRetirement.blocker.ownership",
  "workflow-busy": "migrationRetirement.blocker.busy",
  "other-retirement-active": "migrationRetirement.blocker.busy",
  "retirement-in-progress": "migrationRetirement.blocker.busy",
  "private-target-recovery-required": "migrationRetirement.blocker.privateTarget",
  "completed-recovery-required": "migrationRetirement.blocker.completed",
  "public-resource": "migrationRetirement.blocker.public",
  "production-resource": "migrationRetirement.blocker.public",
  "public-use-unproven": "migrationRetirement.blocker.public",
  "workspace-in-use": "migrationRetirement.blocker.workspace",
  "workspace-unsafe": "migrationRetirement.blocker.workspace",
  "review-stale": "migrationRetirement.blocker.stale",
  "confirmation-required": "migrationRetirement.blocker.stale",
  "reviewed-retry-required": "migrationRetirement.blocker.stale",
  "cleanup-incomplete": "migrationRetirement.blocker.incomplete",
  "operation-timeout": "migrationRetirement.blocker.incomplete",
  "session-closed": "migrationRetirement.blocker.closed",
  "control-plane-ownership-unproven": "migrationRetirement.blocker.controller",
}
const steps: Record<string, TranslationKey> = {
  "queued": "migrationRetirement.step.queued",
  "revalidate": "migrationRetirement.step.revalidate",
  "remove-element": "migrationRetirement.step.element",
  "remove-synapse": "migrationRetirement.step.synapse",
  "remove-postgres": "migrationRetirement.step.postgres",
  "remove-network": "migrationRetirement.step.network",
  "remove-workspace": "migrationRetirement.step.workspace",
  "record-evidence": "migrationRetirement.step.evidence",
  "interrupted": "migrationRetirement.step.interrupted",
  "needs-attention": "migrationRetirement.needsAttention",
  "retired": "migrationRetirement.completeDetail",
}
export const retirementBlockerKey = (code: string | null | undefined): TranslationKey =>
  blockers[code ?? ""] ?? "migrationRetirement.unavailable"
export const retirementStepKey = (code: string): TranslationKey =>
  steps[code] ?? "migrationRetirement.checking"
