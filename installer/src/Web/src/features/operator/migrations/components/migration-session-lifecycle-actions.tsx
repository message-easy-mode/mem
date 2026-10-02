import { useState, type FormEvent } from "react"
import { createPortal } from "react-dom"
import {
  Archive,
  ArchiveRestore,
  MoreHorizontal,
  ShieldAlert,
  Trash2,
  XCircle,
} from "lucide-react"
import { Link, useNavigate } from "react-router-dom"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Select } from "@/components/ui/select"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  isMigrationSessionLifecycleStepUpRequired,
  type MigrationSessionCancelRequest,
  type MigrationSessionDeleteRequest,
  type MigrationSessionInventoryRow,
  type MigrationSessionLifecycleInspection,
  type MigrationSessionLifecycleProblemError,
} from "@/features/operator/migrations/api/migration-sessions"
import { useMigrationSessionLifecycleMutations } from "@/features/operator/migrations/hooks/use-migration-session-lifecycle"

type LifecycleAction = "archive" | "unarchive" | "cancel" | "delete"

type LifecycleActionModel = {
  migrationId: string
  stateVersion: number
  canArchive: boolean
  canUnarchive: boolean
  canCancel: boolean
  canDelete: boolean
  sourceUnaffectedNotice: string
}

export function MigrationSessionRowActions({
  session,
}: {
  session: MigrationSessionInventoryRow
}) {
  const { t } = useI18n()
  const [action, setAction] = useState<LifecycleAction | null>(null)
  const label = session.primaryAction.kind === "continue"
    ? t("migrationWorkspace.inventory.action.continue")
    : session.primaryAction.kind === "review"
      ? t("migrationWorkspace.inventory.action.review")
      : t("migrationWorkspace.inventory.action.view")
  const hasOverflowAction =
    session.capabilities.canArchive ||
    session.capabilities.canUnarchive ||
    session.capabilities.canCancel ||
    session.capabilities.canDelete
  const model: LifecycleActionModel = {
    migrationId: session.migrationId,
    stateVersion: session.stateVersion,
    canArchive: session.capabilities.canArchive,
    canUnarchive: session.capabilities.canUnarchive,
    canCancel: session.capabilities.canCancel,
    canDelete: session.capabilities.canDelete,
    sourceUnaffectedNotice: t("migrationWorkspace.lifecycle.sourceUnaffected"),
  }

  return (
    <>
      <div className="flex items-center justify-end gap-2">
        <Button
          size="sm"
          variant={session.primaryAction.kind === "continue" ? "default" : "outline"}
          asChild
        >
          <Link to={`/migrations/${encodeURIComponent(session.migrationId)}`}>{label}</Link>
        </Button>

        {hasOverflowAction ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button
                type="button"
                size="icon-sm"
                variant="outline"
                aria-label={t("migrationWorkspace.lifecycle.moreActions")}
              >
                <MoreHorizontal aria-hidden="true" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem asChild>
                <Link to={`/migrations/${encodeURIComponent(session.migrationId)}`}>
                  {t("migrationWorkspace.lifecycle.view")}
                </Link>
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              {session.capabilities.canCancel ? (
                <DropdownMenuItem
                  variant="destructive"
                  onSelect={() => setAction("cancel")}
                >
                  <XCircle aria-hidden="true" />
                  {t("migrationWorkspace.lifecycle.cancelAction")}
                </DropdownMenuItem>
              ) : null}
              {session.capabilities.canDelete ? (
                <DropdownMenuItem
                  variant="destructive"
                  onSelect={() => setAction("delete")}
                >
                  <Trash2 aria-hidden="true" />
                  {t("migrationWorkspace.lifecycle.deleteAction")}
                </DropdownMenuItem>
              ) : null}
              {session.capabilities.canArchive ? (
                <DropdownMenuItem onSelect={() => setAction("archive")}>
                  <Archive aria-hidden="true" />
                  {t("migrationWorkspace.lifecycle.archiveAction")}
                </DropdownMenuItem>
              ) : null}
              {session.capabilities.canUnarchive ? (
                <DropdownMenuItem onSelect={() => setAction("unarchive")}>
                  <ArchiveRestore aria-hidden="true" />
                  {t("migrationWorkspace.lifecycle.unarchiveAction")}
                </DropdownMenuItem>
              ) : null}
            </DropdownMenuContent>
          </DropdownMenu>
        ) : null}
      </div>

      <MigrationSessionLifecycleActionDialog
        model={model}
        action={action}
        onActionChange={setAction}
      />
    </>
  )
}

