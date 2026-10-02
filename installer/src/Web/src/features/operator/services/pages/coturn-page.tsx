import { useEffect, useState, type ReactNode } from "react"
import { useQuery } from "@tanstack/react-query"
import { Link } from "react-router-dom"
import {
  AlertTriangle,
  ArrowLeft,
  CheckCircle2,
  ChevronDown,
  ExternalLink,
  FileText,
  Info,
  PlayCircle,
  RadioTower,
  RefreshCw,
  ShieldCheck,
  Wrench,
  XCircle,
} from "lucide-react"

import type { I18nContextValue } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { getDiagnosticsPortainerOverview } from "@/features/operator/diagnostics/api/diagnostics.api"
import { CoturnApiError, isCoturnStepUpRequired } from "@/features/operator/services/api/coturn.api"
import type {
  CoturnCheckItem,
  CoturnCheckResponse,
  CoturnCheckStatus,
  CoturnDockerRuntimeEvidence,
  CoturnCheckFreshness,
  CoturnLogsResponse,
  CoturnMaintenanceAction,
  CoturnMaintenanceRequest,
  CoturnRuntimeResponse,
  CoturnStartupSupervisionResponse,
} from "@/features/operator/services/api/coturn.api"
import {
  useCheckCoturn,
  useCoturn,
  useCoturnLatestCheck,
  useCoturnActiveMaintenance,
  useCoturnStartupSupervision,
  useCoturnLogs,
  useCoturnInstallOperation,
  useCoturnMaintenanceOperation,
  useInstallCoturn,
  useMaintainCoturn,
} from "@/features/operator/services/hooks/use-coturn"
import { useRuntimeContext } from "@/features/runtime-context/use-runtime-context"

type Translate = I18nContextValue["t"]
type CoturnConfirmationTarget = "install" | "apply" | CoturnMaintenanceAction

const coturnPortainerHandoffUrl =
  "/api/operator/diagnostics/portainer/resources/platform-service/coturn?service=coturn"

