import type { TranslationKey } from "@/app/i18n/messages"
import type { HostCheckStatus } from "@/features/setup/host-checks/api/host-checks.types"
import type {
  InstallationStatus,
  WorkflowStepStatus,
} from "@/features/setup/install-run/api/install.types"

export function hostCheckStatusTranslationKey(
  status: HostCheckStatus,
): TranslationKey {
  switch (status) {
    case "Pass":
      return "setup.checks.status.pass"
    case "Warning":
      return "setup.checks.status.warning"
    case "Fail":
      return "setup.checks.status.fail"
    case "Skipped":
      return "setup.checks.status.skipped"
    case "Unavailable":
      return "setup.checks.status.unavailable"
    case "Unknown":
      return "setup.checks.status.unknown"
  }
}

export function workflowStepStatusTranslationKey(
  status: WorkflowStepStatus,
): TranslationKey {
  switch (status) {
    case "Pending":
      return "setup.activity.status.pending"
    case "Running":
      return "setup.activity.status.running"
    case "Succeeded":
      return "setup.activity.status.succeeded"
    case "WaitingForUser":
      return "setup.activity.status.waiting"
    case "Failed":
      return "setup.activity.status.failed"
  }
}

export function installationStatusTranslationKey(
  status: InstallationStatus,
): TranslationKey {
  switch (status) {
    case "Draft":
      return "setup.activity.installStatus.draft"
    case "Ready":
      return "setup.activity.installStatus.ready"
    case "Running":
      return "setup.activity.installStatus.running"
    case "WaitingForUser":
      return "setup.activity.installStatus.waiting"
    case "Succeeded":
      return "setup.activity.installStatus.succeeded"
    case "Failed":
      return "setup.activity.installStatus.failed"
    default:
      return "setup.activity.installStatus.unknown"
  }
}
