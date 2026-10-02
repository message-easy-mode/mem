import { useEffect, useRef, useState } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { Link } from "react-router-dom"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { isMigrationStagingStepUpRequired } from "../api/migration-staging"
import { acceptStagingRetirement, getStagingRetirementReview,
  type StagingRetirementRequest, type StagingRetirementTarget } from "../api/migration-staging-retirement"
import { migrationSessionKeys } from "../hooks/use-migration-sessions"
import { migrationStagingKeys } from "../hooks/use-migration-staging"
import { retirementBlockerKey, retirementStepKey } from "./migration-staging-retirement-labels"

/** Shared by Services and Migration. GET reviews; only explicit confirmation POSTs.
 * Accepted work belongs to the server and can be rediscovered after navigation. */
export function MigrationStagingRetirementDialog({ target, onClose, onChanged }: {
  target: StagingRetirementTarget
  onClose: () => void
  onChanged?: () => Promise<unknown>
}) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const queryKey = ["migration-staging-retirement", target.migrationId, target.stagingRunId] as const
  const [confirmedFingerprint, setConfirmedFingerprint] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const sendingRef = useRef(false)
  const [uncertain, setUncertain] = useState(false)
  const reconciliationAfter = useRef(0)
  const [submissionFailed, setSubmissionFailed] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const pendingStepUp = useRef<StagingRetirementRequest | null>(null)
  const changedRef = useRef(onChanged)
  changedRef.current = onChanged
  const query = useQuery({
    queryKey,
    queryFn: ({ signal }) => getStagingRetirementReview(target, signal),
    retry: 0,
    staleTime: 0,
    refetchOnMount: "always",
    refetchInterval: (state) => uncertain || ["queued", "running"].includes(state.state.data?.operation?.status ?? "") ? 2_000 : 15_000,
  })
  const review = query.data
  const operation = review?.operation
  const active = operation?.status === "queued" || operation?.status === "running"
  const retired = operation?.status === "retired"
  const checked = Boolean(review?.reviewFingerprint && confirmedFingerprint === review.reviewFingerprint)
  const actionable = !query.isError && !query.isFetching && !uncertain && !sending && !active && !retired &&
    review?.canRetire === true && Boolean(review.reviewFingerprint)

  useEffect(() => {
    if (!operation) return
    void Promise.all([
      queryClient.invalidateQueries({ queryKey: ["services", "temporary-staging"] }),
      queryClient.invalidateQueries({ queryKey: migrationStagingKeys.list(target.migrationId) }),
      queryClient.invalidateQueries({ queryKey: migrationSessionKeys.all }),
      queryClient.invalidateQueries({ queryKey: ["migration-guided-evidence", target.migrationId] }),
      changedRef.current?.(),
    ]).catch(() => undefined)
  }, [operation?.operationId, operation?.updatedAtUtc, queryClient, target.migrationId])

  // A failed HTTP response never authorizes replay. Reconcile through GET first.
  useEffect(() => {
    if (uncertain && !query.isFetching && !query.isError && query.dataUpdatedAt > reconciliationAfter.current) setUncertain(false)
  }, [uncertain, query.isFetching, query.isError, query.dataUpdatedAt])

  async function submit(request: StagingRetirementRequest) {
    if (sendingRef.current) return
    sendingRef.current = true
    setSending(true)
    setSubmissionFailed(false)
    await queryClient.cancelQueries({ queryKey, exact: true })
    try {
      await acceptStagingRetirement(target, request)
      // A 202 means accepted, not retired. A fresh GET is the presentation truth.
      setConfirmedFingerprint(null)
      reconciliationAfter.current = queryClient.getQueryState(queryKey)?.dataUpdatedAt ?? 0
      setUncertain(true)
      await query.refetch()
    } catch (error) {
      if (isMigrationStagingStepUpRequired(error)) {
        pendingStepUp.current = request
        setStepUpOpen(true)
      } else {
        setConfirmedFingerprint(null)
        setSubmissionFailed(true)
        reconciliationAfter.current = queryClient.getQueryState(queryKey)?.dataUpdatedAt ?? 0
        setUncertain(true)
        await query.refetch()
      }
    } finally {
      sendingRef.current = false
      setSending(false)
    }
  }

  return <>
    <ConfirmationDialog
      open={!stepUpOpen}
      onOpenChange={(open) => { if (!open) onClose() }}
      title={t("migrationRetirement.title")}
      description={t("migrationRetirement.description")}
      confirmLabel={t(retired ? "migrationRetirement.retired" : active ? "migrationRetirement.accepted"
        : operation ? "migrationRetirement.retry" : "migrationRetirement.confirm")}
      confirmingLabel={t("migrationRetirement.submitting")}
      cancelLabel={t("migrationRetirement.close")}
      confirmVariant="destructive"
      confirmDisabled={!actionable || !checked}
      isConfirming={sending}
      showProgress={sending}
      onConfirm={() => {
        if (actionable && checked && review?.reviewFingerprint) void submit({
          reviewFingerprint: review.reviewFingerprint, confirmRetirement: true, retry: Boolean(operation),
        })
      }}
      className="max-h-[calc(100dvh-2rem)] max-w-xl overflow-y-auto"
    >
      <div className="rounded-lg border bg-muted/20 p-3 text-sm">
        <div className="font-medium break-words">{review?.displayName ?? target.migrationId}</div>
        <p className="mt-1 break-all font-mono text-xs text-muted-foreground">{target.stagingRunId}</p>
      </div>
      {(query.isPending || uncertain) && <p role="status" className="text-sm">{t("migrationRetirement.checking")}</p>}
      {query.isError && <p role="alert" className="text-sm text-destructive">{t("migrationRetirement.unavailable")}</p>}
      {submissionFailed && !active && !retired && <p role="alert" className="text-sm text-destructive">{t("migrationRetirement.submitFailed")}</p>}
      {operation && <div role="status" className="space-y-2 rounded-lg border p-3 text-sm">
        <p className="font-medium">{t(retired ? "migrationRetirement.retired" : active ? "migrationRetirement.inProgress" : "migrationRetirement.needsAttention")}</p>
        {!retired && operation.currentStep !== "needs-attention" && <p>{t(retirementStepKey(operation.currentStep))}</p>}
        <p className="break-all font-mono text-xs">{operation.operationId}</p>
        {operation.failureCode && !active && <p>{t(retirementBlockerKey(operation.failureCode))}</p>}
        <p>{t(retired ? "migrationRetirement.completeDetail" : "migrationRetirement.durable")}</p>
      </div>}
      {review && !active && !retired && <>
        {!review.canRetire && <p role="alert" className="text-sm">{t(retirementBlockerKey(review.blockerCode))}</p>}
        {review.canRetire && <div className="space-y-2 text-sm">
          <p>{t("migrationRetirement.scope", { containers: review.containerCount, networks: review.networkCount })}</p>
          <p>{t(review.workspacePresent ? "migrationRetirement.workspacePresent" : "migrationRetirement.workspaceAbsent")}</p>
          <p>{t("migrationRetirement.retained")}</p>
          <p>{t("migrationRetirement.continuation")}</p>
          <p>{t("migrationRetirement.boundary")}</p>
          <label className="flex items-start gap-3 rounded-lg border border-destructive/30 p-3">
            <input type="checkbox" className="mt-1 h-4 w-4 shrink-0" checked={checked}
              disabled={!actionable} onChange={(event) => setConfirmedFingerprint(event.target.checked ? review.reviewFingerprint : null)} />
            <span>{t("migrationRetirement.acknowledge")}</span>
          </label>
        </div>}
      </>}
      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" variant="outline" size="sm" disabled={sending || query.isFetching}
          onClick={() => { setConfirmedFingerprint(null); void query.refetch() }}>{t("migrationRetirement.refresh")}</Button>
        <Link className="text-sm underline" to={`/migrations/${encodeURIComponent(target.migrationId)}`} onClick={onClose}>
          {t("services.staging.openMigration")}
        </Link>
      </div>
    </ConfirmationDialog>
    <OperatorStepUpDialog open={stepUpOpen} onOpenChange={setStepUpOpen} onVerified={() => {
      const request = pendingStepUp.current
      pendingStepUp.current = null
      setStepUpOpen(false)
      // Reuse exactly what was reviewed and explicitly confirmed. The server rejects
      // a changed fingerprint; step-up never silently accepts a changed scope.
      if (request) void submit(request)
    }} />
  </>
}