export function CoturnPage() {
  const { t } = useI18n()
  const coturn = useCoturn()
  const installCoturn = useInstallCoturn()
  const maintainCoturn = useMaintainCoturn()
  const checkCoturn = useCheckCoturn()
  const latestCheck = useCoturnLatestCheck()
  const activeMaintenance = useCoturnActiveMaintenance()
  const startupSupervision = useCoturnStartupSupervision()
  const runtimeContext = useRuntimeContext()
  const [logsOpen, setLogsOpen] = useState(false)
  const [hostNativeHelpOpen, setHostNativeHelpOpen] = useState(false)
  const logs = useCoturnLogs(logsOpen)
  const [externalIp, setExternalIp] = useState("")
  const [externalIpDirty, setExternalIpDirty] = useState(false)
  const [installOperationId, setInstallOperationId] = useState<string | null>(null)
  const [maintenanceOperationId, setMaintenanceOperationId] = useState<string | null>(null)
  const [maintenanceAction, setMaintenanceAction] = useState<CoturnMaintenanceAction | null>(null)
  const [pendingMaintenanceRequest, setPendingMaintenanceRequest] = useState<CoturnMaintenanceRequest | null>(null)
  const [confirmationTarget, setConfirmationTarget] = useState<CoturnConfirmationTarget | null>(null)
  const [stepUpTarget, setStepUpTarget] = useState<"install" | "maintenance" | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const installOperation = useCoturnInstallOperation(installOperationId)
  const maintenanceOperation = useCoturnMaintenanceOperation(maintenanceOperationId)

  const data = coturn.data
  const protectedEvidenceRestricted = data?.protectedEvidenceAccess === "restricted"
  const hostNativeDevelopment =
    runtimeContext.data?.runtimeMode === "local-development"
  const showHostNativeRecovery = hostNativeDevelopment
  const portainer = useQuery({
    queryKey: ["diagnostics", "portainer", "coturn-host-native-handoff"],
    queryFn: getDiagnosticsPortainerOverview,
    enabled: showHostNativeRecovery,
    staleTime: 10_000,
    retry: 0,
  })
  const canOpenPortainer = Boolean(
    portainer.data?.available &&
      portainer.data.capabilities.canOpenHome,
  )
  const mutationResultIsNewer = isCheckResultNewer(
    checkCoturn.data,
    latestCheck.data?.result,
  )
  const latestResult = mutationResultIsNewer
    ? checkCoturn.data ?? null
    : latestCheck.data?.result ?? checkCoturn.data ?? null
  const latestFreshness: CoturnCheckFreshness = mutationResultIsNewer
    ? "fresh"
    : latestCheck.isError
      ? checkCoturn.data
        ? "fresh"
        : "unavailable"
      : latestCheck.data?.freshness ?? (checkCoturn.data ? "fresh" : "not-checked")
  const functionalHealth = describeFunctionalHealth(
    latestResult,
    latestFreshness,
    t,
  )
  const needsInstallationBoundary = Boolean(
    data &&
      ((!data.containerExists && !data.secretPresent) || !data.resolvedImageId),
  )
  const externalIpChanged = Boolean(
    data && externalIpDirty && !sameExternalIp(externalIp, data.externalIp),
  )
  const offerApplyConfiguration = Boolean(
    data?.containerExists && !needsInstallationBoundary && externalIpChanged,
  )
  const canApplyConfiguration = Boolean(
    data?.ownershipVerified && data.protectedEvidenceAccess === "available" && data.resolvedImageId,
  )
  const restartPolicyOnlyDrift = Boolean(
    data &&
      data.runtimeDrift.length === 1 &&
      data.runtimeDrift[0] === "restart-policy",
  )
  const repairRequired = Boolean(
    data &&
      !needsInstallationBoundary &&
      data.protectedEvidenceAccess === "available" &&
      !restartPolicyOnlyDrift &&
      (
        !data.containerExists ||
        !data.running ||
        !data.imageApproved ||
        data.operatorStatus === "repair-required" ||
        data.domainDriftDetected ||
        !data.secretPresent ||
        !data.secretFilePermissionsApplied ||
        !data.relayPortsPublished ||
        !data.securityPolicyApplied ||
        data.runtimeDrift.length > 0
      ),
  )
  const maintenanceRunning =
    (Boolean(maintenanceOperationId) && !maintenanceOperation.data?.terminal) ||
    activeMaintenance.data?.active === true
  const startupSupervisionRunning =
    startupSupervision.data?.status === "waiting" ||
    startupSupervision.data?.status === "recovering"
  const canRestartVerify = Boolean(
    data &&
      data.containerExists &&
      data.running &&
      data.ownershipVerified &&
      data.protectedEvidenceAccess === "available" &&
      data.imageApproved &&
      data.secretPresent &&
      data.secretFilePermissionsApplied &&
      !data.domainDriftDetected &&
      data.relayPortsPublished &&
      data.securityPolicyApplied &&
      ((data.runtimeExact && data.operatorStatus === "runtime-ready") ||
        restartPolicyOnlyDrift),
  )
  const busy =
    coturn.isFetching ||
    installCoturn.isPending ||
    maintainCoturn.isPending ||
    (Boolean(installOperationId) && !installOperation.data?.terminal) ||
    maintenanceRunning ||
    startupSupervisionRunning ||
    checkCoturn.isPending

  useEffect(() => {
    const active = activeMaintenance.data?.operation
    if (!maintenanceOperationId && active && !active.terminal) {
      setMaintenanceOperationId(active.operationId)
      setMaintenanceAction(active.action)
    }
  }, [activeMaintenance.data?.operation, maintenanceOperationId])

  useEffect(() => {
    if (!externalIpDirty) {
      setExternalIp(data?.externalIp ?? "")
    } else if (data && sameExternalIp(externalIp, data.externalIp)) {
      // Mark applied only when runtime inspection agrees, not when the POST
      // is accepted. Preserve a pending edit across polling and step-up.
      setExternalIpDirty(false)
    }
  }, [data, externalIp, externalIpDirty])

  const submitInstall = () => {
    installCoturn.mutate(
      { externalIp: externalIp.trim() || null },
      {
        onSuccess: (accepted) => {
          setInstallOperationId(accepted.operationId)
          setConfirmationTarget(null)
          setStepUpTarget(null)
          setStepUpOpen(false)
        },
        onError: (error) => {
          if (isCoturnStepUpRequired(error)) {
            setConfirmationTarget(null)
            setStepUpTarget("install")
            setStepUpOpen(true)
          }
        },
      },
    )
  }

  const submitMaintenance = (request: CoturnMaintenanceRequest) => {
    maintainCoturn.mutate(request, {
      onSuccess: (accepted) => {
        setMaintenanceOperationId(accepted.operationId)
        setMaintenanceAction(accepted.action)
        setPendingMaintenanceRequest(request)
        setConfirmationTarget(null)
        setStepUpTarget(null)
        setStepUpOpen(false)
      },
      onError: (error) => {
        if (isCoturnStepUpRequired(error)) {
          setPendingMaintenanceRequest(request)
          setConfirmationTarget(null)
          setStepUpTarget("maintenance")
          setStepUpOpen(true)
        }
      },
    })
  }

  const handleInstall = () => {
    installCoturn.reset()
    setConfirmationTarget("install")
  }

  const handleMaintenance = (target: Exclude<CoturnConfirmationTarget, "install">) => {
    maintainCoturn.reset()
    setPendingMaintenanceRequest(null)
    setMaintenanceAction(target === "apply" ? "repair" : target)
    setConfirmationTarget(target)
  }

  const confirmCoturnAction = () => {
    if (confirmationTarget === "install") {
      submitInstall()
      return
    }

    if (!confirmationTarget) return

    // Applying network settings uses the existing protected repair/recreate
    // boundary (including recent step-up). Plain restart never applies an edit.
    const action = confirmationTarget === "apply" ? "repair" : confirmationTarget
    const requestedIp = action === "repair" ? externalIp.trim() || null : null
    const request: CoturnMaintenanceRequest =
      pendingMaintenanceRequest?.action === action &&
      sameExternalIp(pendingMaintenanceRequest.externalIp, requestedIp)
        ? pendingMaintenanceRequest
        : {
            action,
            externalIp: requestedIp,
            idempotencyKey: createMaintenanceIdempotencyKey(),
          }
    setPendingMaintenanceRequest(request)
    setMaintenanceAction(action)
    submitMaintenance(request)
  }

  const installError = confirmationTarget === "install"
    ? null
    : installCoturn.error && !isCoturnStepUpRequired(installCoturn.error)
      ? friendlyCoturnError(installCoturn.error, t)
      : installOperation.data?.terminal && !installOperation.data.succeeded
        ? installOperation.data.lastError ?? t("services.coturn.error.generic")
        : null
  const maintenanceError = confirmationTarget === "restart-verify" || confirmationTarget === "repair" || confirmationTarget === "apply"
    ? null
    : maintainCoturn.error && !isCoturnStepUpRequired(maintainCoturn.error)
      ? friendlyCoturnError(maintainCoturn.error, t)
      : maintenanceOperation.data?.terminal && !maintenanceOperation.data.succeeded
        ? maintenanceOperation.data.lastError ?? t("services.coturn.error.generic")
        : null
  const confirmation = describeCoturnConfirmation(
    confirmationTarget,
    data?.relayMinPort ?? 49160,
    data?.relayMaxPort ?? 49200,
    t,
    data?.externalIp ?? null,
    externalIp.trim() || null,
  )
  const confirmationPending = confirmationTarget === "install"
    ? installCoturn.isPending
    : maintainCoturn.isPending
  const confirmationError = confirmationTarget === "install"
    ? installCoturn.error && !isCoturnStepUpRequired(installCoturn.error)
      ? friendlyCoturnError(installCoturn.error, t)
      : null
    : maintainCoturn.error && !isCoturnStepUpRequired(maintainCoturn.error)
      ? friendlyCoturnError(maintainCoturn.error, t)
      : null

  return (
    <div className="space-y-6">
      <PageHeader
        busy={busy}
        onRefresh={() => void coturn.refetch()}
        t={t}
      />

      {coturn.error && (
        <ErrorAlert
          title={t("services.coturn.error.inspectTitle")}
          description={t("services.coturn.error.generic")}
          technicalDetail={coturn.error.message}
          t={t}
        />
      )}

      {installError && (
        <ErrorAlert
          title={t("services.coturn.error.installTitle")}
          description={installError}
          technicalDetail={installOperation.data?.lastError ?? installCoturn.error?.message ?? null}
          t={t}
        />
      )}

      {maintenanceError && (
        <ErrorAlert
          title={t("services.coturn.error.maintenanceTitle")}
          description={maintenanceError}
          technicalDetail={maintenanceOperation.data?.lastError ?? maintainCoturn.error?.message ?? null}
          t={t}
        />
      )}

      {checkCoturn.error && (
        <ErrorAlert
          title={t("services.coturn.error.checkTitle")}
          description={t("services.coturn.error.generic")}
          technicalDetail={checkCoturn.error.message}
          t={t}
        />
      )}

      {installOperation.data && !installOperation.data.terminal && (
        <Alert className="border-sky-500/20 bg-sky-500/10">
          <RefreshCw className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("services.coturn.install.runningTitle")}</AlertTitle>
          <AlertDescription>
            {t("services.coturn.install.runningDescription", {
              step: installOperation.data.currentStep ?? "queued",
            })}
          </AlertDescription>
        </Alert>
      )}

      {installOperation.data?.terminal && installOperation.data.succeeded && (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("services.coturn.install.succeededTitle")}</AlertTitle>
          <AlertDescription>{t("services.coturn.install.succeededDescription")}</AlertDescription>
        </Alert>
      )}

      {maintenanceOperation.data && !maintenanceOperation.data.terminal && (
        <Alert className="border-sky-500/20 bg-sky-500/10">
          <RefreshCw className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("services.coturn.maintenance.runningTitle")}</AlertTitle>
          <AlertDescription>
            {t("services.coturn.maintenance.runningDescription", {
              step: localizeMaintenanceStep(maintenanceOperation.data.currentStep, t),
            })}
          </AlertDescription>
        </Alert>
      )}

      {maintenanceOperation.data?.terminal && maintenanceOperation.data.succeeded && (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("services.coturn.maintenance.succeededTitle")}</AlertTitle>
          <AlertDescription>
            {maintenanceAction === "repair"
              ? t("services.coturn.maintenance.repairSucceededDescription")
              : t("services.coturn.maintenance.restartSucceededDescription")}
          </AlertDescription>
        </Alert>
      )}

      {shouldShowStartupSupervisionAlert(
        startupSupervision.data,
        data,
        latestResult,
        latestFreshness,
      ) && startupSupervision.data ? (
        <StartupSupervisionAlert
          supervision={startupSupervision.data}
          t={t}
        />
      ) : null}

      {protectedEvidenceRestricted && (
        <Alert className="border-sky-500/20 bg-sky-500/10">
          <Info className="h-4 w-4" />
          <AlertTitle>{t("services.coturn.protectedEvidence.restricted.title")}</AlertTitle>
          <AlertDescription>
            {t("services.coturn.protectedEvidence.restricted.description")}
          </AlertDescription>
        </Alert>
      )}

      {data?.warnings && data.warnings.length > 0 && (
        <Alert className="border-amber-500/20 bg-amber-500/10">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("services.coturn.warnings.title")}</AlertTitle>
          <AlertDescription>
            <div className="space-y-1">
              {data.warnings.map((warning, index) => (
                <div key={`${warning}-${index}`}>{warning}</div>
              ))}
            </div>
          </AlertDescription>
        </Alert>
      )}

      <div className="grid gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2">
                  <RadioTower className="h-5 w-5" />
                  {t("services.coturn.overview.title")}
                </CardTitle>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("services.coturn.overview.description")}
                </p>
              </div>
            </div>
          </CardHeader>

          <CardContent className="space-y-5">
            {coturn.isLoading && !data ? (
              <EmptyBox>{t("services.coturn.loading")}</EmptyBox>
            ) : !data ? (
              <EmptyBox>{t("services.coturn.unavailable")}</EmptyBox>
            ) : (
              <>
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
                  <StateCard
                    label={t("services.coturn.serviceStatus")}
                    value={localizeOperatorStatus(data.operatorStatus, t)}
                    tone={operatorStatusTone(data.operatorStatus)}
                  />
                  <StateCard
                    label={t("services.coturn.containerState")}
                    value={localizeContainerState(data.containerState, t)}
                    tone={data.running && data.ownershipVerified ? "positive" : "warning"}
                  />
                  <StateCard
                    label={t("services.coturn.turnReadiness")}
                    value={localizeReadiness(data.readiness, t)}
                    tone={
                      data.runtimeExact && data.readiness === "ready"
                        ? "positive"
                        : data.readiness === "verification-limited"
                          ? "neutral"
                          : "warning"
                    }
                  />
                  <StateCard
                    label={t("services.coturn.functionalHealth.label")}
                    value={functionalHealth.label}
                    tone={functionalHealth.tone}
                  />
                </div>

                <div className="rounded-xl border border-border bg-background/40 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-sm font-medium">
                      {t("services.coturn.functionalHealth.evidenceTitle")}
                    </div>
                    {latestResult ? (
                      <span className="text-xs text-muted-foreground">
                        {t("services.coturn.functionalHealth.checkedAt", {
                          value: formatTimestamp(latestResult.checkedAtUtc),
                        })}
                      </span>
                    ) : null}
                  </div>
                  <p className="mt-2 text-sm text-muted-foreground">
                    {functionalHealth.description}
                  </p>
                </div>

                <div className="grid gap-4 text-sm md:grid-cols-2 xl:grid-cols-4">
                  <InfoTile label={t("services.coturn.field.container")} value={data.containerName} />
                  <InfoTile label={t("services.coturn.field.image")} value={data.image} breakAll />
                  <InfoTile label={t("services.coturn.field.publicHost")} value={data.publicHost} />
                  <InfoTile label={t("services.coturn.field.realm")} value={data.realm} />
                  <InfoTile label={t("services.coturn.field.turnPort")} value={String(data.turnPort)} />
                  <InfoTile
                    label={t("services.coturn.field.relayRange")}
                    value={`${data.relayMinPort}-${data.relayMaxPort}/udp`}
                  />
                  <InfoTile
                    label={t("services.coturn.field.relayPublished")}
                    value={data.relayPortsPublished ? t("services.coturn.yes") : t("services.coturn.no")}
                  />
                  <InfoTile
                    label={t("services.coturn.field.externalIp")}
                    value={data.externalIp ?? t("services.coturn.externalIp.automatic")}
                  />
                </div>

                <div className="rounded-xl border border-sky-500/20 bg-sky-500/10 p-4 text-sm text-sky-100">
                  {t("services.coturn.directExposure")}
                </div>

                <details className="rounded-xl border border-border bg-background/40 p-4">
                  <summary className="flex cursor-pointer list-none items-center gap-2 text-sm font-medium">
                    <ChevronDown className="h-4 w-4" />
                    {t("services.coturn.technicalDetails")}
                  </summary>

                  <div className="mt-4 space-y-4">
                    <Section title={t("services.coturn.turnUris")}>
                      <div className="space-y-2">
                        {data.turnUris.map((uri) => (
                          <CodeLine key={uri}>{uri}</CodeLine>
                        ))}
                      </div>
                    </Section>

                    <Section title={t("services.coturn.publishedPorts")}>
                      {data.publishedPorts.length > 0 ? (
                        <div className="grid gap-2 text-sm md:grid-cols-2">
                          {data.publishedPorts.map((port) => (
                            <CodeLine key={port}>{port}</CodeLine>
                          ))}
                        </div>
                      ) : (
                        <div className="text-sm text-muted-foreground">
                          {t("services.coturn.noPublishedPorts")}
                        </div>
                      )}
                    </Section>

                    <Section title={t("services.coturn.firewallPorts")}>
                      <div className="grid gap-2 text-sm md:grid-cols-2">
                        {data.requiredProductionFirewallPorts.map((port) => (
                          <CodeLine key={port}>{port}</CodeLine>
                        ))}
                      </div>
                    </Section>

                    <Section title={t("services.coturn.runtime.title")}>
                      <p className="mb-3 text-xs text-muted-foreground">
                        {t("services.coturn.runtime.description")}
                      </p>
                      {data.dockerRuntime ? (
                        <DockerRuntimeEvidenceView evidence={data.dockerRuntime} t={t} />
                      ) : (
                        <div className="text-sm text-muted-foreground">
                          {t("services.coturn.runtime.unavailable")}
                        </div>
                      )}
                      <div className="mt-4">
                        <div className="mb-2 text-xs uppercase tracking-wide text-muted-foreground">
                          {t("services.coturn.runtime.driftTitle")}
                        </div>
                        {data.runtimeDrift.length === 0 ? (
                          <div className="text-sm text-muted-foreground">
                            {t("services.coturn.runtime.noDrift")}
                          </div>
                        ) : (
                          <div className="flex flex-wrap gap-2">
                            {data.runtimeDrift.map((code) => (
                              <span
                                key={code}
                                className="rounded-full border border-amber-500/20 bg-amber-500/10 px-2.5 py-1 text-xs text-amber-300"
                              >
                                {localizeRuntimeDrift(code, t)}
                              </span>
                            ))}
                          </div>
                        )}
                      </div>
                    </Section>

                    <div className="grid gap-4 text-sm md:grid-cols-2 xl:grid-cols-4">
                      <InfoTile
                        label={t("services.coturn.field.approvedImage")}
                        value={data.approvedImageReference}
                        breakAll
                      />
                      <InfoTile
                        label={t("services.coturn.field.imageApproved")}
                        value={data.imageApproved ? t("services.coturn.yes") : t("services.coturn.no")}
                      />
                      <InfoTile
                        label={t("services.coturn.field.protectedEvidence")}
                        value={localizeProtectedEvidenceAccess(data.protectedEvidenceAccess, t)}
                      />
                      <InfoTile
                        label={t("services.coturn.field.securityPolicy")}
                        value={
                          protectedEvidenceRestricted
                            ? t("services.coturn.protectedEvidence.restricted.value")
                            : data.securityPolicyApplied
                              ? data.securityPolicyVersion
                              : t("services.coturn.notApplied")
                        }
                      />
                      <InfoTile
                        label={t("services.coturn.field.secret")}
                        value={
                          protectedEvidenceRestricted
                            ? t("services.coturn.protectedEvidence.restricted.value")
                            : data.secretPresent
                              ? t("services.coturn.protectedAvailable")
                              : t("services.coturn.unavailableValue")
                        }
                      />
                      <InfoTile
                        label={t("services.coturn.field.expectedDomain")}
                        value={data.expectedBaseDomain}
                      />
                      <InfoTile
                        label={t("services.coturn.field.configuredDomain")}
                        value={data.configuredBaseDomain ?? t("services.coturn.notSet")}
                      />
                      <InfoTile
                        label={t("services.coturn.field.domainDrift")}
                        value={data.domainDriftDetected ? t("services.coturn.yes") : t("services.coturn.no")}
                      />
                      <InfoTile
                        label={t("services.coturn.field.containerId")}
                        value={data.containerId ?? t("services.coturn.notCreated")}
                        breakAll
                      />
                    </div>
                  </div>
                </details>
              </>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t("services.coturn.actions.title")}</CardTitle>
          </CardHeader>

          <CardContent className="space-y-4">
            <label className="block space-y-2">
              <span className="text-sm font-medium">
                {t("services.coturn.externalIp.label")}
              </span>
              <input
                value={externalIp}
                onChange={(event) => {
                  setExternalIp(event.target.value)
                  setExternalIpDirty(true)
                }}
                placeholder={t("services.coturn.externalIp.placeholder")}
                className="h-10 w-full rounded-md border border-input bg-background px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring"
                disabled={busy || !data || protectedEvidenceRestricted || confirmationTarget !== null || stepUpOpen}
                aria-describedby="coturn-external-ip-help coturn-action-authority-help"
                autoComplete="off"
                spellCheck={false}
              />
              <span id="coturn-external-ip-help" className="block text-xs text-muted-foreground">
                {t("services.coturn.externalIp.help")}
              </span>
            </label>

            {externalIpChanged && (
              <p role="status" className="text-sm text-amber-600 dark:text-amber-300">
                {t("services.coturn.externalIp.pending")}
              </p>
            )}

            {offerApplyConfiguration && (
              <Button
                className="w-full"
                onClick={() => handleMaintenance("apply")}
                disabled={busy || !canApplyConfiguration}
                aria-describedby="coturn-action-authority-help"
              >
                <Wrench className="mr-2 h-4 w-4" />
                {maintainCoturn.isPending
                  ? t("services.coturn.action.applying")
                  : t("services.coturn.action.applyVerify")}
              </Button>
            )}

            {needsInstallationBoundary && (
              <Button
                className="w-full"
                onClick={handleInstall}
                disabled={busy || !data || protectedEvidenceRestricted}
                aria-describedby="coturn-action-authority-help"
              >
                <Wrench className="mr-2 h-4 w-4" />
                {installCoturn.isPending || (Boolean(installOperationId) && !installOperation.data?.terminal)
                  ? t("services.coturn.action.installing")
                  : t("services.coturn.action.install")}
              </Button>
            )}

            {repairRequired && !offerApplyConfiguration && (
              <Button
                variant="outline"
                className="w-full"
                onClick={() => handleMaintenance("repair")}
                disabled={busy || !data || protectedEvidenceRestricted}
                aria-describedby="coturn-action-authority-help"
              >
                <Wrench className="mr-2 h-4 w-4" />
                {maintainCoturn.isPending && maintenanceAction === "repair"
                  ? t("services.coturn.action.repairing")
                  : t("services.coturn.action.repair")}
              </Button>
            )}

            {data?.containerExists && !offerApplyConfiguration && (
              <Button
                variant="outline"
                className="w-full"
                onClick={() => handleMaintenance("restart-verify")}
                disabled={busy || !canRestartVerify || protectedEvidenceRestricted}
                aria-describedby="coturn-action-authority-help"
              >
                <RefreshCw className="mr-2 h-4 w-4" />
                {maintainCoturn.isPending && maintenanceAction === "restart-verify"
                  ? t("services.coturn.action.restarting")
                  : t("services.coturn.action.restartVerify")}
              </Button>
            )}

            <Button
              variant="outline"
              className="w-full"
              onClick={() => checkCoturn.mutate()}
              disabled={busy || !data?.containerExists || protectedEvidenceRestricted}
              aria-describedby="coturn-action-authority-help"
            >
              <PlayCircle className="mr-2 h-4 w-4" />
              {checkCoturn.isPending
                ? t("services.coturn.action.checking")
                : t("services.coturn.action.runCheck")}
            </Button>

            {showHostNativeRecovery && (
              <>
                {canOpenPortainer ? (
                  <Button asChild variant="outline" className="w-full">
                    <a
                      href={coturnPortainerHandoffUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      <ExternalLink className="mr-2 h-4 w-4" />
                      {t("services.coturn.hostNative.openPortainer")}
                    </a>
                  </Button>
                ) : (
                  <Button
                    type="button"
                    variant="outline"
                    className="w-full"
                    disabled
                  >
                    <ExternalLink className="mr-2 h-4 w-4" />
                    {t("services.coturn.hostNative.openPortainer")}
                  </Button>
                )}

                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="justify-start px-2"
                  aria-expanded={hostNativeHelpOpen}
                  aria-controls="coturn-host-native-recovery-help"
                  onClick={() => setHostNativeHelpOpen((value) => !value)}
                >
                  <Info className="mr-2 h-4 w-4" />
                  {hostNativeHelpOpen
                    ? t("services.coturn.hostNative.hideHelp")
                    : t("services.coturn.hostNative.showHelp")}
                </Button>
              </>
            )}

            <Button
              variant="outline"
              className="w-full"
              onClick={() => setLogsOpen((value) => !value)}
              disabled={!data?.containerExists}
            >
              <FileText className="mr-2 h-4 w-4" />
              {logsOpen
                ? t("services.coturn.action.hideLogs")
                : t("services.coturn.action.viewLogs")}
            </Button>

            {showHostNativeRecovery && hostNativeHelpOpen && (
              <div
                id="coturn-host-native-recovery-help"
                className="space-y-3 rounded-lg border border-sky-500/20 bg-sky-500/10 p-4 text-sm"
              >
                <div>
                  <div className="font-medium">
                    {t("services.coturn.hostNative.helpTitle")}
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("services.coturn.hostNative.helpDescription")}
                  </p>
                </div>

                <div>
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("services.coturn.hostNative.portainerSetupTitle")}
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("services.coturn.hostNative.portainerSetupDescription")}
                  </p>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {t("services.coturn.hostNative.portainerSetupToken")}
                  </p>
                  <div className="mt-2">
                    <CodeLine>docker logs portainer 2&gt;&amp;1 | grep 'setup_token=' | tail -n 1</CodeLine>
                  </div>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {t("services.coturn.hostNative.portainerSetupTimeout")}
                  </p>
                  <div className="mt-2">
                    <CodeLine>docker restart portainer</CodeLine>
                  </div>
                  <p className="mt-2 text-xs text-muted-foreground">
                    {t("services.coturn.hostNative.portainerSetupBoundary")}
                  </p>
                </div>

                <div>
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("services.coturn.hostNative.troubleshootTitle")}
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("services.coturn.hostNative.troubleshootDescription")}
                  </p>
                </div>

                <div className="space-y-2">
                  <CodeLine>docker logs --tail 200 mem-coturn</CodeLine>
                  <CodeLine>docker inspect mem-coturn</CodeLine>
                  <CodeLine>docker restart mem-coturn</CodeLine>
                  <CodeLine>docker stop mem-coturn</CodeLine>
                  <CodeLine>docker start mem-coturn</CodeLine>
                </div>

                <p className="text-xs text-muted-foreground">
                  {t("services.coturn.hostNative.manualBoundary")}
                </p>

                {!canOpenPortainer && !portainer.isLoading ? (
                  <p className="text-xs text-amber-300">
                    {t("services.coturn.hostNative.portainerUnavailable")}
                  </p>
                ) : null}
              </div>
            )}

            <div
              id="coturn-action-authority-help"
              className="rounded-lg border border-border bg-background/40 p-3 text-xs text-muted-foreground"
            >
              {protectedEvidenceRestricted
                ? t("services.coturn.actions.restrictedHelp")
                : t("services.coturn.actions.help")}
            </div>
          </CardContent>
        </Card>
      </div>

      {latestResult && (
        <CheckResults
          result={latestResult}
          freshness={latestFreshness}
          incidentId={checkCoturn.data?.incidentId ?? latestCheck.data?.incidentId ?? null}
          t={t}
        />
      )}

      {logsOpen && (
        <LogsCard
          loading={logs.isLoading}
          error={logs.error}
          data={logs.data}
          onRefresh={() => void logs.refetch()}
          t={t}
        />
      )}

      <Card>
        <CardHeader>
          <CardTitle>{t("services.coturn.usedByStacks.title")}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("services.coturn.usedByStacks.description")}
          </p>
        </CardHeader>
        <CardContent>
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("services.coturn.usedByStacks.current")}
          </div>
        </CardContent>
      </Card>

      <ConfirmationDialog
        open={confirmationTarget !== null}
        onOpenChange={(open) => {
          if (!open) setConfirmationTarget(null)
        }}
        title={confirmation.title}
        description={confirmation.description}
        confirmLabel={confirmation.confirmLabel}
        confirmingLabel={confirmation.confirmingLabel}
        cancelLabel={t("services.coturn.confirmation.cancel")}
        confirmVariant={confirmationTarget === "repair" || confirmationTarget === "apply" ? "destructive" : "default"}
        onConfirm={confirmCoturnAction}
        isConfirming={confirmationPending}
        showProgress={confirmationPending}
      >
        {confirmation.details.length > 0 ? (
          <div className="space-y-2 text-sm">
            {confirmation.details.map((detail) => (
              <div
                key={detail}
                className="rounded-lg border border-border bg-background/30 px-3 py-2 text-muted-foreground"
              >
                {detail}
              </div>
            ))}
          </div>
        ) : null}

        {confirmation.interruption ? (
          <Alert className="border-amber-500/20 bg-amber-500/10">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("services.coturn.confirmation.interruptionTitle")}</AlertTitle>
            <AlertDescription>{confirmation.interruption}</AlertDescription>
          </Alert>
        ) : null}

        {confirmationError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("services.coturn.confirmation.errorTitle")}</AlertTitle>
            <AlertDescription>{confirmationError}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => {
          if (stepUpTarget === "maintenance" && pendingMaintenanceRequest) {
            submitMaintenance(pendingMaintenanceRequest)
            return
          }
          submitInstall()
        }}
      />
    </div>
  )
}

