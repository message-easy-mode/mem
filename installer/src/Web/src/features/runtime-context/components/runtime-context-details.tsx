import { useState, type PropsWithChildren, type ReactElement } from "react"
import {
  ArrowRight,
  Box,
  Building2,
  Check,
  Clipboard,
  Cpu,
  Database,
  Fingerprint,
  GitCommitHorizontal,
  Info,
  Link2,
  Monitor,
  Network,
  ServerCog,
  ShieldCheck,
  Tag,
  X,
  type LucideIcon,
} from "lucide-react"
import { Dialog as DialogPrimitive } from "radix-ui"
import { useNavigate } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  controlPlaneAccessModeKey,
  controlPlaneExposureStateKey,
  dockerEndpointKindKey,
  runtimeModeKey,
  stateRootLabelKey,
  uiDeliveryKey,
} from "../runtime-context-labels"
import { getMemReleaseCodename } from "../runtime-release-identity"
import { runtimeContextNeedsAttention } from "../runtime-context-state"
import { useRuntimeContext } from "../use-runtime-context"

export function RuntimeContextDetailsProvider({ children }: PropsWithChildren) {
  const [open, setOpen] = useState(false)

  return (
    <DialogPrimitive.Root open={open} onOpenChange={setOpen}>
      {children}
      {open ? <RuntimeContextDetailsContent /> : null}
    </DialogPrimitive.Root>
  )
}

export function RuntimeContextDetailsTrigger({ children }: { children: ReactElement }) {
  return <DialogPrimitive.Trigger asChild>{children}</DialogPrimitive.Trigger>
}

type FieldCopyState = {
  field: string
  status: "copied" | "failed"
} | null

