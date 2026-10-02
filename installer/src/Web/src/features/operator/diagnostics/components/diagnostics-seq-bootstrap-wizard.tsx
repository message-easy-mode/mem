import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  CheckCircle2,
  Circle,
  Copy,
  Eye,
  EyeOff,
  KeyRound,
  LoaderCircle,
  RefreshCw,
  Server,
  ShieldCheck,
  XCircle,
} from "lucide-react"
import { useEffect, useMemo, useRef, useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { getMemApiProblemCode, getMemApiProblemDetail } from "@/lib/api-problem"
import {
  executeDiagnosticsSeqBootstrap,
  getDiagnosticsSeqBootstrapOperation,
  getDiagnosticsSeqBootstrapOverview,
  reviewDiagnosticsSeqBootstrap,
} from "../api/diagnostics.api"
import type {
  DiagnosticsSeqBootstrapExecuteRequest,
  DiagnosticsSeqBootstrapOperationResponse,
  DiagnosticsSeqBootstrapReviewResponse,
  DiagnosticsSeqOverviewResponse,
} from "../api/diagnostics.types"

type WizardStage = "closed" | "understand" | "security" | "review" | "deploy" | "finish"

export function DiagnosticsSeqBootstrapWizard({
  seq,
  isOwner,
}: {
  seq: DiagnosticsSeqOverviewResponse
  isOwner: boolean
}) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const [stage, setStage] = useState<WizardStage>("closed")
  const [acceptEula, setAcceptEula] = useState(false)
  const [administratorPassword, setAdministratorPassword] = useState("")
  const [administratorPasswordConfirmation, setAdministratorPasswordConfirmation] = useState("")
  const [privateUiUrl, setPrivateUiUrl] = useState("")
  const [enableEventDelivery, setEnableEventDelivery] = useState(true)
  const [showPassword, setShowPassword] = useState(false)
  const [operationId, setOperationId] = useState<string | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const pendingExecution = useRef<DiagnosticsSeqBootstrapExecuteRequest | null>(null)
  const stepUpVerified = useRef(false)

  const bootstrap = useQuery({
    queryKey: ["diagnostics", "seq", "bootstrap"],
    queryFn: getDiagnosticsSeqBootstrapOverview,
    staleTime: 5_000,
    retry: 0,
  })
  const administratorPasswordRequired =
    bootstrap.data?.administratorSecretState !== "available"

  const review = useMutation({
    mutationFn: reviewDiagnosticsSeqBootstrap,
    onSuccess: (result) => {
      if (!result.ready) return
      if (result.security.administratorPasswordRequired &&
          !validPassword(administratorPassword)) {
        void bootstrap.refetch()
        setStage("security")
        return
      }
      if (result.security.currentAdministratorPasswordRequired &&
          !validCurrentPassword(administratorPassword)) {
        void bootstrap.refetch()
        setStage("security")
        return
      }
      setStage("review")
    },
  })
  const execute = useMutation({
    mutationFn: executeDiagnosticsSeqBootstrap,
    onSuccess: (result) => {
      clearPasswords()
      pendingExecution.current = null
      setOperationId(result.operationId)
      setStage("deploy")
    },
    onError: (error) => {
      if (getMemApiProblemCode(error) === "step_up_required") {
        setStepUpOpen(true)
        return
      }

      clearPasswords()
      pendingExecution.current = null
      setStage("security")
    },
  })
  const operation = useQuery({
    queryKey: ["diagnostics", "seq", "bootstrap", "operation", operationId],
    queryFn: () => getDiagnosticsSeqBootstrapOperation(operationId!),
    enabled: operationId !== null,
    retry: 0,
    refetchInterval: (query) => {
      const value = query.state.data
      return value?.status === "queued" || value?.status === "running" ? 1_000 : false
    },
  })

  useEffect(() => {
    if (operation.data?.status === "succeeded") {
      setStage("finish")
    }
    if (operation.data?.status === "succeeded" || operation.data?.status === "attention") {
      void queryClient.invalidateQueries({ queryKey: ["diagnostics", "seq"] })
      void queryClient.invalidateQueries({ queryKey: ["diagnostics", "seq", "bootstrap"] })
    }
  }, [operation.data?.status, queryClient])

  useEffect(() => () => {
    pendingExecution.current = null
  }, [])

  const passwordProblem = useMemo(() => {
    if (!administratorPasswordRequired) return null
    if (!administratorPassword && !administratorPasswordConfirmation) return null
    if (administratorPassword !== administratorPasswordConfirmation) {
      return t("diagnostics.seq.bootstrap.passwordMismatch")
    }
    if (!validPassword(administratorPassword)) {
      return t("diagnostics.seq.bootstrap.passwordWeak")
    }
    return null
  }, [administratorPassword, administratorPasswordConfirmation, administratorPasswordRequired, t])

  const uiProblem = useMemo(() => {
    if (!privateUiUrl.trim()) return null
    return safePrivateUrl(privateUiUrl) ? null : t("diagnostics.seq.bootstrap.uiInvalid")
  }, [privateUiUrl, t])

  const overview = bootstrap.data
  const setupComplete = overview?.state === "running" ||
    (overview?.state !== "setup-incomplete" &&
      seq.runtime.managed &&
      seq.runtime.present &&
      seq.runtime.usesApprovedRuntime &&
      seq.health.status === "ready")
  if (stage === "closed" && setupComplete) {
    return <SeqSetupComplete seq={seq} />
  }

  const canStart = isOwner && overview?.canStartSetup === true

  const clearPasswords = () => {
    setAdministratorPassword("")
    setAdministratorPasswordConfirmation("")
    setShowPassword(false)
  }

  const close = () => {
    clearPasswords()
    pendingExecution.current = null
    review.reset()
    execute.reset()
    setStage("closed")
  }

  const passwordReady = administratorPasswordRequired
    ? !passwordProblem && validPassword(administratorPassword)
    : validCurrentPassword(administratorPassword)

  const submitReview = () => {
    if (!passwordReady || !acceptEula || uiProblem) return
    execute.reset()
    review.mutate({
      acceptEula,
      privateUiUrl: privateUiUrl.trim() || null,
      enableEventDelivery,
    })
  }

  const submitExecution = () => {
    if (!review.data) return
    const passwordRequired = review.data.security.administratorPasswordRequired
    const currentPasswordRequired = review.data.security.currentAdministratorPasswordRequired
    if ((passwordRequired || currentPasswordRequired) && !passwordReady) return
    const request: DiagnosticsSeqBootstrapExecuteRequest = {
      reviewId: review.data.reviewId,
      administratorPassword: passwordRequired ? administratorPassword : "",
      administratorPasswordConfirmation: passwordRequired
        ? administratorPasswordConfirmation
        : "",
      connectionAdministratorPassword: currentPasswordRequired
        ? administratorPassword
        : "",
    }
    pendingExecution.current = request
    if (seq.capabilities.requiresRecentStepUp === false) {
      clearPasswords()
      execute.mutate(request)
      return
    }

    stepUpVerified.current = false
    setStepUpOpen(true)
  }

  return (
    <section aria-labelledby="seq-bootstrap-title" className="space-y-4">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <h2 id="seq-bootstrap-title" className="flex items-center gap-2 text-lg font-semibold">
                <Server className="h-5 w-5" />
                {t("diagnostics.seq.bootstrap.title")}
              </h2>
              <CardDescription className="mt-1">
                {t("diagnostics.seq.bootstrap.description")}
              </CardDescription>
            </div>
            {stage === "closed" && canStart ? (
              <Button type="button" onClick={() => setStage("understand")}>
                {t("diagnostics.seq.bootstrap.start")}
              </Button>
            ) : null}
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {bootstrap.isLoading ? (
            <p className="text-sm text-muted-foreground">
              {t("diagnostics.seq.bootstrap.loading")}
            </p>
          ) : null}

          {bootstrap.error ? (
            <Alert variant="destructive">
              <XCircle className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.seq.bootstrap.unavailableTitle")}</AlertTitle>
              <AlertDescription>{t("diagnostics.seq.bootstrap.unavailableDescription")}</AlertDescription>
            </Alert>
          ) : null}

          {overview && !overview.setupAvailable && overview.state !== "running" ? (
            <Alert variant="destructive">
              <XCircle className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.seq.bootstrap.blockedTitle")}</AlertTitle>
              <AlertDescription className="space-y-2">
                <p>{t("diagnostics.seq.bootstrap.blockedDescription")}</p>
                {overview.warnings.length > 0 ? (
                  <ul className="list-disc space-y-1 pl-5">
                    {overview.warnings.map((warning) => (
                      <li key={warning}>{bootstrapProblem(warning, t)}</li>
                    ))}
                  </ul>
                ) : null}
              </AlertDescription>
            </Alert>
          ) : null}

          {overview && !isOwner ? (
            <Alert>
              <ShieldCheck className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.seq.bootstrap.ownerOnlyTitle")}</AlertTitle>
              <AlertDescription>{t("diagnostics.seq.bootstrap.ownerOnlyDescription")}</AlertDescription>
            </Alert>
          ) : null}

          {stage === "closed" ? (
            <dl className="grid gap-3 md:grid-cols-3">
              <SetupFact
                title={t("diagnostics.seq.bootstrap.image")}
                value={bootstrapStateLabel(overview?.imageState, t)}
              />
              <SetupFact
                title={t("diagnostics.seq.bootstrap.storage")}
                value={bootstrapStateLabel(overview?.storageState, t)}
              />
              <SetupFact
                title={t("diagnostics.seq.bootstrap.access")}
                value={overview?.uiAuthorityState === "configured"
                  ? t("diagnostics.seq.bootstrap.configured")
                  : t("diagnostics.seq.bootstrap.optional")}
              />
            </dl>
          ) : null}

          {stage === "understand" ? (
            <WizardPanel
              title={t("diagnostics.seq.bootstrap.understandTitle")}
              description={t("diagnostics.seq.bootstrap.understandDescription")}
            >
              <ul className="space-y-2 text-sm text-muted-foreground">
                {bootstrapEffectKeys.map((key) => (
                  <li key={key} className="flex gap-2">
                    <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
                    <span>{t(key)}</span>
                  </li>
                ))}
              </ul>
              <WizardActions onCancel={close} onNext={() => setStage("security")} />
            </WizardPanel>
          ) : null}

          {stage === "security" ? (
            <WizardPanel
              title={t("diagnostics.seq.bootstrap.securityTitle")}
              description={t("diagnostics.seq.bootstrap.securityDescription")}
            >
              <div className="flex items-start gap-3 rounded-lg border p-3">
                <Checkbox
                  id="seq-eula"
                  checked={acceptEula}
                  onCheckedChange={(value) => setAcceptEula(value === true)}
                />
                <div>
                  <Label htmlFor="seq-eula">{t("diagnostics.seq.bootstrap.acceptEula")}</Label>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("diagnostics.seq.bootstrap.eulaExplanation")}
                  </p>
                </div>
              </div>

              {administratorPasswordRequired ? (
                <>
                  <div className="grid gap-4 md:grid-cols-2">
                    <PasswordField
                      id="seq-admin-password"
                      label={t("diagnostics.seq.bootstrap.password")}
                      value={administratorPassword}
                      show={showPassword}
                      onChange={setAdministratorPassword}
                      onToggle={() => setShowPassword((value) => !value)}
                    />
                    <PasswordField
                      id="seq-admin-password-confirmation"
                      label={t("diagnostics.seq.bootstrap.passwordConfirmation")}
                      value={administratorPasswordConfirmation}
                      show={showPassword}
                      onChange={setAdministratorPasswordConfirmation}
                      onToggle={() => setShowPassword((value) => !value)}
                    />
                  </div>
                  {passwordProblem ? (
                    <p className="text-sm text-destructive">{passwordProblem}</p>
                  ) : null}
                </>
              ) : (
                <div className="space-y-4">
                  <Alert>
                    <KeyRound className="h-4 w-4" />
                    <AlertTitle>{t("diagnostics.seq.bootstrap.existingAdministratorTitle")}</AlertTitle>
                    <AlertDescription>
                      {t("diagnostics.seq.bootstrap.existingAdministratorDescription")}
                    </AlertDescription>
                  </Alert>
                  <PasswordField
                    id="seq-current-admin-password"
                    label={t("diagnostics.seq.bootstrap.currentPassword")}
                    value={administratorPassword}
                    show={showPassword}
                    autoComplete="current-password"
                    onChange={setAdministratorPassword}
                    onToggle={() => setShowPassword((value) => !value)}
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("diagnostics.seq.bootstrap.currentPasswordHelp")}
                  </p>
                </div>
              )}

              <div className="flex items-start gap-3 rounded-lg border p-3">
                <Checkbox
                  id="seq-enable-delivery"
                  checked={enableEventDelivery}
                  onCheckedChange={(value) => setEnableEventDelivery(value === true)}
                />
                <div>
                  <Label htmlFor="seq-enable-delivery">
                    {t("diagnostics.seq.bootstrap.enableDelivery")}
                  </Label>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("diagnostics.seq.bootstrap.enableDeliveryDescription")}
                  </p>
                </div>
              </div>

              <div className="space-y-2">
                <Label htmlFor="seq-private-ui">{t("diagnostics.seq.bootstrap.privateUi")}</Label>
                <Input
                  id="seq-private-ui"
                  type="url"
                  value={privateUiUrl}
                  placeholder={t("diagnostics.seq.bootstrap.privateUiPlaceholder")}
                  onChange={(event) => setPrivateUiUrl(event.target.value)}
                />
                <p className="text-xs text-muted-foreground">
                  {t("diagnostics.seq.bootstrap.privateUiOptional")}
                </p>
                {uiProblem ? <p className="text-sm text-destructive">{uiProblem}</p> : null}
              </div>

              {review.error ? (
                <Alert variant="destructive">
                  <AlertTitle>{t("diagnostics.seq.bootstrap.reviewFailedTitle")}</AlertTitle>
                  <AlertDescription>{operationError(review.error, t)}</AlertDescription>
                </Alert>
              ) : null}

              {execute.error && getMemApiProblemCode(execute.error) !== "step_up_required" ? (
                <Alert variant="destructive">
                  <AlertTitle>{t("diagnostics.seq.bootstrap.executeFailedTitle")}</AlertTitle>
                  <AlertDescription>{operationError(execute.error, t)}</AlertDescription>
                </Alert>
              ) : null}

              <div className="flex flex-wrap justify-between gap-2">
                <Button type="button" variant="ghost" onClick={() => setStage("understand")}>
                  {t("common.back")}
                </Button>
                <div className="flex gap-2">
                  <Button type="button" variant="outline" onClick={close}>
                    {t("common.cancel")}
                  </Button>
                  <Button
                    type="button"
                    disabled={!acceptEula || !passwordReady || Boolean(uiProblem) || review.isPending}
                    onClick={submitReview}
                  >
                    {review.isPending ? t("diagnostics.seq.bootstrap.reviewing") : t("diagnostics.seq.bootstrap.review")}
                  </Button>
                </div>
              </div>
            </WizardPanel>
          ) : null}

          {stage === "review" && review.data ? (
            <ReviewPanel
              review={review.data}
              pending={execute.isPending}
              error={execute.error}
              onBack={() => setStage("security")}
              onCancel={close}
              onExecute={submitExecution}
            />
          ) : null}

          {(stage === "deploy" || stage === "finish") && operationId ? (
            <ProgressPanel
              operation={operation.data}
              loading={operation.isLoading}
              error={operation.error}
              onRetry={() => void operation.refetch()}
              onClose={close}
            />
          ) : null}
        </CardContent>
      </Card>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            queueMicrotask(() => {
              if (!stepUpVerified.current) {
                clearPasswords()
                pendingExecution.current = null
              }
              stepUpVerified.current = false
            })
          }
        }}
        onVerified={() => {
          stepUpVerified.current = true
          const request = pendingExecution.current
          clearPasswords()
          if (request) execute.mutate(request)
        }}
      />
    </section>
  )
}