function describeCoturnConfirmation(
  target: CoturnConfirmationTarget | null,
  relayMinPort: number,
  relayMaxPort: number,
  t: Translate,
  currentExternalIp: string | null,
  requestedExternalIp: string | null,
) {
  if (target === "apply") {
    return {
      title: t("services.coturn.confirmation.apply.title"),
      description: t("services.coturn.confirmation.apply.description"),
      confirmLabel: t("services.coturn.action.applyVerify"),
      confirmingLabel: t("services.coturn.action.applying"),
      details: [
        t("services.coturn.confirmation.apply.current", {
          value: currentExternalIp ?? t("services.coturn.externalIp.automatic"),
        }),
        t("services.coturn.confirmation.apply.requested", {
          value: requestedExternalIp ?? t("services.coturn.externalIp.automatic"),
        }),
        t("services.coturn.confirmation.apply.configuration"),
        t("services.coturn.confirmation.apply.verification"),
        t("services.coturn.confirmation.apply.externalBoundary"),
      ],
      interruption: t("services.coturn.confirmation.repair.interruption"),
    }
  }

  if (target === "install") {
    return {
      title: t("services.coturn.confirmation.install.title"),
      description: t("services.coturn.confirmation.install.description"),
      confirmLabel: t("services.coturn.action.install"),
      confirmingLabel: t("services.coturn.action.installing"),
      details: [
        t("services.coturn.confirmation.install.image"),
        t("services.coturn.confirmation.apply.requested", {
          value: requestedExternalIp ?? t("services.coturn.externalIp.automatic"),
        }),
        t("services.coturn.confirmation.install.ports", {
          min: relayMinPort,
          max: relayMaxPort,
        }),
        t("services.coturn.confirmation.install.verification"),
      ],
      interruption: null as string | null,
    }
  }

  if (target === "repair") {
    return {
      title: t("services.coturn.confirmation.repair.title"),
      description: t("services.coturn.confirmation.repair.description"),
      confirmLabel: t("services.coturn.action.repair"),
      confirmingLabel: t("services.coturn.action.repairing"),
      details: [
        t("services.coturn.confirmation.repair.image"),
        t("services.coturn.confirmation.repair.configuration"),
        t("services.coturn.confirmation.apply.requested", {
          value: requestedExternalIp ?? t("services.coturn.externalIp.automatic"),
        }),
        t("services.coturn.confirmation.repair.ports", {
          min: relayMinPort,
          max: relayMaxPort,
        }),
        t("services.coturn.confirmation.repair.verification"),
      ],
      interruption: t("services.coturn.confirmation.repair.interruption"),
    }
  }

  return {
    title: t("services.coturn.confirmation.restart.title"),
    description: t("services.coturn.confirmation.restart.description"),
    confirmLabel: t("services.coturn.action.restartVerify"),
    confirmingLabel: t("services.coturn.action.restarting"),
    details: target === "restart-verify"
      ? [
          t("services.coturn.confirmation.restart.runtime"),
          t("services.coturn.confirmation.restart.configuration"),
          t("services.coturn.confirmation.restart.verification"),
        ]
      : [],
    interruption: target === "restart-verify"
      ? t("services.coturn.confirmation.restart.interruption")
      : null,
  }
}