export function MigrationSessionLifecycleButtons({
  lifecycle,
}: {
  lifecycle: MigrationSessionLifecycleInspection
}) {
  const { t } = useI18n()
  const [action, setAction] = useState<LifecycleAction | null>(null)
  const model: LifecycleActionModel = {
    migrationId: lifecycle.migrationId,
    stateVersion: lifecycle.stateVersion,
    canArchive: lifecycle.capabilities.canArchive,
    canUnarchive: lifecycle.capabilities.canUnarchive,
    canCancel: lifecycle.capabilities.canCancel,
    canDelete: lifecycle.capabilities.canDelete,
    sourceUnaffectedNotice: lifecycle.sourceUnaffectedNotice,
  }

  return (
    <>
      <div className="flex flex-wrap gap-2">
        {lifecycle.capabilities.canCancel ? (
          <Button
            type="button"
            variant="destructive"
            onClick={() => setAction("cancel")}
          >
            <XCircle className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.lifecycle.cancelAction")}
          </Button>
        ) : null}
        {lifecycle.capabilities.canDelete ? (
          <Button
            type="button"
            variant="destructive"
            onClick={() => setAction("delete")}
          >
            <Trash2 className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.lifecycle.deleteAction")}
          </Button>
        ) : null}
        {lifecycle.capabilities.canArchive ? (
          <Button
            type="button"
            variant="outline"
            onClick={() => setAction("archive")}
          >
            <Archive className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.lifecycle.archiveAction")}
          </Button>
        ) : null}
        {lifecycle.capabilities.canUnarchive ? (
          <Button
            type="button"
            variant="outline"
            onClick={() => setAction("unarchive")}
          >
            <ArchiveRestore className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.lifecycle.unarchiveAction")}
          </Button>
        ) : null}
      </div>

      <MigrationSessionLifecycleActionDialog
        model={model}
        action={action}
        onActionChange={setAction}
      />
    </>
  )
}

