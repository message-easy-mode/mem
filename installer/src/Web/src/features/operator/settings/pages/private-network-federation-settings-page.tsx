import { useEffect, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  LoaderCircle,
  Network,
  RefreshCw,
  RotateCcw,
  ShieldAlert,
  Trash2,
} from "lucide-react"

import type { TranslationKey } from "@/app/i18n/messages"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select } from "@/components/ui/select"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import {
  isPrivateNetworkStepUpRequired,
  privateNetworkProblemCode,
  privateNetworkProblemDetail,
  type PrivateNetworkExceptionAction,
  type PrivateNetworkFederationApplyRequest,
  type PrivateNetworkFederationApplyResult,
  type PrivateNetworkFederationReview,
  type PrivateNetworkFederationStackState,
} from "../api/private-network-federation.api"
import {
  useApplyPrivateNetworkFederationException,
  usePrivateNetworkFederationInventory,
  useReviewPrivateNetworkFederationException,
} from "../hooks/use-private-network-federation"
import { SettingsSectionNavigation } from "../components/settings-section-navigation"

function problemMessage(error: unknown, t: (key: TranslationKey) => string) {
  const code = privateNetworkProblemCode(error)
  const byCode: Record<string, TranslationKey> = {
    private_network_exact_address_required: "settings.network.problem.exactAddress",
    private_network_address_required: "settings.network.problem.addressRequired",
    private_network_address_invalid: "settings.network.problem.addressInvalid",
    private_network_address_unsafe: "settings.network.problem.addressUnsafe",
    private_network_address_not_private: "settings.network.problem.notPrivate",
    private_network_config_custom_unsupported: "settings.network.problem.customConfig",
    private_network_federation_state_unsupported: "settings.network.problem.federationState",
    private_network_review_stale: "settings.network.problem.reviewStale",
    private_network_operation_in_progress: "settings.network.problem.operationRunning",
    private_network_no_change: "settings.network.problem.noChange",
  }
  if (code && byCode[code]) return t(byCode[code])
  return privateNetworkProblemDetail(error) ?? t("settings.network.problem.default")
}

type PendingApply = {
  input: PrivateNetworkFederationApplyRequest
  runtimeStackId: string
  canonicalCidr: string
}

type ReconciledOutcome = {
  action: PrivateNetworkExceptionAction
  canonicalCidr: string
}

const RECONCILIATION_SETTLE_DELAYS_MS = [500, 1000, 2000, 3000, 5000] as const

function refreshedStateHasRequestedExceptionResult(
  stack: PrivateNetworkFederationStackState | undefined,
  action: PrivateNetworkExceptionAction,
  canonicalCidr: string,
) {
  if (!stack) return false
  const present = stack.currentExceptions.includes(canonicalCidr)
  return action === "add" ? present : !present
}

function refreshedStateConfirmsApply(
  stack: PrivateNetworkFederationStackState | undefined,
  action: PrivateNetworkExceptionAction,
  canonicalCidr: string,
) {
  return refreshedStateHasRequestedExceptionResult(stack, action, canonicalCidr) &&
    stack?.configurationState === "healthy" &&
    stack.federationConfigurationState === "healthy" &&
    stack.matrixContainerRunning
}

function refreshedStateMayStillSettle(
  stack: PrivateNetworkFederationStackState | undefined,
  action: PrivateNetworkExceptionAction,
  canonicalCidr: string,
) {
  return refreshedStateHasRequestedExceptionResult(stack, action, canonicalCidr) &&
    stack?.configurationState === "healthy" &&
    stack.matrixContainerRunning &&
    stack.federationConfigurationState !== "healthy"
}

function waitForReconciliationDelay(delayMs: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, delayMs)
  })
}