// Comparison only: validation remains server-owned. Normalize IPv6 spelling so
// a successful application is not left marked pending after .NET canonicalizes
// the literal. Never use URL parsing for arbitrary user text or hostnames.
function sameExternalIp(left: string | null | undefined, right: string | null | undefined) {
  const normalize = (value: string | null | undefined) => {
    const literal = (value ?? "").trim()
    if (literal.includes(":") && /^[0-9a-f:.]+$/i.test(literal)) {
      try { return new URL(`http://[${literal}]/`).hostname.toLowerCase() } catch { /* keep invalid input for server validation */ }
    }
    return literal
  }
  return normalize(left) === normalize(right)
}

function createMaintenanceIdempotencyKey() {
  if (typeof crypto !== "undefined" && "randomUUID" in crypto) {
    return crypto.randomUUID()
  }

  return `coturn-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function localizeMaintenanceStep(step: string | null, t: Translate) {
  switch (step) {
    case "queued":
      return t("services.coturn.maintenance.step.queued")
    case "restart-platform-turn":
      return t("services.coturn.maintenance.step.restart")
    case "repair-platform-turn":
      return t("services.coturn.maintenance.step.repair")
    case "verify-platform-turn-runtime":
      return t("services.coturn.maintenance.step.runtimeVerify")
    case "verify-platform-turn-functional":
      return t("services.coturn.maintenance.step.functionalVerify")
    case "completed":
      return t("services.coturn.maintenance.step.completed")
    default:
      return step ?? t("services.coturn.maintenance.step.queued")
  }
}

function CheckResults({
  result,
  freshness,
  incidentId,
  t,
}: {
  result: CoturnCheckResponse
  freshness: CoturnCheckFreshness
  incidentId: string | null
  t: Translate
}) {
  if (!result) return null

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{t("services.coturn.check.title")}</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("services.coturn.check.description")}
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <StatusPill status={result.status} t={t} />
            <span className="rounded-full border border-border px-2 py-1 text-xs text-muted-foreground">
              {localizeCheckFreshness(freshness, t)}
            </span>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted-foreground">
          <span>
            {t("services.coturn.check.checkedAt", {
              value: formatTimestamp(result.checkedAtUtc),
            })}
          </span>
          <span>
            {t("services.coturn.check.freshUntil", {
              value: formatTimestamp(result.freshUntilUtc),
            })}
          </span>
        </div>

        {freshness === "runtime-changed" && (
          <Alert className="border-amber-500/20 bg-amber-500/10">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("services.coturn.check.runtimeChanged.title")}</AlertTitle>
            <AlertDescription>
              {t("services.coturn.check.runtimeChanged.description")}
            </AlertDescription>
          </Alert>
        )}

        {freshness === "stale" && (
          <Alert className="border-amber-500/20 bg-amber-500/10">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("services.coturn.check.stale.title")}</AlertTitle>
            <AlertDescription>
              {t("services.coturn.check.stale.description")}
            </AlertDescription>
          </Alert>
        )}

        {incidentId && (
          <div className="flex justify-end">
            <Button asChild variant="outline" size="sm">
              <Link to={`/diagnostics/logs?incident=${encodeURIComponent(incidentId)}`}>
                {t("services.coturn.check.reviewIncident")}
              </Link>
            </Button>
          </div>
        )}

        <div className="grid gap-3 md:grid-cols-2">
          {result.checks.map((check: CoturnCheckItem) => (
            <CheckRow key={check.key} check={check} t={t} />
          ))}
        </div>

        <div className="rounded-xl border border-border bg-background/40 p-4">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div className="font-medium">
              {t("services.coturn.check.allocationTitle")}
            </div>
            <StatusPill status={result.allocation.status} t={t} />
          </div>
          <p className="mt-2 text-sm text-muted-foreground">
            {localizeAllocationSummary(result.allocation.status, t)}
          </p>
          {result.allocation.logTail && (
            <details className="mt-3">
              <summary className="cursor-pointer text-xs font-medium text-muted-foreground">
                {t("services.coturn.technicalDetails")}
              </summary>
              <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap break-all rounded-lg border border-border bg-background p-3 text-xs">
                {result.allocation.logTail}
              </pre>
            </details>
          )}
        </div>
      </CardContent>
    </Card>
  )
}

function CheckRow({
  check,
  t,
}: {
  check: CoturnCheckItem
  t: Translate
}) {
  return (
    <div className="rounded-xl border border-border bg-background/40 p-4">
      <div className="flex items-center justify-between gap-3">
        <div className="text-sm font-medium">
          {localizeCheckKey(check.key, t)}
        </div>
        <StatusPill status={check.status} t={t} />
      </div>
      {check.key === "external-ip" && (
        <div className="mt-2 space-y-2 text-sm">
          <p>{localizeRelaySummary(check, t)}</p>
          <p className="text-xs text-muted-foreground">{t("services.coturn.relay.externalBoundary")}</p>
        </div>
      )}
      <details className="mt-2">
        <summary className="cursor-pointer text-xs text-muted-foreground">
          {t("services.coturn.check.details")}
        </summary>
        <p className="mt-2 text-xs text-muted-foreground">{check.summary}</p>
        {check.detail && (
          <p className="mt-1 break-all font-mono text-xs text-muted-foreground">
            {check.detail}
          </p>
        )}
      </details>
    </div>
  )
}

function localizeRelaySummary(check: CoturnCheckItem, t: Translate) {
  const keys: Record<string, TranslationKey> = {
    "relay-auto-public": "services.coturn.relay.autoPublic",
    "relay-explicit-public": "services.coturn.relay.explicitPublic",
    "relay-non-public": "services.coturn.relay.nonPublic",
    "relay-mismatch": "services.coturn.relay.mismatch",
    "relay-inconclusive": "services.coturn.relay.inconclusive",
    "relay-allocation-failed": "services.coturn.relay.allocationFailed",
    "relay-allocation-not-run": "services.coturn.relay.notRun",
    "relay-port-out-of-range": "services.coturn.relay.portOutOfRange",
  }
  const key = check.code ? keys[check.code] : undefined
  return key ? t(key) : check.summary
}

function LogsCard({
  loading,
  error,
  data,
  onRefresh,
  t,
}: {
  loading: boolean
  error: Error | null
  data: CoturnLogsResponse | undefined
  onRefresh: () => void
  t: Translate
}) {
  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{t("services.coturn.logs.title")}</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("services.coturn.logs.description")}
            </p>
          </div>
          <Button variant="outline" size="sm" onClick={onRefresh} disabled={loading}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("services.coturn.action.refreshLogs")}
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        {error ? (
          <ErrorAlert
            title={t("services.coturn.error.logsTitle")}
            description={t("services.coturn.error.generic")}
            technicalDetail={error.message}
            t={t}
          />
        ) : loading && !data ? (
          <EmptyBox>{t("services.coturn.logs.loading")}</EmptyBox>
        ) : !data?.content ? (
          <EmptyBox>{t("services.coturn.logs.empty")}</EmptyBox>
        ) : (
          <>
            <div className="mb-2 text-xs text-muted-foreground">
              {t("services.coturn.logs.summary", {
                count: data.returnedLines,
                value: formatTimestamp(data.retrievedAtUtc),
              })}
            </div>
            <pre className="max-h-96 overflow-auto whitespace-pre-wrap break-all rounded-xl border border-border bg-background p-4 text-xs">
              {data.content}
            </pre>
            {data.truncated && (
              <p className="mt-2 text-xs text-muted-foreground">
                {t("services.coturn.logs.truncated")}
              </p>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}

function ErrorAlert({
  title,
  description,
  technicalDetail,
  t,
}: {
  title: string
  description: string
  technicalDetail?: string | null
  t: Translate
}) {
  return (
    <Alert variant="destructive">
      <AlertTriangle className="h-4 w-4" />
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription>
        <div>{description}</div>
        {technicalDetail && (
          <details className="mt-2">
            <summary className="cursor-pointer text-xs">
              {t("services.coturn.technicalDetails")}
            </summary>
            <div className="mt-1 break-all font-mono text-xs">
              {technicalDetail}
            </div>
          </details>
        )}
      </AlertDescription>
    </Alert>
  )
}

function shouldShowStartupSupervisionAlert(
  supervision: CoturnStartupSupervisionResponse | undefined,
  runtime: CoturnRuntimeResponse | undefined,
  latestResult: CoturnCheckResponse | null,
  latestFreshness: CoturnCheckFreshness,
) {
  if (!supervision?.enabled) return false

  const terminalNegativeFinding =
    supervision.status === "repair-required" ||
    supervision.status === "conflict" ||
    supervision.status === "cooldown" ||
    supervision.status === "failed"

  if (!terminalNegativeFinding) return true

  const runtimeCurrentlyExact = Boolean(
    runtime?.containerExists &&
      runtime.running &&
      runtime.ownershipVerified &&
      runtime.runtimeExact &&
      runtime.operatorStatus === "runtime-ready",
  )

  const newerPassingVerification = Boolean(
    latestResult &&
      latestFreshness === "fresh" &&
      latestResult.status === "passed" &&
      isTimestampAfter(latestResult.checkedAtUtc, supervision.observedAtUtc),
  )

  // Startup-supervision findings are process-local evidence about an earlier
  // runtime snapshot. Keep them visible until a later, fresh passing TURN
  // verification proves that the owned runtime has converged. The historical
  // incident/evidence remains available in Diagnostics; the Coturn workspace
  // should not continue presenting a resolved repair requirement as current.
  return !(runtimeCurrentlyExact && newerPassingVerification)
}

function isTimestampAfter(candidate: string, reference: string) {
  const candidateMs = Date.parse(candidate)
  const referenceMs = Date.parse(reference)

  return (
    Number.isFinite(candidateMs) &&
    Number.isFinite(referenceMs) &&
    candidateMs > referenceMs
  )
}

function StartupSupervisionAlert({
  supervision,
  t,
}: {
  supervision: CoturnStartupSupervisionResponse
  t: Translate
}) {
  const status = supervision.status
  const recovering = status === "waiting" || status === "recovering"
  const positive = status === "verified" || status === "recovered"
  const severe = status === "repair-required" || status === "conflict" || status === "failed"

  const className = positive
    ? "border-emerald-500/20 bg-emerald-500/10"
    : severe
      ? "border-rose-500/20 bg-rose-500/10"
      : status === "cooldown"
        ? "border-amber-500/20 bg-amber-500/10"
        : "border-sky-500/20 bg-sky-500/10"

  const icon = recovering ? (
    <RefreshCw className="h-4 w-4 animate-spin" />
  ) : positive ? (
    <ShieldCheck className="h-4 w-4" />
  ) : severe || status === "cooldown" ? (
    <AlertTriangle className="h-4 w-4" />
  ) : (
    <Info className="h-4 w-4" />
  )

  return (
    <Alert className={className}>
      {icon}
      <AlertTitle>
        {t(`services.coturn.startupSupervision.${status}.title` as TranslationKey)}
      </AlertTitle>
      <AlertDescription>
        <div className="space-y-1">
          <p>
            {t(`services.coturn.startupSupervision.${status}.description` as TranslationKey)}
          </p>
          {supervision.cooldownActive && supervision.cooldownUntilUtc ? (
            <p className="text-xs text-muted-foreground">
              {t("services.coturn.startupSupervision.cooldownUntil", {
                value: formatTimestamp(supervision.cooldownUntilUtc),
              })}
            </p>
          ) : null}
        </div>
      </AlertDescription>
    </Alert>
  )
}

function PageHeader({
  busy,
  onRefresh,
  t,
}: {
  busy: boolean
  onRefresh: () => void
  t: Translate
}) {
  return (
    <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
      <div>
        <Button variant="ghost" size="sm" asChild className="-ml-3 mb-2">
          <Link to="/services">
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("services.coturn.back")}
          </Link>
        </Button>

        <h1 className="text-2xl font-semibold tracking-tight">
          {t("services.coturn.title")}
        </h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("services.coturn.description")}
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        <Button variant="outline" size="sm" onClick={onRefresh} disabled={busy}>
          <RefreshCw className="mr-2 h-4 w-4" />
          {t("services.coturn.action.refresh")}
        </Button>
      </div>
    </div>
  )
}

type StateTone = "positive" | "warning" | "danger" | "neutral"

function StateCard({
  label,
  value,
  tone,
}: {
  label: string
  value: string
  tone: StateTone
}) {
  const toneClass = tone === "positive"
    ? "border-emerald-500/20 bg-emerald-500/10"
    : tone === "danger"
      ? "border-red-500/20 bg-red-500/10"
      : tone === "warning"
        ? "border-amber-500/20 bg-amber-500/10"
        : "border-border bg-background/40"

  return (
    <div className={`rounded-xl border p-4 ${toneClass}`}>
      <div className="flex items-center gap-2 text-xs uppercase tracking-wide text-muted-foreground">
        {tone === "positive" ? (
          <ShieldCheck className="h-4 w-4" />
        ) : (
          <AlertTriangle className="h-4 w-4" />
        )}
        {label}
      </div>
      <div className="mt-2 text-lg font-medium">{value}</div>
    </div>
  )
}

function DockerRuntimeEvidenceView({
  evidence,
  t,
}: {
  evidence: CoturnDockerRuntimeEvidence
  t: Translate
}) {
  const configMountVerified =
    evidence.configMountPresent &&
    evidence.configMountReadOnly &&
    evidence.configMountSourceMatches &&
    evidence.configMountDestinationMatches

  return (
    <div className="grid gap-4 text-sm md:grid-cols-2 xl:grid-cols-4">
      <InfoTile
        label={t("services.coturn.runtime.restartPolicy")}
        value={t("services.coturn.runtime.restartPolicyValue", {
          actual: evidence.restartPolicy,
          expected: evidence.expectedRestartPolicy,
        })}
      />
      <InfoTile
        label={t("services.coturn.runtime.restartCount")}
        value={String(evidence.restartCount)}
      />
      <InfoTile
        label={t("services.coturn.runtime.restarting")}
        value={yesNo(evidence.restarting, t)}
      />
      <InfoTile
        label={t("services.coturn.runtime.paused")}
        value={yesNo(evidence.paused, t)}
      />
      <InfoTile
        label={t("services.coturn.runtime.exitCode")}
        value={String(evidence.exitCode)}
      />
      <InfoTile
        label={t("services.coturn.runtime.oomKilled")}
        value={yesNo(evidence.oomKilled, t)}
      />
      <InfoTile
        label={t("services.coturn.runtime.dead")}
        value={yesNo(evidence.dead, t)}
      />
      <InfoTile
        label={t("services.coturn.runtime.stateError")}
        value={yesNo(evidence.stateErrorPresent, t)}
      />
      <InfoTile
        label={t("services.coturn.runtime.dockerHealth")}
        value={evidence.healthStatus ?? t("services.coturn.runtime.notConfigured")}
      />
      <InfoTile
        label={t("services.coturn.runtime.startedAt")}
        value={evidence.startedAtUtc ? formatTimestamp(evidence.startedAtUtc) : t("services.coturn.notSet")}
      />
      <InfoTile
        label={t("services.coturn.runtime.finishedAt")}
        value={evidence.finishedAtUtc ? formatTimestamp(evidence.finishedAtUtc) : t("services.coturn.notSet")}
      />
      <InfoTile
        label={t("services.coturn.runtime.networkMode")}
        value={evidence.networkMode ?? t("services.coturn.notSet")}
      />
      <InfoTile
        label={t("services.coturn.runtime.expectedNetwork")}
        value={evidence.expectedNetwork}
      />
      <InfoTile
        label={t("services.coturn.runtime.attachedNetworks")}
        value={evidence.attachedNetworks.length > 0 ? evidence.attachedNetworks.join(", ") : t("services.coturn.runtime.none")}
      />
      <InfoTile
        label={t("services.coturn.runtime.networkAliases")}
        value={evidence.observedExpectedNetworkAliases.length > 0
          ? evidence.observedExpectedNetworkAliases.join(", ")
          : t("services.coturn.runtime.none")}
      />
      <InfoTile
        label={t("services.coturn.runtime.configMount")}
        value={configMountVerified
          ? t("services.coturn.runtime.verified")
          : t("services.coturn.runtime.notVerified")}
      />
      <InfoTile
        label={t("services.coturn.runtime.commandContract")}
        value={evidence.commandMatches
          ? t("services.coturn.runtime.verified")
          : t("services.coturn.runtime.notVerified")}
      />
      <InfoTile
        label={t("services.coturn.runtime.startupUserContract")}
        value={evidence.startupUserMatches
          ? t("services.coturn.runtime.verified")
          : t("services.coturn.runtime.notVerified")}
      />
    </div>
  )
}

function yesNo(value: boolean, t: Translate) {
  return value ? t("services.coturn.yes") : t("services.coturn.no")
}

function StatusPill({
  status,
  t,
}: {
  status: CoturnCheckStatus
  t: Translate
}) {
  const icon = status === "passed"
    ? <CheckCircle2 className="h-3.5 w-3.5" />
    : status === "failed"
      ? <XCircle className="h-3.5 w-3.5" />
      : <AlertTriangle className="h-3.5 w-3.5" />
  const tone = status === "passed"
    ? "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"
    : status === "failed"
      ? "border-red-500/20 bg-red-500/10 text-red-300"
      : "border-amber-500/20 bg-amber-500/10 text-amber-300"

  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-1 text-xs font-medium ${tone}`}>
      {icon}
      {localizeCheckStatus(status, t)}
    </span>
  )
}