function ReviewPanel({
  review,
  pending,
  error,
  onBack,
  onCancel,
  onExecute,
}: {
  review: DiagnosticsSeqBootstrapReviewResponse
  pending: boolean
  error: unknown
  onBack: () => void
  onCancel: () => void
  onExecute: () => void
}) {
  const { t } = useI18n()
  return (
    <WizardPanel
      title={t("diagnostics.seq.bootstrap.reviewTitle")}
      description={t("diagnostics.seq.bootstrap.reviewDescription")}
    >
      <dl className="grid gap-3 rounded-lg border p-4 text-sm md:grid-cols-2">
        <SetupFact title={t("diagnostics.seq.bootstrap.approvedVersion")} value={review.image.expectedVersion} />
        <SetupFact
          title={t("diagnostics.seq.bootstrap.imageAction")}
          value={review.image.willPullDuringSetup
            ? t("diagnostics.seq.bootstrap.willPull")
            : t("diagnostics.seq.bootstrap.alreadyLocal")}
        />
        <SetupFact title={t("diagnostics.seq.bootstrap.hostPort")} value={String(review.runtime.selectedHostPort)} />
        <SetupFact
          title={t("diagnostics.seq.bootstrap.administratorAuthority")}
          value={review.security.administratorPasswordRequired
            ? t("diagnostics.seq.bootstrap.newAdministratorAuthority")
            : t("diagnostics.seq.bootstrap.existingAdministratorAuthority")}
        />
        <SetupFact
          title={t("diagnostics.seq.bootstrap.publicIngress")}
          value={review.runtime.publishesPublicIngress ? t("common.yes") : t("common.no")}
        />
        <SetupFact
          title={t("diagnostics.seq.bootstrap.eventDelivery")}
          value={review.enableEventDelivery
            ? t("diagnostics.seq.bootstrap.eventDeliveryAfterRestart")
            : t("diagnostics.seq.bootstrap.eventDeliveryDisabled")}
        />
      </dl>
      <ul className="space-y-2 text-sm text-muted-foreground">
        {review.actionCodes.map((code) => (
          <li key={code} className="flex gap-2">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{bootstrapActionLabel(code, t)}</span>
          </li>
        ))}
      </ul>
      {error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("diagnostics.seq.bootstrap.executeFailedTitle")}</AlertTitle>
          <AlertDescription>{operationError(error, t)}</AlertDescription>
        </Alert>
      ) : null}
      <div className="flex flex-wrap justify-between gap-2">
        <Button type="button" variant="ghost" onClick={onBack}>{t("common.back")}</Button>
        <div className="flex gap-2">
          <Button type="button" variant="outline" onClick={onCancel}>{t("common.cancel")}</Button>
          <Button type="button" disabled={!review.ready || pending} onClick={onExecute}>
            {pending ? t("diagnostics.seq.bootstrap.starting") : t("diagnostics.seq.bootstrap.deploy")}
          </Button>
        </div>
      </div>
    </WizardPanel>
  )
}

