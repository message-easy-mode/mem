import type { TranslationKey } from "@/app/i18n/messages"
import type {
  DashboardAction,
  DashboardActionCode,
  DashboardActivityItem,
  DashboardCapabilities,
  DashboardHero,
  DashboardHostSummary,
  DashboardNotice,
  DashboardTone,
} from "../api/dashboard.types"

type Translate = (key: TranslationKey, values?: Readonly<Record<string, string | number>>) => string

type DashboardHostDiskMetrics = {
  usedBytes: number
  totalBytes: number
  usagePercent: number
  scope: string
}

const actionLabelKeys: Readonly<Record<DashboardActionCode, TranslationKey>> = {
  create_chat_server: "dashboard.action.createChatServer",
  manage_domains: "dashboard.action.manageDomains",
  open_services: "dashboard.action.openServices",
  open_backups: "dashboard.action.openBackups",
  open_restores: "dashboard.action.openRestores",
  open_restore_workspace: "dashboard.action.openRestoreWorkspace",
  open_diagnostics: "dashboard.action.openDiagnostics",
}

const actionRoutes: Readonly<Record<DashboardActionCode, string>> = {
  create_chat_server: "/stacks/new",
  manage_domains: "/domains",
  open_services: "/dashboard",
  open_backups: "/backups",
  open_restores: "/restores",
  open_restore_workspace: "/restores",
  open_diagnostics: "/diagnostics",
}

const heroTitleKeys: Readonly<Record<DashboardHero["headlineCode"], TranslationKey>> = {
  platform_ready: "dashboard.hero.platformReady.title",
  platform_needs_attention: "dashboard.hero.platformNeedsAttention.title",
  restore_needs_attention: "dashboard.hero.restoreNeedsAttention.title",
  recovery_needed: "dashboard.hero.recoveryNeeded.title",
  create_first_chat_server: "dashboard.hero.createFirstChatServer.title",
  platform_unavailable: "dashboard.hero.platformUnavailable.title",
}

const heroDescriptionKeys: Readonly<Record<DashboardHero["headlineCode"], TranslationKey>> = {
  platform_ready: "dashboard.hero.platformReady.description",
  platform_needs_attention: "dashboard.hero.platformNeedsAttention.description",
  restore_needs_attention: "dashboard.hero.restoreNeedsAttention.description",
  recovery_needed: "dashboard.hero.recoveryNeeded.description",
  create_first_chat_server: "dashboard.hero.createFirstChatServer.description",
  platform_unavailable: "dashboard.hero.platformUnavailable.description",
}

const noticeTitleKeys: Readonly<Record<DashboardNotice["code"], TranslationKey>> = {
  restore_needs_attention: "dashboard.hero.restoreNeedsAttention.title",
  recovery_needed: "dashboard.hero.recoveryNeeded.title",
  public_access_needs_attention: "dashboard.notice.publicAccess.title",
  public_access_staging_certificate: "dashboard.notice.publicAccessStaging.title",
  platform_needs_attention: "dashboard.hero.platformNeedsAttention.title",
}

const noticeDescriptionKeys: Readonly<Record<DashboardNotice["code"], TranslationKey>> = {
  restore_needs_attention: "dashboard.hero.restoreNeedsAttention.description",
  recovery_needed: "dashboard.hero.recoveryNeeded.description",
  public_access_needs_attention: "dashboard.notice.publicAccess.description",
  public_access_staging_certificate: "dashboard.notice.publicAccessStaging.description",
  platform_needs_attention: "dashboard.hero.platformNeedsAttention.description",
}

const stateLabelKeys: Readonly<Record<string, TranslationKey>> = {
  ready: "dashboard.state.ready",
  attention: "dashboard.state.attention",
  degraded: "dashboard.state.degraded",
  verification_limited: "dashboard.state.verificationLimited",
  unavailable: "dashboard.state.unavailable",
  unknown: "dashboard.state.unknown",
  responsive: "dashboard.state.responsive",
  running: "dashboard.state.running",
  stopped: "dashboard.state.stopped",
  not_deployed: "dashboard.state.notDeployed",
  valid: "dashboard.state.valid",
  renewing: "dashboard.state.renewing",
  expiring: "dashboard.state.expiring",
  expired: "dashboard.state.expired",
  missing: "dashboard.state.missing",
  staging: "dashboard.state.staging",
  not_configured: "dashboard.state.notConfigured",
  passed: "dashboard.state.passed",
  failed: "dashboard.state.failed",
  not_applicable: "dashboard.state.notApplicable",
  no_recovery_point: "dashboard.state.noRecoveryPoint",
  partial_coverage: "dashboard.state.partialCoverage",
  covered: "dashboard.state.covered",
  available: "dashboard.state.available",
  empty: "dashboard.state.empty",
}