export function PrivateNetworkFederationSettingsPage() {
  const { t } = useI18n()
  const { session } = useOperatorSession()
  const isPlatformOwner = session.roles.includes("platform_owner")
  const inventory = usePrivateNetworkFederationInventory(isPlatformOwner)
  const stacks = inventory.data?.stacks ?? []
  const [selectedSlug, setSelectedSlug] = useState("")
  const selected = stacks.find((stack) => stack.slug === selectedSlug) ?? stacks[0]
  const [address, setAddress] = useState("")
  const [review, setReview] = useState<PrivateNetworkFederationReview | null>(null)
  const [pendingApply, setPendingApply] = useState<PendingApply | null>(null)
  const [outcome, setOutcome] = useState<PrivateNetworkFederationApplyResult | null>(null)
  const [reconciledOutcome, setReconciledOutcome] = useState<ReconciledOutcome | null>(null)
  const [reconcilingAfterError, setReconcilingAfterError] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)

  const reviewMutation = useReviewPrivateNetworkFederationException(selected?.slug ?? "")
  const applyMutation = useApplyPrivateNetworkFederationException(selected?.slug ?? "")

  const selectedStateHealthy = selected !== undefined &&
    selected.configurationState === "healthy" &&
    selected.federationConfigurationState === "healthy" &&
    selected.matrixContainerRunning
  const localOnly = selected?.federationMode === "local_only"
  const canAdd = selectedStateHealthy &&
    (selected?.federationMode === "public" || selected?.federationMode === "restricted")
  const canRemove = selectedStateHealthy &&
    (selected?.federationMode === "public" ||
      selected?.federationMode === "restricted" ||
      localOnly)

  useEffect(() => {
    if (!selectedSlug && stacks.length > 0) {
      setSelectedSlug(stacks[0].slug)
    }
  }, [selectedSlug, stacks])

  useEffect(() => {
    setAddress("")
    setReview(null)
    setPendingApply(null)
    setOutcome(null)
    setReconciledOutcome(null)
    setReconcilingAfterError(false)
    reviewMutation.reset()
    applyMutation.reset()
    // Mutation objects are intentionally excluded; selected stack is the reset boundary.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selected?.slug])

  if (!isPlatformOwner) {
    return (
      <Card className="mx-auto max-w-2xl">
        <CardHeader>
          <CardTitle>{t("settings.security.accessDeniedTitle")}</CardTitle>
          <CardDescription>{t("settings.security.accessDeniedDescription")}</CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const runReview = (
    reviewAddress: string,
    reviewAction: PrivateNetworkExceptionAction,
  ) => {
    if (!selected || !reviewAddress.trim()) return
    setOutcome(null)
    setReconciledOutcome(null)
    setReview(null)
    applyMutation.reset()
    reviewMutation.reset()
    reviewMutation.mutate(
      { address: reviewAddress.trim(), action: reviewAction },
      { onSuccess: (result) => setReview(result) },
    )
  }

  const requestAdditionReview = () => {
    runReview(address, "add")
  }

  const requestRemovalReview = (entry: string) => {
    const separator = entry.lastIndexOf("/")
    if (separator <= 0) return
    runReview(entry.slice(0, separator), "remove")
  }

  const runApply = (pending: PendingApply) => {
    setPendingApply(pending)
    setReconcilingAfterError(false)
    applyMutation.mutate(pending.input, {
      onSuccess: (result) => {
        setOutcome(result)
        setReconciledOutcome(null)
        setPendingApply(null)
        setReview(null)
      },
      onError: async (error) => {
        if (isPrivateNetworkStepUpRequired(error)) {
          setStepUpOpen(true)
          return
        }

        setReconcilingAfterError(true)
        try {
          let refreshed = await inventory.refetch()
          let refreshedStack = refreshed.data?.stacks.find(
            (stack) => stack.runtimeStackId === pending.runtimeStackId,
          )

          for (const delayMs of RECONCILIATION_SETTLE_DELAYS_MS) {
            if (refreshedStateConfirmsApply(
              refreshedStack,
              pending.input.action,
              pending.canonicalCidr,
            )) {
              break
            }

            if (!refreshedStateMayStillSettle(
              refreshedStack,
              pending.input.action,
              pending.canonicalCidr,
            )) {
              break
            }

            await waitForReconciliationDelay(delayMs)
            refreshed = await inventory.refetch()
            refreshedStack = refreshed.data?.stacks.find(
              (stack) => stack.runtimeStackId === pending.runtimeStackId,
            )
          }

          if (refreshedStateConfirmsApply(
            refreshedStack,
            pending.input.action,
            pending.canonicalCidr,
          )) {
            applyMutation.reset()
            setReconciledOutcome({
              action: pending.input.action,
              canonicalCidr: pending.canonicalCidr,
            })
            setOutcome(null)
            setPendingApply(null)
            setReview(null)
          }
        } finally {
          setPendingApply(null)
          setReconcilingAfterError(false)
        }
      },
    })
  }

  const confirmApply = () => {
    if (!review) return
    const input: PrivateNetworkFederationApplyRequest = {
      address: review.canonicalAddress,
      action: review.action,
      reviewHash: review.reviewHash,
      idempotencyKey: `private-network-${review.runtimeStackId}-${crypto.randomUUID()}`,
    }
    const pending: PendingApply = {
      input,
      runtimeStackId: review.runtimeStackId,
      canonicalCidr: review.canonicalCidr,
    }
    setReview(null)
    runApply(pending)
  }

  const error = reviewMutation.error
    ? problemMessage(reviewMutation.error, t)
    : !reconcilingAfterError && applyMutation.error && !isPrivateNetworkStepUpRequired(applyMutation.error)
      ? problemMessage(applyMutation.error, t)
      : null

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="text-sm font-medium text-muted-foreground">{t("settings.breadcrumb")}</div>
        <h1 className="text-3xl font-semibold tracking-tight">{t("settings.network.title")}</h1>
        <p className="max-w-3xl text-muted-foreground">{t("settings.network.description")}</p>
      </div>

      <SettingsSectionNavigation />

      <Alert className="border-amber-500/40 bg-amber-500/10">
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>{t("settings.network.scopeTitle")}</AlertTitle>
        <AlertDescription>{t("settings.network.scopeDescription")}</AlertDescription>
      </Alert>

      {inventory.isLoading ? (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              {t("settings.network.loading")}
            </CardTitle>
          </CardHeader>
        </Card>
      ) : null}

      {inventory.isError ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("settings.network.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{problemMessage(inventory.error, t)}</AlertDescription>
        </Alert>
      ) : null}

      {stacks.length > 0 && selected ? (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Network className="h-5 w-5" />
              {t("settings.network.manageTitle")}
            </CardTitle>
            <CardDescription>{t("settings.network.manageDescription")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            <div className="space-y-2">
              <Label htmlFor="private-network-stack">{t("settings.network.stackLabel")}</Label>
              <Select
                id="private-network-stack"
                value={selected.slug}
                onChange={(event) => setSelectedSlug(event.target.value)}
              >
                {stacks.map((stack) => (
                  <option key={stack.runtimeStackId} value={stack.slug}>
                    {stack.slug} — {stack.matrixServerName}
                  </option>
                ))}
              </Select>
            </div>

            <div className="grid gap-3 sm:grid-cols-3">
              <Fact label={t("settings.network.federationMode")} value={selected.federationMode} />
              <Fact label={t("settings.network.configState")} value={`${selected.configurationState} / ${selected.federationConfigurationState}`} />
              <Fact
                label={t("settings.network.matrixRuntime")}
                value={selected.matrixContainerRunning
                  ? t("settings.network.running")
                  : t("settings.network.notRunning")}
              />
            </div>

            {selected.configurationState !== "healthy" ||
            selected.federationConfigurationState !== "healthy" ||
            !selected.matrixContainerRunning ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("settings.network.readOnlyTitle")}</AlertTitle>
                <AlertDescription>
                  {!selected.matrixContainerRunning
                    ? t("settings.network.runtimeUnavailableDescription")
                    : selected.detail ?? t("settings.network.readOnlyDescription")}
                </AlertDescription>
              </Alert>
            ) : null}

            <div className="space-y-3 rounded-xl border border-border p-4">
              <div>
                <h2 className="font-medium">{t("settings.network.currentTitle")}</h2>
                <p className="text-sm text-muted-foreground">{t("settings.network.currentDescription")}</p>
              </div>
              {selected.currentExceptions.length > 0 ? (
                <div className="space-y-2">
                  {selected.currentExceptions.map((entry) => (
                    <div key={entry} className="flex flex-wrap items-center justify-between gap-2 rounded-lg border p-3">
                      <code className="text-sm">{entry}</code>
                      {(entry.endsWith("/32") || entry.endsWith("/128")) ? (
                        <Button
                          type="button"
                          size="sm"
                          variant="outline"
                          disabled={!canRemove || reviewMutation.isPending || applyMutation.isPending}
                          onClick={() => requestRemovalReview(entry)}
                        >
                          <Trash2 className="mr-2 h-4 w-4" />
                          {t("settings.network.reviewRemoval")}
                        </Button>
                      ) : (
                        <Badge variant="outline">{t("settings.network.broadEntry")}</Badge>
                      )}
                    </div>
                  ))}
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">{t("settings.network.none")}</p>
              )}
            </div>

            {localOnly ? (
              <Alert className="border-amber-500/40 bg-amber-500/10">
                <ShieldAlert className="h-4 w-4" />
                <AlertTitle>{t("settings.network.localOnlyCleanupTitle")}</AlertTitle>
                <AlertDescription>{t("settings.network.localOnlyCleanupDescription")}</AlertDescription>
              </Alert>
            ) : (
              <div className="space-y-3 rounded-xl border border-border p-4">
                <div>
                  <h2 className="font-medium">{t("settings.network.addTitle")}</h2>
                  <p className="text-sm text-muted-foreground">{t("settings.network.addDescription")}</p>
                </div>
                <div className="grid gap-4 md:grid-cols-[minmax(0,1fr)_auto] md:items-end">
                  <div className="space-y-2">
                    <Label htmlFor="private-network-address">{t("settings.network.addressLabel")}</Label>
                    <Input
                      id="private-network-address"
                      value={address}
                      onChange={(event) => setAddress(event.target.value)}
                      placeholder="10.0.0.238"
                      autoComplete="off"
                      spellCheck={false}
                    />
                    <p className="text-xs text-muted-foreground">{t("settings.network.addressHelp")}</p>
                  </div>
                  <Button
                    type="button"
                    onClick={requestAdditionReview}
                    disabled={
                      !address.trim() ||
                      !canAdd ||
                      reviewMutation.isPending ||
                      applyMutation.isPending
                    }
                  >
                    {reviewMutation.isPending ? (
                      <><RefreshCw className="mr-2 h-4 w-4 animate-spin" />{t("settings.network.reviewing")}</>
                    ) : t("settings.network.reviewAddition")}
                  </Button>
                </div>
              </div>
            )}

            {error ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("settings.network.problemTitle")}</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            ) : null}

            {applyMutation.isPending ? (
              <Alert>
                <RefreshCw className="h-4 w-4 animate-spin" />
                <AlertTitle>{t("settings.network.applyingTitle")}</AlertTitle>
                <AlertDescription>{t("settings.network.applyingDescription")}</AlertDescription>
              </Alert>
            ) : null}

            {reconcilingAfterError ? (
              <Alert>
                <RefreshCw className="h-4 w-4 animate-spin" />
                <AlertTitle>{t("settings.network.reconcilingTitle")}</AlertTitle>
                <AlertDescription>{t("settings.network.reconcilingDescription")}</AlertDescription>
              </Alert>
            ) : null}

            {reconciledOutcome ? <ReconciledOutcomeAlert result={reconciledOutcome} /> : null}
            {outcome ? <OutcomeAlert result={outcome} /> : null}
          </CardContent>
        </Card>
      ) : null}

      {!inventory.isLoading && !inventory.isError && stacks.length === 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.network.noStacksTitle")}</CardTitle>
            <CardDescription>{t("settings.network.noStacksDescription")}</CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      <ConfirmationDialog
        open={review !== null}
        onOpenChange={(open) => { if (!open) setReview(null) }}
        title={t("settings.network.reviewTitle")}
        description={t("settings.network.reviewDescription")}
        confirmLabel={t("settings.network.confirm")}
        cancelLabel={t("settings.network.cancel")}
        confirmDisabled={review?.noChange === true}
        onConfirm={confirmApply}
        className="max-w-2xl"
      >
        {review ? (
          <div className="space-y-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <Fact label={t("settings.network.reviewStack")} value={review.slug} />
              <Fact label={t("settings.network.reviewAction")} value={review.action} />
              <Fact label={t("settings.network.reviewAddress")} value={review.canonicalAddress} />
              <Fact label={t("settings.network.reviewException")} value={review.canonicalCidr} />
            </div>
            <ExceptionList title={t("settings.network.reviewCurrent")} entries={review.currentExceptions} />
            <ExceptionList title={t("settings.network.reviewProposed")} entries={review.proposedExceptions} />
            <Alert className="border-amber-500/40 bg-amber-500/10">
              <ShieldAlert className="h-4 w-4" />
              <AlertTitle>{t("settings.network.reviewScopeTitle")}</AlertTitle>
              <AlertDescription>{t("settings.network.reviewScopeDescription")}</AlertDescription>
            </Alert>
            <Alert>
              <RotateCcw className="h-4 w-4" />
              <AlertTitle>{t("settings.network.rollbackTitle")}</AlertTitle>
              <AlertDescription>{t("settings.network.rollbackDescription")}</AlertDescription>
            </Alert>
            <p className="font-medium">{review.confirmationText}</p>
          </div>
        ) : null}
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => { if (pendingApply) runApply(pendingApply) }}
      />
    </div>
  )
}