function ProgressPanel({
  operation,
  loading,
  error,
  onRetry,
  onClose,
}: {
  operation: DiagnosticsSeqBootstrapOperationResponse | undefined
  loading: boolean
  error: unknown
  onRetry: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  if (loading && !operation) {
    return <p className="text-sm text-muted-foreground">{t("diagnostics.seq.bootstrap.progressLoading")}</p>
  }
  if (error && !operation) {
    return (
      <Alert variant="destructive">
        <AlertTitle>{t("diagnostics.seq.bootstrap.progressUnavailableTitle")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <span className="block">{operationError(error, t)}</span>
          <Button type="button" variant="outline" size="sm" onClick={onRetry}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("common.retry")}
          </Button>
        </AlertDescription>
      </Alert>
    )
  }
  if (!operation) return null

  const completed = operation.status === "succeeded"
  const failed = operation.status === "failed"
  const attention = operation.status === "attention"
  return (
    <WizardPanel
      title={completed
        ? t("diagnostics.seq.bootstrap.completedTitle")
        : attention
          ? t("diagnostics.seq.bootstrap.attentionTitle")
          : failed
            ? t("diagnostics.seq.bootstrap.failedTitle")
            : t("diagnostics.seq.bootstrap.progressTitle")}
      description={completed
        ? t("diagnostics.seq.bootstrap.completedDescription")
        : attention
          ? t("diagnostics.seq.bootstrap.attentionDescription")
          : failed
            ? t("diagnostics.seq.bootstrap.failedDescription")
            : t("diagnostics.seq.bootstrap.progressDescription")}
    >
      <div className="space-y-2">
        {operation.checks.map((check) => (
          <div key={check.code} className="flex items-center justify-between gap-3 rounded-lg border p-3 text-sm">
            <span className="flex items-center gap-2">
              <CheckIcon status={check.status} />
              {bootstrapCheckLabel(check.code, t)}
            </span>
            <span className="text-muted-foreground">{bootstrapCheckStatus(check.status, t)}</span>
          </div>
        ))}
      </div>
      <p className="font-mono text-xs text-muted-foreground">{operation.operationId}</p>
      {operation.warningCode ? (
        <Alert variant={failed ? "destructive" : undefined}>
          <AlertTitle>{attention
            ? t("diagnostics.seq.bootstrap.attentionStageTitle")
            : t("diagnostics.seq.bootstrap.failedStageTitle")}</AlertTitle>
          <AlertDescription>{bootstrapProblem(operation.warningCode, t)}</AlertDescription>
        </Alert>
      ) : null}
      {completed || failed || attention ? (
        <div className="flex justify-end">
          <Button type="button" onClick={onClose}>
            {completed ? t("diagnostics.seq.bootstrap.finish") : t("diagnostics.seq.bootstrap.close")}
          </Button>
        </div>
      ) : null}
    </WizardPanel>
  )
}

function WizardPanel({
  title,
  description,
  children,
}: {
  title: string
  description: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-4 rounded-xl border p-4">
      <div>
        <h3 className="font-semibold">{title}</h3>
        <p className="mt-1 text-sm text-muted-foreground">{description}</p>
      </div>
      {children}
    </div>
  )
}

function WizardActions({ onCancel, onNext }: { onCancel: () => void; onNext: () => void }) {
  const { t } = useI18n()
  return (
    <div className="flex justify-end gap-2">
      <Button type="button" variant="outline" onClick={onCancel}>{t("common.cancel")}</Button>
      <Button type="button" onClick={onNext}>{t("common.continue")}</Button>
    </div>
  )
}

function PasswordField({
  id,
  label,
  value,
  show,
  onChange,
  onToggle,
  autoComplete = "new-password",
}: {
  id: string
  label: string
  value: string
  show: boolean
  onChange: (value: string) => void
  onToggle: () => void
  autoComplete?: string
}) {
  const { t } = useI18n()
  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{label}</Label>
      <div className="flex gap-2">
        <Input
          id={id}
          type={show ? "text" : "password"}
          autoComplete={autoComplete}
          value={value}
          maxLength={256}
          onChange={(event) => onChange(event.target.value)}
        />
        <Button type="button" variant="outline" size="icon" onClick={onToggle} aria-label={show ? t("common.hide") : t("common.show")}>
          {show ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
        </Button>
      </div>
    </div>
  )
}

function SeqSetupComplete({ seq }: { seq: DiagnosticsSeqOverviewResponse }) {
  const { t } = useI18n()
  const connected = seq.connection?.verificationState === "verified"
  const restartRequired = connected && seq.delivery.desiredEnabled && seq.delivery.restartRequired
  const deliveryActive = connected && seq.delivery.enabled
  const restart = seq.delivery.restart ?? {
    kind: "unsupported",
    guidanceCode: "restart_supervisor_unknown",
    command: null,
    commandAvailable: false,
  }

  const copyRestartCommand = async () => {
    const command = restart.command
    if (!command || !restart.commandAvailable) return

    try {
      await navigator.clipboard.writeText(command)
    } catch {
      // The server-authored command remains visible when clipboard access is unavailable.
    }
  }

  const statusTitle = !connected
    ? t("diagnostics.seq.bootstrap.postDeployConnectionAttentionTitle")
    : restartRequired
      ? t("diagnostics.seq.bootstrap.postDeployRestartTitle")
      : deliveryActive
        ? t("diagnostics.seq.bootstrap.postDeployDeliveryActiveTitle")
        : t("diagnostics.seq.bootstrap.postDeployConnectedTitle")
  const statusDescription = !connected
    ? t("diagnostics.seq.bootstrap.postDeployConnectionAttentionDescription")
    : restartRequired
      ? t("diagnostics.seq.bootstrap.postDeployRestartDescription")
      : deliveryActive
        ? t("diagnostics.seq.bootstrap.postDeployDeliveryActiveDescription")
        : t("diagnostics.seq.bootstrap.postDeployConnectedDescription")

  return (
    <section aria-labelledby="seq-bootstrap-complete-title">
      <Card>
        <CardHeader>
          <div>
            <h2 id="seq-bootstrap-complete-title" className="flex items-center gap-2 text-lg font-semibold">
              <CheckCircle2 className="h-5 w-5 text-emerald-600" />
              {connected
                ? t("diagnostics.seq.bootstrap.postDeployTitle")
                : t("diagnostics.seq.bootstrap.postDeployPartialTitle")}
            </h2>
            <CardDescription className="mt-1">
              {t("diagnostics.seq.bootstrap.postDeployDescription")}
            </CardDescription>
          </div>
        </CardHeader>
        <CardContent>
          <Alert>
            {connected ? <CheckCircle2 className="h-4 w-4" /> : <KeyRound className="h-4 w-4" />}
            <AlertTitle>{statusTitle}</AlertTitle>
            <AlertDescription className="space-y-3">
              <p>{statusDescription}</p>
              {restartRequired ? (
                <>
                  <p>{restartGuidance(restart.guidanceCode, t)}</p>
                  {restart.commandAvailable && restart.command ? (
                    <div className="flex flex-wrap items-center gap-2 rounded-md border px-3 py-2">
                      <code className="min-w-0 flex-1 overflow-x-auto text-xs">
                        {restart.command}
                      </code>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        onClick={() => void copyRestartCommand()}
                      >
                        <Copy className="mr-2 h-3.5 w-3.5" />
                        {t("diagnostics.seq.delivery.copyRestartCommand")}
                      </Button>
                    </div>
                  ) : null}
                </>
              ) : null}
            </AlertDescription>
          </Alert>
        </CardContent>
      </Card>
    </section>
  )
}

function restartGuidance(
  guidanceCode: string,
  t: Translate,
) {
  const key = `diagnostics.seq.delivery.guidance.${guidanceCode}` as TranslationKey
  return t(key)
}

function SetupFact({ title, value }: { title: string; value: string }) {
  return (
    <div className="rounded-lg border p-3">
      <dt className="text-xs text-muted-foreground">{title}</dt>
      <dd className="mt-1 font-medium">{value}</dd>
    </div>
  )
}

function CheckIcon({ status }: { status: string }) {
  if (status === "passed") return <CheckCircle2 className="h-4 w-4 text-emerald-600" />
  if (status === "failed") return <XCircle className="h-4 w-4 text-destructive" />
  if (status === "running") return <LoaderCircle className="h-4 w-4 animate-spin" />
  return <Circle className="h-4 w-4 text-muted-foreground" />
}

type Translate = ReturnType<typeof useI18n>["t"]

const bootstrapEffectKeys: readonly TranslationKey[] = [
  "diagnostics.seq.bootstrap.effect.approvedImage",
  "diagnostics.seq.bootstrap.effect.privateRuntime",
  "diagnostics.seq.bootstrap.effect.persistentData",
  "diagnostics.seq.bootstrap.effect.noPublicIngress",
  "diagnostics.seq.bootstrap.effect.dedicatedIngestion",
  "diagnostics.seq.bootstrap.effect.deliveryChoice",
  "diagnostics.seq.bootstrap.effect.nativeDiagnostics",
]

function validPassword(value: string) {
  return value.length >= 14 &&
    value.length <= 256 &&
    !/[\0\r\n]/.test(value) &&
    new Set(value).size >= 4
}

function validCurrentPassword(value: string) {
  return value.trim().length > 0 && value.length <= 256
}

function safePrivateUrl(value: string) {
  try {
    const url = new URL(value)
    return (url.protocol === "http:" || url.protocol === "https:") &&
      !url.username && !url.password && !url.search && !url.hash
  } catch {
    return false
  }
}

function bootstrapStateLabel(value: string | undefined, t: Translate) {
  switch (value) {
    case "local": return t("diagnostics.seq.bootstrap.state.local")
    case "preparable": return t("diagnostics.seq.bootstrap.state.preparable")
    case "ready": return t("diagnostics.seq.bootstrap.state.ready")
    case "ready-to-create": return t("diagnostics.seq.bootstrap.state.readyToCreate")
    case "initialized": return t("diagnostics.seq.bootstrap.state.initialized")
    case "not-configured": return t("diagnostics.seq.bootstrap.state.notConfigured")
    case "invalid": return t("diagnostics.seq.bootstrap.state.invalid")
    default: return t("diagnostics.seq.bootstrap.state.unknown")
  }
}

function bootstrapActionLabel(code: string, t: Translate) {
  const known: Readonly<Record<string, TranslationKey>> = {
    prepare_approved_image: "diagnostics.seq.bootstrap.action.prepareImage",
    hash_administrator_password_securely: "diagnostics.seq.bootstrap.action.hashPassword",
    reuse_existing_administrator_authority: "diagnostics.seq.bootstrap.action.reuseAdministrator",
    prepare_server_owned_storage: "diagnostics.seq.bootstrap.action.prepareStorage",
    create_mem_managed_container: "diagnostics.seq.bootstrap.action.createContainer",
    attach_mem_gateway_network: "diagnostics.seq.bootstrap.action.attachNetwork",
    do_not_create_public_ingress: "diagnostics.seq.bootstrap.action.noPublicIngress",
    preserve_seq_data_directory: "diagnostics.seq.bootstrap.action.preserveData",
    verify_docker_and_seq_health: "diagnostics.seq.bootstrap.action.verifyHealth",
    provision_mem_ingestion_credential: "diagnostics.seq.bootstrap.action.provisionIngestion",
    verify_mem_ingestion: "diagnostics.seq.bootstrap.action.verifyIngestion",
    prepare_event_delivery_after_restart: "diagnostics.seq.bootstrap.action.prepareDelivery",
    leave_event_delivery_disabled: "diagnostics.seq.bootstrap.action.leaveDeliveryDisabled",
  }
  const key = known[code]
  return key ? t(key) : t("diagnostics.seq.bootstrap.action.unknown")
}

function bootstrapCheckLabel(code: string, t: Translate) {
  const known: Readonly<Record<string, TranslationKey>> = {
    approved_image: "diagnostics.seq.bootstrap.check.image",
    password_hash: "diagnostics.seq.bootstrap.check.passwordHash",
    administrator_secret: "diagnostics.seq.bootstrap.check.adminSecret",
    storage: "diagnostics.seq.bootstrap.check.storage",
    bootstrap_state: "diagnostics.seq.bootstrap.check.state",
    docker_runtime: "diagnostics.seq.bootstrap.check.runtime",
    seq_health: "diagnostics.seq.bootstrap.check.health",
    ingestion_connection: "diagnostics.seq.bootstrap.check.ingestionConnection",
    event_delivery: "diagnostics.seq.bootstrap.check.eventDelivery",
  }
  const key = known[code]
  return key ? t(key) : code
}

function bootstrapCheckStatus(status: string, t: Translate) {
  switch (status) {
    case "passed": return t("diagnostics.seq.bootstrap.checkStatus.passed")
    case "running": return t("diagnostics.seq.bootstrap.checkStatus.running")
    case "failed": return t("diagnostics.seq.bootstrap.checkStatus.failed")
    case "skipped": return t("diagnostics.seq.bootstrap.checkStatus.skipped")
    default: return t("diagnostics.seq.bootstrap.checkStatus.notStarted")
  }
}

function bootstrapProblem(code: string, t: Translate) {
  const known: Readonly<Record<string, TranslationKey>> = {
    seq_bootstrap_review_not_found: "diagnostics.seq.bootstrap.problem.reviewNotFound",
    seq_bootstrap_review_expired: "diagnostics.seq.bootstrap.problem.reviewExpired",
    seq_bootstrap_review_stale: "diagnostics.seq.bootstrap.problem.reviewStale",
    seq_administrator_password_mismatch: "diagnostics.seq.bootstrap.problem.passwordMismatch",
    seq_administrator_password_weak: "diagnostics.seq.bootstrap.problem.passwordWeak",
    seq_administrator_password_required: "diagnostics.seq.bootstrap.problem.passwordRequired",
    seq_administrator_password_not_required: "diagnostics.seq.bootstrap.problem.passwordNotRequired",
    seq_connection_administrator_password_required: "diagnostics.seq.bootstrap.problem.connectionPasswordRequired",
    seq_connection_administrator_password_not_required: "diagnostics.seq.bootstrap.problem.connectionPasswordNotRequired",
    seq_initialized_data_requires_existing_administrator_secret: "diagnostics.seq.bootstrap.problem.initializedAuthorityMissing",
    seq_setup_image_preparation_failed: "diagnostics.seq.bootstrap.problem.imagePreparation",
    seq_password_hash_failed: "diagnostics.seq.bootstrap.problem.passwordHash",
    seq_password_hash_timeout: "diagnostics.seq.bootstrap.problem.passwordHashTimeout",
    seq_secret_write_failed: "diagnostics.seq.bootstrap.problem.secretWrite",
    seq_data_path_unexplained: "diagnostics.seq.bootstrap.problem.storageConflict",
    seq_unmanaged_container: "diagnostics.seq.bootstrap.problem.unmanaged",
    seq_health_verification_failed: "diagnostics.seq.bootstrap.problem.health",
    seq_connection_verification_failed: "diagnostics.seq.bootstrap.problem.connection",
    seq_administrator_password_invalid: "diagnostics.seq.bootstrap.problem.connectionPasswordInvalid",
    seq_verification_event_not_found: "diagnostics.seq.bootstrap.problem.connection",
    seq_verification_event_rejected: "diagnostics.seq.bootstrap.problem.connection",
    seq_delivery_state_write_failed: "diagnostics.seq.bootstrap.problem.delivery",
    seq_bootstrap_completion_failed: "diagnostics.seq.bootstrap.problem.completion",
    "diagnostics.seq_health_failed": "diagnostics.seq.bootstrap.problem.health",
    "diagnostics.seq_probe_timeout": "diagnostics.seq.bootstrap.problem.healthTimeout",
    seq_operation_in_progress: "diagnostics.seq.bootstrap.problem.operationInProgress",
    seq_bootstrap_queue_unavailable: "diagnostics.seq.bootstrap.problem.queueUnavailable",
    seq_bootstrap_request_too_large: "diagnostics.seq.bootstrap.problem.requestTooLarge",
    seq_bootstrap_request_invalid: "diagnostics.seq.bootstrap.problem.requestInvalid",
    seq_setup_configuration_invalid: "diagnostics.seq.bootstrap.problem.configurationInvalid",
    seq_data_path_invalid: "diagnostics.seq.bootstrap.problem.storageInvalid",
    seq_data_path_unavailable: "diagnostics.seq.bootstrap.problem.storageUnavailable",
    seq_bootstrap_interrupted: "diagnostics.seq.bootstrap.problem.interrupted",
    seq_bootstrap_failed: "diagnostics.seq.bootstrap.problem.failed",
  }
  const key = known[code]
  return key ? t(key) : t("diagnostics.seq.bootstrap.problem.failed")
}

function operationError(error: unknown, t: Translate) {
  const code = getMemApiProblemCode(error)
  return code
    ? bootstrapProblem(code, t)
    : getMemApiProblemDetail(error, t("diagnostics.seq.bootstrap.problem.failed"))
}
