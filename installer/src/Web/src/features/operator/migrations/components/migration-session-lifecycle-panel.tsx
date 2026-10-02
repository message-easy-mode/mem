import { formatMigrationDateTime } from "./migration-time"
import { Archive, ShieldCheck } from "lucide-react"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { MigrationSessionLifecycleInspection } from "@/features/operator/migrations/api/migration-sessions"
import { MigrationSessionLifecycleButtons } from "@/features/operator/migrations/components/migration-session-lifecycle-actions"

export function MigrationSessionLifecyclePanel({
  lifecycle,
}: {
  lifecycle: MigrationSessionLifecycleInspection
}) {
  const { intlLocale, t } = useI18n()
  const status = lifecycle.archived ? "archived" : lifecycle.lifecycleStatus

  return (
    <Card>
      <CardHeader className="gap-3">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Archive className="h-5 w-5 text-sky-400" aria-hidden="true" />
              {t("migrationWorkspace.lifecycle.panelTitle")}
            </CardTitle>
            <p className="mt-2 max-w-3xl text-sm text-muted-foreground">
              {t("migrationWorkspace.lifecycle.panelDescription")}
            </p>
          </div>
          <Badge variant="outline">{valueLabel("lifecycle", status, t)}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.sessionLifecycle")}
            value={valueLabel("lifecycle", status, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.currentOperation")}
            value={valueLabel("operation", lifecycle.currentOperation, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.package")}
            value={valueLabel("package", lifecycle.packageState, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.candidateArtifact")}
            value={valueLabel("resource", lifecycle.candidateArtifactState, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.privateStaging")}
            value={valueLabel("resource", lifecycle.privateStagingRuntimeState, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.productionRuntime")}
            value={valueLabel("production", lifecycle.productionRuntimeState, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.publicRoutes")}
            value={valueLabel("routes", lifecycle.publicRoutesState, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.sourceState")}
            value={valueLabel("source", lifecycle.sourceState, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.updatedVersion")}
            value={String(lifecycle.stateVersion)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.closed")}
            value={formatDate(lifecycle.closedAtUtc, intlLocale, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.archived")}
            value={formatDate(lifecycle.archivedAtUtc, intlLocale, t)}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.permanentDeletion")}
            value={lifecycle.capabilities.canDelete
              ? t("migrationWorkspace.lifecycle.allowed")
              : t("migrationWorkspace.lifecycle.blocked")}
          />
          <LifecycleFact
            label={t("migrationWorkspace.lifecycle.deletionReason")}
            value={lifecycle.capabilities.canDelete
              ? t("migrationWorkspace.lifecycle.allowed")
              : localizedProblem(
                  lifecycle.capabilities.deleteBlockedCode,
                  lifecycle.capabilities.deleteBlockedReason ??
                    t("migrationWorkspace.lifecycle.blocked"),
                  t,
                )}
          />
        </div>

        {!lifecycle.capabilities.canCancel && lifecycle.capabilities.cancelBlockedReason ? (
          <Alert>
            <ShieldCheck className="h-4 w-4" aria-hidden="true" />
            <AlertTitle>{t("migrationWorkspace.lifecycle.cancelUnavailableTitle")}</AlertTitle>
            <AlertDescription>
              {localizedProblem(
                lifecycle.capabilities.cancelBlockedCode,
                lifecycle.capabilities.cancelBlockedReason,
                t,
              )}
            </AlertDescription>
          </Alert>
        ) : null}

        <MigrationSessionLifecycleButtons lifecycle={lifecycle} />
      </CardContent>
    </Card>
  )
}

function LifecycleFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-sm font-medium">{value}</div>
    </div>
  )
}

type Translate = I18nContextValue["t"]

function valueLabel(group: string, value: string, t: Translate) {
  const key = lifecycleValueKey(group, value)
  return key ? t(key) : value
}

function localizedProblem(
  code: string | null,
  fallback: string,
  t: Translate,
) {
  const key = lifecycleProblemKey(code)
  return key ? t(key) : fallback
}

function lifecycleValueKey(group: string, value: string): TranslationKey | null {
  switch (`${group}:${value}`) {
    case "lifecycle:active": return "migrationWorkspace.lifecycle.value.lifecycle.active"
    case "lifecycle:completed": return "migrationWorkspace.lifecycle.value.lifecycle.completed"
    case "lifecycle:closed": return "migrationWorkspace.lifecycle.value.lifecycle.closed"
    case "lifecycle:cancelled": return "migrationWorkspace.lifecycle.value.lifecycle.cancelled"
    case "lifecycle:archived": return "migrationWorkspace.lifecycle.value.lifecycle.archived"
    case "operation:none": return "migrationWorkspace.lifecycle.value.operation.none"
    case "operation:conversion": return "migrationWorkspace.lifecycle.value.operation.conversion"
    case "operation:staging": return "migrationWorkspace.lifecycle.value.operation.staging"
    case "operation:materialisation": return "migrationWorkspace.lifecycle.value.operation.materialisation"
    case "operation:cutover": return "migrationWorkspace.lifecycle.value.operation.cutover"
    case "operation:rollback": return "migrationWorkspace.lifecycle.value.operation.rollback"
    case "package:none": return "migrationWorkspace.lifecycle.value.package.none"
    case "package:awaiting": return "migrationWorkspace.lifecycle.value.package.awaiting"
    case "package:expired": return "migrationWorkspace.lifecycle.value.package.expired"
    case "package:retained": return "migrationWorkspace.lifecycle.value.package.retained"
    case "package:retired": return "migrationWorkspace.lifecycle.value.package.retired"
    case "package:cancelled": return "migrationWorkspace.lifecycle.value.package.cancelled"
    case "resource:none": return "migrationWorkspace.lifecycle.value.resource.none"
    case "resource:retained": return "migrationWorkspace.lifecycle.value.resource.retained"
    case "resource:retired": return "migrationWorkspace.lifecycle.value.resource.retired"
    case "resource:destroyed": return "migrationWorkspace.lifecycle.value.resource.destroyed"
    case "production:none": return "migrationWorkspace.lifecycle.value.production.none"
    case "production:planned": return "migrationWorkspace.lifecycle.value.production.planned"
    case "production:private": return "migrationWorkspace.lifecycle.value.production.private"
    case "production:public": return "migrationWorkspace.lifecycle.value.production.public"
    case "production:accepted": return "migrationWorkspace.lifecycle.value.production.accepted"
    case "routes:none": return "migrationWorkspace.lifecycle.value.routes.none"
    case "routes:active": return "migrationWorkspace.lifecycle.value.routes.active"
    case "routes:restored": return "migrationWorkspace.lifecycle.value.routes.restored"
    case "routes:unknown": return "migrationWorkspace.lifecycle.value.routes.unknown"
    case "source:running": return "migrationWorkspace.lifecycle.value.source.running"
    case "source:frozen": return "migrationWorkspace.lifecycle.value.source.frozen"
    case "source:external": return "migrationWorkspace.lifecycle.value.source.external"
    default: return null
  }
}

function lifecycleProblemKey(code: string | null): TranslationKey | null {
  switch (code) {
    case "migration_session_state_stale":
      return "migrationWorkspace.lifecycle.problem.migration_session_state_stale"
    case "migration_session_archived":
      return "migrationWorkspace.lifecycle.problem.migration_session_archived"
    case "migration_session_completed":
      return "migrationWorkspace.lifecycle.problem.migration_session_completed"
    case "migration_session_catalog_relationship":
      return "migrationWorkspace.lifecycle.problem.migration_session_catalog_relationship"
    case "migration_session_production_owned":
      return "migrationWorkspace.lifecycle.problem.migration_session_production_owned"
    case "migration_session_staging_retained":
      return "migrationWorkspace.lifecycle.problem.migration_session_staging_retained"
    case "migration_session_conversion_exists":
      return "migrationWorkspace.lifecycle.problem.migration_session_conversion_exists"
    case "migration_session_conversion_active":
      return "migrationWorkspace.lifecycle.problem.migration_session_conversion_active"
    case "migration_session_lifecycle_terminal":
      return "migrationWorkspace.lifecycle.problem.migration_session_lifecycle_terminal"
    case "migration_session_package_state_not_eligible":
      return "migrationWorkspace.lifecycle.problem.migration_session_package_state_not_eligible"
    case "migration_session_active":
      return "migrationWorkspace.lifecycle.problem.migration_session_active"
    case "migration_session_staging_exists":
      return "migrationWorkspace.lifecycle.problem.migration_session_staging_exists"
    case "migration_session_package_authority_active":
      return "migrationWorkspace.lifecycle.problem.migration_session_package_authority_active"
    case "migration_session_package_evidence_inconsistent":
      return "migrationWorkspace.lifecycle.problem.migration_session_package_evidence_inconsistent"
    case "migration_session_two_server_qualification":
      return "migrationWorkspace.lifecycle.problem.migration_session_two_server_qualification"
    case "migration_session_legacy_retention":
      return "migrationWorkspace.lifecycle.problem.migration_session_legacy_retention"
    case "migration_session_lifecycle_not_disposable":
      return "migrationWorkspace.lifecycle.problem.migration_session_lifecycle_not_disposable"
    case "migration_session_target_state_unverifiable":
      return "migrationWorkspace.lifecycle.problem.migration_session_target_state_unverifiable"
    case "migration_session_unexpected_package_material":
      return "migrationWorkspace.lifecycle.problem.migration_session_unexpected_package_material"
    case "migration_session_plaintext_package_present":
      return "migrationWorkspace.lifecycle.problem.migration_session_plaintext_package_present"
    case "migration_session_delete_confirmation_mismatch":
      return "migrationWorkspace.lifecycle.problem.migration_session_delete_confirmation_mismatch"
    case "migration_session_target_cleanup_failed":
      return "migrationWorkspace.lifecycle.problem.migration_session_target_cleanup_failed"
    case "migration_session_delete_failed":
      return "migrationWorkspace.lifecycle.problem.migration_session_delete_failed"
    case "migration_session_delete_not_allowed":
      return "migrationWorkspace.lifecycle.problem.migration_session_delete_not_allowed"
    default:
      return null
  }
}

function formatDate(
  value: string | null,
  locale: string,
  t: Translate,
) {
  return formatMigrationDateTime(value, locale, t("common.notRecorded"))
}