const serviceNameKeys: Readonly<Record<string, TranslationKey>> = {
  postgres: "dashboard.service.postgres.name",
  npm_ingress: "dashboard.service.npmIngress.name",
  coturn: "dashboard.service.coturn.name",
}

const serviceDescriptionKeys: Readonly<Record<string, TranslationKey>> = {
  postgres: "dashboard.service.postgres.description",
  npm_ingress: "dashboard.service.npmIngress.description",
  coturn: "dashboard.service.coturn.description",
}

const activityTitleKeys: Readonly<Record<string, TranslationKey>> = {
  chat_server_created: "dashboard.activity.chatServerCreated",
  chat_server_created_failed: "dashboard.activity.chatServerCreateFailed",
  chat_server_removed: "dashboard.activity.chatServerRemoved",
  chat_server_removed_failed: "dashboard.activity.chatServerRemoveFailed",
  backup_completed: "dashboard.activity.backupCompleted",
  backup_completed_failed: "dashboard.activity.backupNeedsAttention",
  chat_server_diagnostics_completed: "dashboard.activity.diagnosticsCompleted",
  chat_server_diagnostics_completed_failed: "dashboard.activity.diagnosticsNeedsAttention",
  restore_recreate_updated: "dashboard.activity.restoreRecreateUpdated",
  restore_recreate_updated_failed: "dashboard.activity.restoreNeedsAttention",
  restore_private_test_updated: "dashboard.activity.restorePrivateTestUpdated",
  restore_private_test_updated_failed: "dashboard.activity.restoreNeedsAttention",
  restore_cleanup_updated: "dashboard.activity.restoreCleanupUpdated",
  restore_cleanup_updated_failed: "dashboard.activity.restoreNeedsAttention",
  runtime_operation_updated: "dashboard.activity.operationUpdated",
  runtime_operation_updated_failed: "dashboard.activity.operationNeedsAttention",
  backup_needs_attention: "dashboard.activity.backupNeedsAttention",
  backup_catalog_warning: "dashboard.activity.backupCatalogWarning",
  backup_imported: "dashboard.activity.backupImported",
  backup_captured: "dashboard.activity.backupCaptured",
  restore_needs_attention: "dashboard.activity.restoreNeedsAttention",
  restore_warning: "dashboard.activity.restoreWarning",
  restore_completed: "dashboard.activity.restoreCompleted",
  restore_updated: "dashboard.activity.restoreUpdated",
  certificate_staging_observed: "dashboard.activity.certificateStagingObserved",
  certificate_updated: "dashboard.activity.certificateUpdated",
}

const activityDetailKeys: Readonly<Record<string, TranslationKey>> = {}

const hostUnavailableDescriptionKeys: Readonly<Record<string, TranslationKey>> = {
  host_observation_failed: "dashboard.host.reason.observationFailed",
}

const hostStorageUnavailableDescriptionKeys: Readonly<Record<string, TranslationKey>> = {
  containerized_host_filesystem_unavailable: "dashboard.host.storageReason.containerized",
  mem_data_filesystem_unavailable: "dashboard.host.storageReason.memDataUnavailable",
  host_filesystem_unavailable: "dashboard.host.storageReason.unavailable",
}

const hostStorageLabelKeys: Readonly<Record<string, TranslationKey>> = {
  host_root: "dashboard.host.storage.hostRoot",
  mem_data: "dashboard.host.storage.memData",
}

export function getDashboardActionRoute(
  action: DashboardAction,
  capabilities: DashboardCapabilities,
): string | null {
  if (action.code === "create_chat_server" && !capabilities.canOperate) {
    return null
  }

  if (action.code === "manage_domains" && !capabilities.canManagePlatform) {
    return null
  }

  if (action.code === "open_services") {
    return null
  }

  if (action.code === "open_restore_workspace") {
    return action.restoreSessionId
      ? `/restores/${encodeURIComponent(action.restoreSessionId)}`
      : "/restores"
  }

  return actionRoutes[action.code]
}

export function getDashboardActionLabel(action: DashboardAction, t: Translate): string {
  return t(actionLabelKeys[action.code])
}

export function getDashboardHeroTitle(hero: DashboardHero, t: Translate): string {
  return t(heroTitleKeys[hero.headlineCode])
}

export function getDashboardHeroDescription(hero: DashboardHero, t: Translate): string {
  return t(heroDescriptionKeys[hero.headlineCode])
}