function Section({
  title,
  children,
}: {
  title: string
  children: ReactNode
}) {
  return (
    <div className="rounded-xl border border-border bg-background/40 p-4">
      <div className="mb-3 text-sm font-medium">{title}</div>
      {children}
    </div>
  )
}

function CodeLine({ children }: { children: ReactNode }) {
  return (
    <div className="break-all rounded-lg border border-border bg-background/40 px-3 py-2 font-mono text-xs text-muted-foreground">
      {children}
    </div>
  )
}

function EmptyBox({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
      {children}
    </div>
  )
}

function InfoTile({
  label,
  value,
  breakAll = false,
}: {
  label: string
  value: string
  breakAll?: boolean
}) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div
        className={[
          "mt-1 text-foreground",
          breakAll ? "break-all font-mono text-xs" : "break-words",
        ].join(" ")}
      >
        {value}
      </div>
    </div>
  )
}

function localizeContainerState(value: string, t: Translate) {
  switch (value) {
    case "running":
      return t("services.coturn.container.running")
    case "stopped":
      return t("services.coturn.container.stopped")
    case "collision":
      return t("services.coturn.container.collision")
    case "not-deployed":
      return t("services.coturn.container.notDeployed")
    default:
      return t("services.coturn.container.unknown")
  }
}

function localizeReadiness(value: string, t: Translate) {
  switch (value) {
    case "ready":
      return t("services.coturn.readiness.ready")
    case "verification-limited":
      return t("services.coturn.readiness.verificationLimited")
    case "setup-incomplete":
      return t("services.coturn.readiness.setupIncomplete")
    case "repair-required":
      return t("services.coturn.readiness.repairRequired")
    case "degraded":
      return t("services.coturn.readiness.degraded")
    case "stopped":
      return t("services.coturn.readiness.stopped")
    case "unavailable":
      return t("services.coturn.readiness.unavailable")
    case "not-deployed":
      return t("services.coturn.readiness.notDeployed")
    default:
      return t("services.coturn.readiness.unknown")
  }
}

