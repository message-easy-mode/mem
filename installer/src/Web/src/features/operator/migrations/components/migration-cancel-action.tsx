import { useEffect, useRef, useState } from "react"
import { useQueryClient } from "@tanstack/react-query"
import { XCircle } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Select } from "@/components/ui/select"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  cancelMigrationSession, isMigrationSessionLifecycleStepUpRequired,
  type MigrationSessionCancelRequest,
} from "../api/migration-sessions"
import type { MigrationWorkspaceCancellation } from "../api/migration-workspace"
import {
  isGuidedCancellationAllowed, isMigrationOutcomeUncertain, readPendingGuidedOperation,
} from "../api/migration-guided-state"
import { migrationSessionKeys } from "../hooks/use-migration-sessions"
import { useMigrationGuidedState } from "./migration-guided-state-context"
import { MigrationStagingRetirementDialog } from "./migration-staging-retirement-dialog"

/** Visible pre-production exit; reuses the lifecycle endpoint and the one guided observer. */
export function MigrationCancelAction() {
  const { t } = useI18n()
  const guide = useMigrationGuidedState()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const migrationId = guide.workspace.migration.migrationId
  const cancellation = guide.workspace.guided.cancellation
  const [review, setReview] = useState<MigrationWorkspaceCancellation | null>(null)
  const [open, setOpen] = useState(false)
  const [retention, setRetention] = useState("")
  const [acknowledged, setAcknowledged] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const submitting = useRef(false)
  const redirected = useRef(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [stepUpRequest, setStepUpRequest] = useState<MigrationSessionCancelRequest | null>(null)
  const [stagingBlockOpen, setStagingBlockOpen] = useState(false)
  const [stagingRetirementOpen, setStagingRetirementOpen] = useState(false)
  const [followingResult, setFollowingResult] = useState(() =>
    readPendingGuidedOperation(migrationId)?.operation === "cancel-migration")
  const pending = guide.pendingOperation === "cancel-migration"
  const stagingRunId = guide.workspace.guided.stagingRunId
  const stagingRetirementRequired = cancellation?.canCancel === false &&
    cancellation.lifecycleStatus === "active" &&
    cancellation.blockedCode === "migration_session_staging_retained" &&
    guide.workspace.guided.stagingRetained === true &&
    Boolean(stagingRunId)
  const changed = review !== null && (cancellation?.stateVersion !== review.stateVersion ||
    cancellation?.confirmationKind !== review.confirmationKind || !isGuidedCancellationAllowed(guide.workspace))

  useEffect(() => {
    // Do not redirect someone simply viewing cancelled history. Only follow an
    // explicit submission (or its same-tab recovery receipt) to the inventory.
    if (!followingResult || cancellation?.lifecycleStatus !== "cancelled" || redirected.current) return
    redirected.current = true
    void queryClient.invalidateQueries({ queryKey: migrationSessionKeys.list() })
    navigate("/migrations", { replace: true, state: {
      cancelledMigrationName: guide.workspace.migration.displayName,
    } })
  }, [cancellation?.lifecycleStatus, followingResult, guide.workspace.migration.displayName, navigate, queryClient])

  function openConfirmation() {
    if (guide.blocked || !isGuidedCancellationAllowed(guide.workspace) || !cancellation) return
    // Keep the version and wording which the operator actually reviewed. A new
    // snapshot must not silently turn empty-session consent into package removal.
    setReview({ ...cancellation })
    setRetention("")
    setAcknowledged(false)
    setError(null)
    setStepUpRequest(null)
    setOpen(true)
  }

  async function submit(request: MigrationSessionCancelRequest) {
    if (submitting.current || guide.blocked) return
    if (!isGuidedCancellationAllowed(guide.workspace) || changed ||
      request.expectedStateVersion !== cancellation?.stateVersion) {
      setError(t("migrationWorkspace.cancel.changed"))
      setOpen(true)
      return
    }
    submitting.current = true
    setSending(true)
    setFollowingResult(true)
    setError(null)
    try {
      await guide.run("cancel-migration", () => cancelMigrationSession(migrationId, request))
      // Only the canonical workspace read closes this journey. The mutation
      // response never supplies presentation state or invents inventory counts.
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setFollowingResult(false)
      if (isMigrationSessionLifecycleStepUpRequired(caught)) {
        setStepUpRequest(request)
        setOpen(false)
        setStepUpOpen(true)
        return
      }
      setError(caught instanceof Error ? caught.message : t("migrationWorkspace.lifecycle.genericError"))
      setOpen(true)
    } finally {
      submitting.current = false
      setSending(false)
    }
  }

  function confirm() {
    if (!review || changed || guide.blocked) return
    if (review.confirmationKind === "package" || review.confirmationKind === "progressed") {
      if (retention !== "remove" && retention !== "retain-encrypted") {
        setError(t("migrationWorkspace.lifecycle.retentionRequired"))
        return
      }
      if (!acknowledged) {
        setError(t("migrationWorkspace.lifecycle.ackRequired"))
        return
      }
    }
    // The explicit empty-session confirmation acknowledges its source-unaffected
    // description. Retention is still sent to the unchanged API contract, but
    // there is no uploaded package for the operator to make a retention choice on.
    void submit({
      expectedStateVersion: review.stateVersion,
      acknowledgeSourceUnaffected: true,
      encryptedPackageRetention: review.confirmationKind === "empty-session" ? "remove" :
        retention as MigrationSessionCancelRequest["encryptedPackageRetention"],
    })
  }

  return (
    <>
      {cancellation?.canCancel ? (
        <Button type="button" variant="outline" onClick={openConfirmation}
          disabled={guide.blocked || sending || stepUpOpen || !isGuidedCancellationAllowed(guide.workspace)}>
          <XCircle className="mr-2 h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.lifecycle.cancelAction")}
        </Button>
      ) : stagingRetirementRequired ? (
        <Button type="button" variant="outline" onClick={() => setStagingBlockOpen(true)}
          disabled={guide.blocked || stagingRetirementOpen}>
          <XCircle className="mr-2 h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.lifecycle.cancelAction")}
        </Button>
      ) : null}
      <ConfirmationDialog
        open={stagingBlockOpen}
        onOpenChange={setStagingBlockOpen}
        title={t("migrationWorkspace.cancel.stagingRequiredTitle")}
        description={t("migrationWorkspace.cancel.stagingRequiredDescription")}
        cancelLabel={t("migrationWorkspace.cancel.keep")}
        confirmLabel={t("migrationWorkspace.cancel.reviewStagingRetirement")}
        onConfirm={() => {
          setStagingBlockOpen(false)
          setStagingRetirementOpen(true)
        }}
      />
      <ConfirmationDialog
        open={open}
        onOpenChange={setOpen}
        title={t("migrationWorkspace.cancel.title")}
        description={t(review?.confirmationKind === "empty-session"
          ? "migrationWorkspace.cancel.emptyDescription"
          : review?.confirmationKind === "progressed"
            ? "migrationWorkspace.cancel.progressedDescription"
            : "migrationWorkspace.cancel.packageDescription")}
        cancelLabel={t(pending ? "migrationWorkspace.cancel.close" : "migrationWorkspace.cancel.keep")}
        confirmLabel={t("migrationWorkspace.lifecycle.cancelAction")}
        confirmingLabel={t("migrationWorkspace.cancel.cancelling")}
        confirmVariant="destructive"
        className="max-h-[calc(100dvh-2rem)] overflow-y-auto"
        isConfirming={sending}
        confirmDisabled={guide.blocked || changed}
        showProgress={sending}
        onConfirm={confirm}
      >
        {changed && !pending ? (
          <Alert role="status"><AlertDescription>{t("migrationWorkspace.cancel.changed")}</AlertDescription></Alert>
        ) : null}
        {error ? <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert> : null}
        {pending && !sending ? (
          <Alert role="status"><AlertDescription>{t("migrationWorkspace.cancel.uncertain")}</AlertDescription></Alert>
        ) : null}
        {review?.confirmationKind === "progressed" ? (
          <Alert role="status">
            <AlertDescription>{t("migrationWorkspace.cancel.progressedScope")}</AlertDescription>
          </Alert>
        ) : null}
        {review?.confirmationKind === "package" || review?.confirmationKind === "progressed" ? (
          <>
            <label className="block space-y-2 text-sm">
              <span className="font-medium">{t("migrationWorkspace.lifecycle.retentionLabel")}</span>
              <Select value={retention} onChange={(event) => setRetention(event.target.value)} disabled={sending || pending || changed}>
                <option value="">{t("migrationWorkspace.lifecycle.retentionPlaceholder")}</option>
                <option value="remove">{t("migrationWorkspace.lifecycle.retentionRemove")}</option>
                <option value="retain-encrypted">{t("migrationWorkspace.lifecycle.retentionKeep")}</option>
              </Select>
            </label>
            <p className="text-xs text-muted-foreground">{t("migrationWorkspace.lifecycle.plaintextAlwaysRemoved")}</p>
            <p className="text-xs text-muted-foreground">{t("migrationWorkspace.lifecycle.decryptionIdentityCleared")}</p>
            <label className="flex items-start gap-3 rounded-lg border p-3 text-sm">
              <Checkbox checked={acknowledged} disabled={sending || pending || changed}
                onCheckedChange={(value) => setAcknowledged(value === true)} />
              <span>{t("migrationWorkspace.lifecycle.acknowledgeSource")}</span>
            </label>
          </>
        ) : null}
      </ConfirmationDialog>
      {stagingRetirementOpen && stagingRunId ? (
        <MigrationStagingRetirementDialog
          target={{ migrationId, stagingRunId }}
          onClose={() => setStagingRetirementOpen(false)}
          onChanged={guide.refresh}
        />
      ) : null}
      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => {
          setStepUpOpen(false)
          if (stepUpRequest) {
            setOpen(true)
            void submit(stepUpRequest)
          }
        }}
      />
    </>
  )
}