function MigrationSessionLifecycleActionDialog({
  model,
  action,
  onActionChange,
}: {
  model: LifecycleActionModel
  action: LifecycleAction | null
  onActionChange: (action: LifecycleAction | null) => void
}) {
  const { t } = useI18n()
  const navigate = useNavigate()
  const mutations = useMigrationSessionLifecycleMutations(model.migrationId)
  const [acknowledged, setAcknowledged] = useState(false)
  const [retention, setRetention] = useState("")
  const [confirmationMigrationId, setConfirmationMigrationId] = useState("")
  const [error, setError] = useState<string | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingStepUp, setPendingStepUp] = useState<
    | { action: "cancel"; request: MigrationSessionCancelRequest }
    | { action: "delete"; request: MigrationSessionDeleteRequest }
    | null
  >(null)

  if (!action) {
    return null
  }

  const close = (force = false) => {
    if (mutations.isPending && !force) return
    setAcknowledged(false)
    setRetention("")
    setConfirmationMigrationId("")
    setError(null)
    setPendingStepUp(null)
    onActionChange(null)
  }

  async function runCancel(request: MigrationSessionCancelRequest) {
    try {
      await mutations.cancel.mutateAsync(request)
      close(true)
    } catch (value) {
      if (isMigrationSessionLifecycleStepUpRequired(value)) {
        setPendingStepUp({ action: "cancel", request })
        setStepUpOpen(true)
        return
      }

      setError(problemMessage(value, t))
    }
  }

  async function runDelete(request: MigrationSessionDeleteRequest) {
    try {
      await mutations.delete.mutateAsync(request)
      close(true)
      navigate("/migrations", { replace: true })
    } catch (value) {
      if (isMigrationSessionLifecycleStepUpRequired(value)) {
        setPendingStepUp({ action: "delete", request })
        setStepUpOpen(true)
        return
      }

      setError(problemMessage(value, t))
    }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError(null)

    try {
      if (action === "archive") {
        await mutations.archive.mutateAsync(model.stateVersion)
        close(true)
        return
      }

      if (action === "unarchive") {
        await mutations.unarchive.mutateAsync(model.stateVersion)
        close(true)
        return
      }

      if (!acknowledged) {
        setError(t("migrationWorkspace.lifecycle.ackRequired"))
        return
      }

      if (action === "delete") {
        if (confirmationMigrationId !== model.migrationId) {
          setError(t("migrationWorkspace.lifecycle.deleteConfirmationMismatch"))
          return
        }

        await runDelete({
          expectedStateVersion: model.stateVersion,
          confirmationMigrationId,
          acknowledgeSourceUnaffected: true,
        })
        return
      }

      if (retention !== "remove" && retention !== "retain-encrypted") {
        setError(t("migrationWorkspace.lifecycle.retentionRequired"))
        return
      }

      await runCancel({
        expectedStateVersion: model.stateVersion,
        acknowledgeSourceUnaffected: true,
        encryptedPackageRetention: retention,
      })
    } catch (value) {
      setError(problemMessage(value, t))
    }
  }

  const title = action === "cancel"
    ? t("migrationWorkspace.lifecycle.cancelTitle")
    : action === "delete"
      ? t("migrationWorkspace.lifecycle.deleteTitle")
      : action === "archive"
        ? t("migrationWorkspace.lifecycle.archiveTitle")
        : t("migrationWorkspace.lifecycle.unarchiveTitle")
  const description = action === "cancel"
    ? t("migrationWorkspace.lifecycle.cancelDescription")
    : action === "delete"
      ? t("migrationWorkspace.lifecycle.deleteDescription")
      : action === "archive"
        ? t("migrationWorkspace.lifecycle.archiveDescription")
        : t("migrationWorkspace.lifecycle.unarchiveDescription")

  return (
    <>
      {createPortal(
        <div className="fixed inset-0 z-[55] overflow-y-auto px-4 py-8">
          <button
            type="button"
            className="fixed inset-0 bg-background/85 backdrop-blur-sm"
            aria-hidden="true"
            tabIndex={-1}
            disabled={mutations.isPending}
            onClick={() => close()}
          />
          <div className="relative z-10 mx-auto flex min-h-full max-w-xl items-center">
            <Card
              className="w-full shadow-xl"
              role="dialog"
              aria-modal="true"
              aria-labelledby="migration-session-lifecycle-action-title"
            >
              <CardHeader>
                <CardTitle id="migration-session-lifecycle-action-title">
                  {title}
                </CardTitle>
                <CardDescription>{description}</CardDescription>
              </CardHeader>
              <CardContent>
                <form className="space-y-5" onSubmit={submit}>
                  {error ? (
                    <Alert variant="destructive">
                      <AlertTitle>{t("migrationWorkspace.lifecycle.errorTitle")}</AlertTitle>
                      <AlertDescription>{error}</AlertDescription>
                    </Alert>
                  ) : null}

                  {action === "cancel" ? (
                    <>
                      <Alert>
                        <ShieldAlert className="h-4 w-4" aria-hidden="true" />
                        <AlertTitle>{t("migrationWorkspace.lifecycle.sourceTitle")}</AlertTitle>
                        <AlertDescription>{model.sourceUnaffectedNotice}</AlertDescription>
                      </Alert>

                      <div className="space-y-2">
                        <label
                          htmlFor="migration-package-retention"
                          className="text-sm font-medium"
                        >
                          {t("migrationWorkspace.lifecycle.retentionLabel")}
                        </label>
                        <Select
                          id="migration-package-retention"
                          value={retention}
                          disabled={mutations.isPending}
                          onChange={(event) => setRetention(event.target.value)}
                        >
                          <option value="">
                            {t("migrationWorkspace.lifecycle.retentionPlaceholder")}
                          </option>
                          <option value="remove">
                            {t("migrationWorkspace.lifecycle.retentionRemove")}
                          </option>
                          <option value="retain-encrypted">
                            {t("migrationWorkspace.lifecycle.retentionKeep")}
                          </option>
                        </Select>
                        <p className="text-xs text-muted-foreground">
                          {t("migrationWorkspace.lifecycle.plaintextAlwaysRemoved")}
                        </p>
                        <p className="text-xs text-muted-foreground">
                          {t("migrationWorkspace.lifecycle.decryptionIdentityCleared")}
                        </p>
                      </div>

                      <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
                        <Checkbox
                          checked={acknowledged}
                          disabled={mutations.isPending}
                          onCheckedChange={(checked) => setAcknowledged(checked === true)}
                        />
                        <span>{t("migrationWorkspace.lifecycle.acknowledgeSource")}</span>
                      </label>
                    </>
                  ) : null}
                  {action === "delete" ? (
                    <>
                      <Alert variant="destructive">
                        <ShieldAlert className="h-4 w-4" aria-hidden="true" />
                        <AlertTitle>{t("migrationWorkspace.lifecycle.deleteWarningTitle")}</AlertTitle>
                        <AlertDescription>
                          {t("migrationWorkspace.lifecycle.deleteWarningDescription")}
                        </AlertDescription>
                      </Alert>

                      <Alert>
                        <ShieldAlert className="h-4 w-4" aria-hidden="true" />
                        <AlertTitle>{t("migrationWorkspace.lifecycle.sourceTitle")}</AlertTitle>
                        <AlertDescription>{model.sourceUnaffectedNotice}</AlertDescription>
                      </Alert>

                      <div className="space-y-2">
                        <label
                          htmlFor="migration-delete-confirmation"
                          className="text-sm font-medium"
                        >
                          {t("migrationWorkspace.lifecycle.deleteConfirmationLabel")}
                        </label>
                        <Input
                          id="migration-delete-confirmation"
                          value={confirmationMigrationId}
                          disabled={mutations.isPending}
                          autoComplete="off"
                          spellCheck={false}
                          onChange={(event) => setConfirmationMigrationId(event.target.value)}
                        />
                        <p className="text-xs text-muted-foreground">
                          {t("migrationWorkspace.lifecycle.deleteConfirmationHelp", {
                            migrationId: model.migrationId,
                          })}
                        </p>
                      </div>

                      <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
                        <Checkbox
                          checked={acknowledged}
                          disabled={mutations.isPending}
                          onCheckedChange={(checked) => setAcknowledged(checked === true)}
                        />
                        <span>{t("migrationWorkspace.lifecycle.deleteAcknowledgeSource")}</span>
                      </label>
                    </>
                  ) : null}

                  <div className="flex justify-end gap-2">
                    <Button
                      type="button"
                      variant="outline"
                      disabled={mutations.isPending}
                      onClick={() => close()}
                    >
                      {t("migrationWorkspace.lifecycle.dismiss")}
                    </Button>
                    <Button
                      type="submit"
                      variant={action === "cancel" || action === "delete" ? "destructive" : "default"}
                      disabled={mutations.isPending}
                    >
                      {mutations.isPending
                        ? t("migrationWorkspace.lifecycle.applying")
                        : action === "cancel"
                          ? t("migrationWorkspace.lifecycle.confirmCancel")
                          : action === "delete"
                            ? t("migrationWorkspace.lifecycle.confirmDelete")
                            : action === "archive"
                              ? t("migrationWorkspace.lifecycle.confirmArchive")
                              : t("migrationWorkspace.lifecycle.confirmUnarchive")}
                    </Button>
                  </div>
                </form>
              </CardContent>
            </Card>
          </div>
        </div>,
        document.body,
      )}

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => {
          if (pendingStepUp?.action === "cancel") {
            void runCancel(pendingStepUp.request)
          } else if (pendingStepUp?.action === "delete") {
            void runDelete(pendingStepUp.request)
          }
        }}
      />
    </>
  )
}