function localizeOperatorStatus(value: string, t: Translate) {
  switch (value) {
    case "runtime-ready":
      return t("services.coturn.operatorStatus.runtimeReady")
    case "verification-limited":
      return t("services.coturn.operatorStatus.verificationLimited")
    case "needs-attention":
      return t("services.coturn.operatorStatus.needsAttention")
    case "repair-required":
      return t("services.coturn.operatorStatus.repairRequired")
    case "stopped":
      return t("services.coturn.operatorStatus.stopped")
    case "not-deployed":
      return t("services.coturn.operatorStatus.notDeployed")
    case "conflict":
      return t("services.coturn.operatorStatus.conflict")
    default:
      return t("services.coturn.operatorStatus.unknown")
  }
}

function operatorStatusTone(value: string): StateTone {
  switch (value) {
    case "runtime-ready":
      return "positive"
    case "verification-limited":
      return "neutral"
    case "needs-attention":
    case "repair-required":
    case "stopped":
      return "warning"
    case "conflict":
      return "danger"
    default:
      return "neutral"
  }
}

function localizeProtectedEvidenceAccess(value: string, t: Translate) {
  switch (value) {
    case "available":
      return t("services.coturn.protectedEvidence.available")
    case "restricted":
      return t("services.coturn.protectedEvidence.restricted.value")
    default:
      return t("services.coturn.protectedEvidence.unavailable")
  }
}