export function getDashboardNoticeTitle(notice: DashboardNotice, t: Translate): string {
  return t(noticeTitleKeys[notice.code] ?? "dashboard.hero.platformNeedsAttention.title")
}

export function getDashboardNoticeDescription(notice: DashboardNotice, t: Translate): string {
  return t(noticeDescriptionKeys[notice.code] ?? "dashboard.hero.platformNeedsAttention.description")
}

export function getDashboardStateLabel(state: string, t: Translate): string {
  return t(stateLabelKeys[state] ?? "dashboard.state.unknown")
}

export function getDashboardServiceName(serviceKey: string, t: Translate): string {
  return t(serviceNameKeys[serviceKey] ?? "dashboard.service.unknown.name")
}

export function getDashboardServiceDescription(serviceKey: string, t: Translate): string {
  return t(serviceDescriptionKeys[serviceKey] ?? "dashboard.service.unknown.description")
}

export function getDashboardActivityTitle(item: DashboardActivityItem, t: Translate): string {
  return t(activityTitleKeys[item.titleCode] ?? "dashboard.activity.unknown")
}

export function getDashboardActivityDetail(item: DashboardActivityItem, t: Translate): string | null {
  if (!item.detailCode) {
    return null
  }

  const key = activityDetailKeys[item.detailCode]
  return key ? t(key) : null
}

export function getDashboardActivityRoute(item: DashboardActivityItem): string | null {
  if (hasRouteSegment(item.restoreSessionId)) {
    return `/restores/${encodeURIComponent(item.restoreSessionId)}`
  }

  if (hasRouteSegment(item.stackSlug)) {
    return `/stacks/${encodeURIComponent(item.stackSlug)}`
  }

  return null
}

export function getDashboardActivityTone(
  severity: DashboardActivityItem["severity"],
): DashboardTone {
  switch (severity) {
    case "success":
      return "good"
    case "warning":
      return "warning"
    case "danger":
      return "danger"
    default:
      return "neutral"
  }
}

export function getDashboardHostDiskMetrics(
  host: DashboardHostSummary,
): DashboardHostDiskMetrics | null {
  if (host.state !== "available" || !isUsableByteUsage(host.disk)) {
    return null
  }

  return {
    usedBytes: host.disk.usedBytes,
    totalBytes: host.disk.totalBytes,
    usagePercent: toUsagePercent(host.disk.usedBytes, host.disk.totalBytes),
    scope: host.disk.scope,
  }
}

export function getDashboardHostUnavailableDescription(
  host: DashboardHostSummary,
  t: Translate,
): string {
  return t(
    hostUnavailableDescriptionKeys[host.unavailableReasonCode ?? ""] ??
      "dashboard.host.reason.unavailable",
  )
}

export function getDashboardHostStorageUnavailableDescription(
  host: DashboardHostSummary,
  t: Translate,
): string {
  return t(
    hostStorageUnavailableDescriptionKeys[host.storageUnavailableReasonCode ?? ""] ??
      "dashboard.host.storageReason.unavailable",
  )
}

export function getDashboardHostStorageLabel(scope: string, t: Translate): string {
  return t(hostStorageLabelKeys[scope] ?? "dashboard.host.storage.unknown")
}

export function getDashboardTone(state: string): DashboardTone {
  switch (state) {
    case "ready":
    case "responsive":
    case "running":
    case "valid":
    case "renewing":
    case "passed":
    case "covered":
    case "available":
      return "good"
    case "attention":
    case "degraded":
    case "expiring":
    case "staging":
    case "missing":
    case "not_configured":
    case "no_recovery_point":
    case "partial_coverage":
    case "stopped":
      return "warning"
    case "unavailable":
    case "expired":
    case "failed":
      return "danger"
    default:
      return "neutral"
  }
}

export function getDashboardSeverityTone(severity: DashboardNotice["severity"]): DashboardTone {
  switch (severity) {
    case "danger":
      return "danger"
    case "warning":
      return "warning"
    default:
      return "neutral"
  }
}

function hasRouteSegment(value: string | null): value is string {
  return typeof value === "string" && value.trim().length > 0
}

function isUsableByteUsage<T extends { usedBytes: number; totalBytes: number } | null>(
  value: T,
): value is Exclude<T, null> {
  return (
    value !== null &&
    Number.isFinite(value.usedBytes) &&
    Number.isFinite(value.totalBytes) &&
    value.usedBytes >= 0 &&
    value.totalBytes > 0 &&
    value.usedBytes <= value.totalBytes
  )
}

function toUsagePercent(usedBytes: number, totalBytes: number): number {
  return Math.round((usedBytes / totalBytes) * 100)
}