function RuntimeContextDetailsContent() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const runtime = useRuntimeContext()
  const data = runtime.data
  const [copyState, setCopyState] = useState<"idle" | "copied" | "failed">("idle")
  const [fieldCopyState, setFieldCopyState] = useState<FieldCopyState>(null)
  const exposureMode = data ? t(controlPlaneAccessModeKey(data.controlPlaneExposure.accessMode)) : ""
  const exposureState = data ? t(controlPlaneExposureStateKey(data.controlPlaneExposure.state)) : ""
  const exposureDescription = data
    ? data.controlPlaneExposure.hostAddress && data.controlPlaneExposure.hostPort
      ? t("runtime.card.accessBinding", {
          mode: exposureMode,
          address: data.controlPlaneExposure.hostAddress,
          port: data.controlPlaneExposure.hostPort,
        })
      : `${exposureMode} · ${exposureState}`
    : ""
  const codename = getMemReleaseCodename(data?.version)
  const versionDisplay = data
    ? codename
      ? `${data.version} · ${codename}`
      : data.version
    : ""

  async function copyRuntimeContext() {
    if (!data || !navigator.clipboard?.writeText) {
      setCopyState("failed")
      return
    }

    const lines = [
      t("runtime.details.title"),
      `${t("runtime.card.mode")}: ${t(runtimeModeKey(data.runtimeMode))}`,
      `${t("runtime.card.uiDelivery")}: ${t(uiDeliveryKey(data.uiDeliveryMode))}`,
      `${t("runtime.card.environment")}: ${data.environmentName}`,
      `${t("runtime.card.state")}: ${t(stateRootLabelKey(data.stateRootKind, data.stateRootProfile))}`,
      `${t("runtime.card.docker")}: ${t(dockerEndpointKindKey(data.dockerEndpointKind))}`,
      `${t("runtime.card.ownership")}: ${t(`runtime.ownership.${data.dockerOwnership.state}` as TranslationKey)}`,
      `${t("runtime.card.access")}: ${exposureDescription}`,
      `${t("runtime.details.validation")}: ${data.validationState === "valid" ? t("runtime.validation.valid") : t("runtime.validation.warning")}`,
      `${t("runtime.card.instance")}: ${data.controlPlaneInstanceId}`,
      `${t("runtime.card.process")}: ${data.apiProcessInstanceId}`,
      `${t("runtime.details.version")}: ${versionDisplay}`,
      ...(data.commit ? [`${t("runtime.details.commit")}: ${data.commit}`] : []),
    ]

    try {
      await navigator.clipboard.writeText(lines.join("\n"))
      setCopyState("copied")
    } catch {
      setCopyState("failed")
    }
  }

  async function copyRuntimeValue(field: string, value: string) {
    if (!navigator.clipboard?.writeText) {
      setFieldCopyState({ field, status: "failed" })
      return
    }

    try {
      await navigator.clipboard.writeText(value)
      setFieldCopyState({ field, status: "copied" })
    } catch {
      setFieldCopyState({ field, status: "failed" })
    }
  }

  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/60 backdrop-blur-[1px]" />
      <DialogPrimitive.Content
        className="fixed inset-y-0 right-0 z-50 flex h-dvh w-full flex-col overflow-hidden border-l border-border bg-card text-card-foreground shadow-2xl outline-none sm:w-[min(48rem,calc(100vw-2rem))]"
        data-testid="runtime-context-details"
      >
        <div className="border-b border-border px-5 py-5 sm:px-7 sm:py-6">
          <div className="flex items-start justify-between gap-4">
            <div className="flex min-w-0 items-start gap-3.5">
              <div className="mt-0.5 inline-flex size-11 shrink-0 items-center justify-center rounded-xl border border-sky-400/20 bg-sky-500/10 text-sky-400 shadow-sm">
                <Monitor className="size-5" aria-hidden="true" />
              </div>
              <div className="min-w-0">
                <DialogPrimitive.Title className="text-xl font-semibold tracking-tight sm:text-2xl">
                  {t("runtime.details.title")}
                </DialogPrimitive.Title>
                <DialogPrimitive.Description className="mt-1 max-w-2xl text-sm leading-6 text-muted-foreground">
                  {t("runtime.details.description")}
                </DialogPrimitive.Description>
              </div>
            </div>
            <DialogPrimitive.Close asChild>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                className="mt-0.5 shrink-0"
                aria-label={t("runtime.details.close")}
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </Button>
            </DialogPrimitive.Close>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-5 sm:px-7 sm:py-6">
          {runtime.isLoading && !data ? (
            <p className="text-sm text-muted-foreground">{t("runtime.card.loading")}</p>
          ) : runtime.error && !data ? (
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/[0.05] p-4">
              <div className="font-medium">{t("runtime.card.unavailableTitle")}</div>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("runtime.card.unavailableDescription")}
              </p>
            </div>
          ) : data ? (
            <div className="space-y-6">
              <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border pb-4">
                <div className="text-base font-semibold">{data.productDisplayName}</div>
                <Badge
                  variant="outline"
                  className={
                    runtimeContextNeedsAttention(data)
                      ? "border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300"
                      : "border-emerald-500/40 bg-emerald-500/10 text-emerald-700 dark:text-emerald-300"
                  }
                >
                  <ShieldCheck className="mr-1 size-3.5" aria-hidden="true" />
                  {data.validationState === "valid" && !runtimeContextNeedsAttention(data)
                    ? t("runtime.validation.valid")
                    : t("runtime.validation.warning")}
                </Badge>
              </div>

              <div className="grid sm:grid-cols-2 sm:divide-x sm:divide-border">
                <dl className="space-y-5 pb-5 sm:pr-7 sm:pb-0">
                  <RuntimeFact
                    icon={ServerCog}
                    label={t("runtime.card.mode")}
                    value={t(runtimeModeKey(data.runtimeMode))}
                    help={t("runtime.details.modeHelp")}
                  />
                  <RuntimeFact
                    icon={Building2}
                    label={t("runtime.card.environment")}
                    value={data.environmentName}
                    help={t("runtime.details.environmentHelp")}
                  />
                  <RuntimeFact
                    icon={Network}
                    label={t("runtime.card.docker")}
                    value={t(dockerEndpointKindKey(data.dockerEndpointKind))}
                    help={t("runtime.details.dockerHelp")}
                  />
                  <RuntimeFact
                    icon={Link2}
                    label={t("runtime.card.access")}
                    value={exposureDescription}
                    help={t("runtime.details.accessHelp")}
                  />
                  <RuntimeCopyableFact
                    icon={Cpu}
                    label={t("runtime.card.process")}
                    value={data.apiProcessInstanceId}
                    help={t("runtime.details.processHelp")}
                    copyState={fieldCopyState}
                    onCopy={copyRuntimeValue}
                  />
                  {data.commit ? (
                    <RuntimeCopyableFact
                      icon={GitCommitHorizontal}
                      label={t("runtime.details.commit")}
                      value={data.commit}
                      help={t("runtime.details.commitHelp")}
                      copyState={fieldCopyState}
                      onCopy={copyRuntimeValue}
                    />
                  ) : null}
                </dl>

                <dl className="space-y-5 border-t border-border pt-5 sm:border-t-0 sm:pl-7 sm:pt-0">
                  <RuntimeFact
                    icon={Monitor}
                    label={t("runtime.card.uiDelivery")}
                    value={t(uiDeliveryKey(data.uiDeliveryMode))}
                    help={t("runtime.details.deliveryHelp")}
                  />
                  <RuntimeFact
                    icon={Database}
                    label={t("runtime.card.state")}
                    value={t(stateRootLabelKey(data.stateRootKind, data.stateRootProfile))}
                    help={t("runtime.details.stateHelp")}
                  />
                  <RuntimeFact
                    icon={Box}
                    label={t("runtime.card.ownership")}
                    value={t(`runtime.ownership.${data.dockerOwnership.state}` as TranslationKey)}
                    help={t("runtime.details.ownershipHelp")}
                  />
                  <RuntimeCopyableFact
                    icon={Fingerprint}
                    label={t("runtime.card.instance")}
                    value={data.controlPlaneInstanceId}
                    help={t("runtime.details.instanceHelp")}
                    copyState={fieldCopyState}
                    onCopy={copyRuntimeValue}
                  />
                  <RuntimeFact
                    icon={Tag}
                    label={t("runtime.details.version")}
                    value={versionDisplay}
                    help={t("runtime.details.versionHelp")}
                  />
                </dl>
              </div>

              {runtimeContextNeedsAttention(data) ? (
                <div className="rounded-xl border border-amber-500/30 bg-amber-500/[0.05] p-4">
                  <div className="font-medium">{t("runtime.details.attentionTitle")}</div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("runtime.warning.message")}
                  </p>
                  {data.dockerOwnership.warningCode || data.controlPlaneExposure.warningCode || data.warnings.length > 0 ? (
                    <ul className="mt-3 list-disc space-y-1 pl-5 font-mono text-xs text-muted-foreground">
                      {data.dockerOwnership.warningCode ? <li>{data.dockerOwnership.warningCode}</li> : null}
                      {data.controlPlaneExposure.warningCode ? <li>{data.controlPlaneExposure.warningCode}</li> : null}
                      {data.warnings.map((warning) => <li key={warning}>{warning}</li>)}
                    </ul>
                  ) : null}
                </div>
              ) : null}

              <div className="border-t border-border pt-5">
                <div className="flex items-start gap-3">
                  <div className="mt-0.5 inline-flex size-8 shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
                    <Info className="size-4" aria-hidden="true" />
                  </div>
                  <div className="min-w-0">
                    <h3 className="text-sm font-semibold">{t("runtime.details.aboutTitle")}</h3>
                    <p className="mt-1 max-w-2xl text-sm leading-6 text-muted-foreground">
                      {t("runtime.details.aboutDescription")}
                    </p>
                  </div>
                </div>
              </div>
            </div>
          ) : null}
        </div>

        <div className="border-t border-border bg-card px-5 py-4 sm:px-7 sm:py-5">
          <div className="grid gap-3 sm:grid-cols-2">
            <Button
              type="button"
              variant="outline"
              className="h-auto min-h-14 justify-start gap-3 px-4 py-3 text-left"
              aria-label={
                copyState === "copied"
                  ? t("runtime.details.copied")
                  : copyState === "failed"
                    ? t("runtime.details.copyFailed")
                    : t("runtime.details.copy")
              }
              onClick={copyRuntimeContext}
              disabled={!data}
            >
              {copyState === "copied" ? (
                <Check className="size-5" aria-hidden="true" />
              ) : (
                <Clipboard className="size-5" aria-hidden="true" />
              )}
              <span className="min-w-0">
                <span className="block font-semibold">
                  {copyState === "copied"
                    ? t("runtime.details.copied")
                    : copyState === "failed"
                      ? t("runtime.details.copyFailed")
                      : t("runtime.details.copy")}
                </span>
                <span className="mt-0.5 block whitespace-normal text-xs font-normal leading-4 text-muted-foreground">
                  {t("runtime.details.copyDescription")}
                </span>
              </span>
            </Button>

            <DialogPrimitive.Close asChild>
              <Button
                type="button"
                className="h-auto min-h-14 justify-start gap-3 px-4 py-3 text-left"
                aria-label={t("runtime.details.openDiagnostics")}
                onClick={() => navigate("/diagnostics")}
              >
                <ArrowRight className="size-5" aria-hidden="true" />
                <span className="min-w-0">
                  <span className="block font-semibold">{t("runtime.details.openDiagnostics")}</span>
                  <span className="mt-0.5 block whitespace-normal text-xs font-normal leading-4 text-primary-foreground/80">
                    {t("runtime.details.openDiagnosticsDescription")}
                  </span>
                </span>
              </Button>
            </DialogPrimitive.Close>
          </div>
        </div>
      </DialogPrimitive.Content>
    </DialogPrimitive.Portal>
  )
}