function Fact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-muted/20 p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</p>
      <p className="mt-2 break-all font-medium">{value}</p>
    </div>
  )
}

function ExceptionList({ title, entries }: { title: string; entries: string[] }) {
  const { t } = useI18n()
  return (
    <div className="rounded-lg border p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{title}</p>
      {entries.length > 0 ? (
        <ul className="mt-2 space-y-1">
          {entries.map((entry) => <li key={entry}><code className="text-sm">{entry}</code></li>)}
        </ul>
      ) : <p className="mt-2 text-sm text-muted-foreground">{t("settings.network.none")}</p>}
    </div>
  )
}

function ReconciledOutcomeAlert({ result }: { result: ReconciledOutcome }) {
  const { t } = useI18n()
  return (
    <Alert>
      <CheckCircle2 className="h-4 w-4" />
      <AlertTitle>{t("settings.network.reconciledSucceeded")}</AlertTitle>
      <AlertDescription>
        {t(result.action === "add"
          ? "settings.network.reconciledAddedDescription"
          : "settings.network.reconciledRemovedDescription")}
        <p className="mt-1 font-mono text-xs">{result.canonicalCidr}</p>
      </AlertDescription>
    </Alert>
  )
}

function OutcomeAlert({ result }: { result: PrivateNetworkFederationApplyResult }) {
  const { t } = useI18n()
  const success = result.status === "succeeded"
  const rolledBack = result.status === "rolled_back"
  return (
    <Alert variant={result.status === "failed" ? "destructive" : undefined}>
      {success ? <CheckCircle2 className="h-4 w-4" /> : rolledBack ? <RotateCcw className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
      <AlertTitle>
        {success
          ? t("settings.network.outcomeSucceeded")
          : rolledBack
            ? t("settings.network.outcomeRolledBack")
            : result.status === "candidate_rejected"
              ? t("settings.network.outcomeRejected")
              : t("settings.network.outcomeFailed")}
      </AlertTitle>
      <AlertDescription>
        <p>{result.detail}</p>
        <p className="mt-1 font-mono text-xs">{t("settings.network.operationId")}: {result.operationId}</p>
      </AlertDescription>
    </Alert>
  )
}