type Translate = I18nContextValue["t"]

function problemMessage(value: unknown, t: Translate) {
  const problem = value as MigrationSessionLifecycleProblemError
  const key = lifecycleProblemKey(problem?.code)
  return key
    ? t(key)
    : problem instanceof Error
      ? problem.message
      : t("migrationWorkspace.lifecycle.genericError")
}

function lifecycleProblemKey(code: string | null | undefined): TranslationKey | null {
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
    case "migration_session_source_acknowledgement_required":
      return "migrationWorkspace.lifecycle.problem.migration_session_source_acknowledgement_required"
    case "migration_session_retention_policy_invalid":
      return "migrationWorkspace.lifecycle.problem.migration_session_retention_policy_invalid"
    case "migration_session_package_cleanup_failed":
      return "migrationWorkspace.lifecycle.problem.migration_session_package_cleanup_failed"
    case "migration_session_progressed_cleanup_failed":
      return "migrationWorkspace.lifecycle.problem.migration_session_progressed_cleanup_failed"
    case "migration_session_cancel_failed":
      return "migrationWorkspace.lifecycle.problem.migration_session_cancel_failed"
    case "migration_session_archive_not_allowed":
      return "migrationWorkspace.lifecycle.problem.migration_session_archive_not_allowed"
    case "migration_session_cancel_not_allowed":
      return "migrationWorkspace.lifecycle.problem.migration_session_cancel_not_allowed"
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