function RuntimeFact({
  icon: Icon,
  label,
  value,
  help,
}: {
  icon: LucideIcon
  label: string
  value: string
  help: string
}) {
  return (
    <div className="grid grid-cols-[2rem_minmax(0,1fr)] gap-3">
      <div className="mt-0.5 inline-flex size-8 items-center justify-center rounded-lg bg-muted/70 text-muted-foreground">
        <Icon className="size-4" aria-hidden="true" />
      </div>
      <div className="min-w-0">
        <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {label}
        </dt>
        <dd className="mt-1 text-sm font-medium text-foreground">
          {value}
        </dd>
        <p className="mt-1 text-xs leading-4 text-muted-foreground">{help}</p>
      </div>
    </div>
  )
}

function RuntimeCopyableFact({
  icon: Icon,
  label,
  value,
  help,
  copyState,
  onCopy,
}: {
  icon: LucideIcon
  label: string
  value: string
  help: string
  copyState: FieldCopyState
  onCopy: (field: string, value: string) => Promise<void>
}) {
  const { t } = useI18n()
  const state = copyState?.field === label ? copyState.status : null
  const actionLabel = state === "copied"
    ? t("runtime.details.copiedField", { field: label })
    : state === "failed"
      ? t("runtime.details.copyFieldFailed", { field: label })
      : t("runtime.details.copyField", { field: label })

  return (
    <div className="grid min-w-0 grid-cols-[2rem_minmax(0,1fr)] gap-3">
      <div className="mt-0.5 inline-flex size-8 items-center justify-center rounded-lg bg-muted/70 text-muted-foreground">
        <Icon className="size-4" aria-hidden="true" />
      </div>
      <div className="min-w-0">
        <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {label}
        </dt>
        <dd className="mt-1 flex min-w-0 items-center gap-1.5">
          <code
            className="min-w-0 whitespace-nowrap font-mono text-[11px] font-medium leading-5 tracking-tight text-foreground sm:text-xs"
            title={value}
          >
            {displayRuntimeIdentity(value)}
          </code>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            className="h-6 w-6 shrink-0"
            aria-label={actionLabel}
            title={actionLabel}
            onClick={() => void onCopy(label, value)}
          >
            {state === "copied" ? (
              <Check className="h-3.5 w-3.5" aria-hidden="true" />
            ) : (
              <Clipboard className="h-3.5 w-3.5" aria-hidden="true" />
            )}
          </Button>
        </dd>
        <p className="mt-1 text-xs leading-4 text-muted-foreground">{help}</p>
      </div>
    </div>
  )
}

function displayRuntimeIdentity(value: string) {
  const normalized = value.trim()

  // Control Plane and API process identities are UUIDs. The full-height inspector
  // has enough horizontal space to show these exact values without visual truncation.
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(normalized)) {
    return normalized
  }

  if (normalized.length <= 24) {
    return normalized
  }

  // Longer non-UUID identities (for example source commits) remain compact so the
  // two-column runtime grid stays balanced. Their exact value is still available
  // through the title tooltip and copy action.
  return `${normalized.slice(0, 12)}…${normalized.slice(-8)}`
}