function localizeRuntimeDrift(code: string, t: Translate) {
  const known: Record<string, TranslationKey> = {
    "docker-inspection": "services.coturn.runtime.drift.dockerInspection",
    "runtime-state": "services.coturn.runtime.drift.runtimeState",
    "restart-policy": "services.coturn.runtime.drift.restartPolicy",
    "gateway-network": "services.coturn.runtime.drift.gatewayNetwork",
    "network-aliases": "services.coturn.runtime.drift.networkAliases",
    "config-mount": "services.coturn.runtime.drift.configMount",
    command: "services.coturn.runtime.drift.command",
    "startup-user": "services.coturn.runtime.drift.startupUser",
  }

  return known[code] ? t(known[code]) : code
}

function isCheckResultNewer(
  mutationResult: CoturnCheckResponse | undefined,
  persistedResult: CoturnCheckResponse | null | undefined,
) {
  if (!mutationResult) return false
  if (!persistedResult) return true

  return (
    Date.parse(mutationResult.checkedAtUtc) >
    Date.parse(persistedResult.checkedAtUtc)
  )
}

function describeFunctionalHealth(
  result: CoturnCheckResponse | null,
  freshness: CoturnCheckFreshness,
  t: Translate,
): {
  label: string
  tone: StateTone
  description: string
} {
  if (!result) {
    return freshness === "unavailable"
      ? {
          label: t("services.coturn.functionalHealth.unavailable"),
          tone: "neutral",
          description: t("services.coturn.functionalHealth.unavailableDescription"),
        }
      : {
          label: t("services.coturn.functionalHealth.notChecked"),
          tone: "neutral",
          description: t("services.coturn.functionalHealth.notCheckedDescription"),
        }
  }

  if (freshness === "runtime-changed") {
    return {
      label: t("services.coturn.functionalHealth.checkRequired"),
      tone: "warning",
      description: t("services.coturn.functionalHealth.runtimeChangedDescription"),
    }
  }

  if (result.status === "failed") {
    return {
      label: t("services.coturn.functionalHealth.needsAttention"),
      tone: "danger",
      description: t("services.coturn.functionalHealth.failedDescription"),
    }
  }

  if (result.status === "warning") {
    return {
      label: t("services.coturn.functionalHealth.warning"),
      tone: "warning",
      description: t("services.coturn.functionalHealth.warningDescription"),
    }
  }

  if (freshness === "stale") {
    return {
      label: t("services.coturn.functionalHealth.checkOverdue"),
      tone: "warning",
      description: t("services.coturn.functionalHealth.staleDescription"),
    }
  }

  if (freshness === "unavailable") {
    return {
      label: t("services.coturn.functionalHealth.unavailable"),
      tone: "neutral",
      description: t("services.coturn.functionalHealth.unavailableDescription"),
    }
  }

  if (freshness !== "fresh") {
    return {
      label: t("services.coturn.functionalHealth.checkRecommended"),
      tone: "neutral",
      description: t("services.coturn.functionalHealth.notCheckedDescription"),
    }
  }

  return {
    label: t("services.coturn.functionalHealth.healthy"),
    tone: "positive",
    description: t("services.coturn.functionalHealth.healthyDescription"),
  }
}

function localizeCheckFreshness(value: CoturnCheckFreshness, t: Translate) {
  switch (value) {
    case "fresh":
      return t("services.coturn.check.freshness.fresh")
    case "stale":
      return t("services.coturn.check.freshness.stale")
    case "runtime-changed":
      return t("services.coturn.check.freshness.runtimeChanged")
    case "unavailable":
      return t("services.coturn.check.freshness.unavailable")
    default:
      return t("services.coturn.check.freshness.notChecked")
  }
}

function localizeCheckStatus(status: CoturnCheckStatus, t: Translate) {
  switch (status) {
    case "passed":
      return t("services.coturn.check.status.passed")
    case "failed":
      return t("services.coturn.check.status.failed")
    case "warning":
      return t("services.coturn.check.status.warning")
    default:
      return t("services.coturn.check.status.notRun")
  }
}

function localizeCheckKey(key: string, t: Translate) {
  const known: Record<string, TranslationKey> = {
    container: "services.coturn.check.key.container",
    "docker-runtime": "services.coturn.check.key.dockerRuntime",
    "restart-policy": "services.coturn.check.key.restartPolicy",
    "gateway-network": "services.coturn.check.key.gatewayNetwork",
    "config-mount": "services.coturn.check.key.configMount",
    "approved-image": "services.coturn.check.key.approvedImage",
    configuration: "services.coturn.check.key.configuration",
    "shared-secret": "services.coturn.check.key.sharedSecret",
    "listener-ports": "services.coturn.check.key.listenerPorts",
    "relay-ports": "services.coturn.check.key.relayPorts",
    "platform-domain": "services.coturn.check.key.platformDomain",
    "host-dns": "services.coturn.check.key.hostDns",
    "probe-dns": "services.coturn.check.key.probeDns",
    "external-ip": "services.coturn.check.key.externalIp",
  }

  return known[key] ? t(known[key]) : key
}

function localizeAllocationSummary(status: CoturnCheckStatus, t: Translate) {
  switch (status) {
    case "passed":
      return t("services.coturn.check.allocationPassed")
    case "failed":
      return t("services.coturn.check.allocationFailed")
    default:
      return t("services.coturn.check.allocationNotRun")
  }
}

function friendlyCoturnError(error: Error, t: Translate) {
  if (error instanceof CoturnApiError && error.code === "coturn_restart_configuration_not_allowed") {
    return t("services.coturn.error.restartConfiguration")
  }
  return error instanceof CoturnApiError && error.code === "coturn_external_ip_invalid"
    ? t("services.coturn.error.invalidExternalIp")
    : t("services.coturn.error.generic")
}

function formatTimestamp(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
