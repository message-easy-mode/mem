import { useEffect, useMemo, useRef, useState } from "react"
import { AlertDialog as AlertDialogPrimitive } from "radix-ui"
import { Link, useNavigate } from "react-router-dom"
import {
  ArrowLeft,
  Box,
  Globe2,
  Info,
  LoaderCircle,
  UserRound,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey, TranslationValues } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select } from "@/components/ui/select"
import { useOperatorDomains } from "@/features/shared/domains/hooks/use-domains"
import { normalizeSlug } from "../api/stacks.api"
import type { CreateRuntimeStackRequest } from "../api/stacks.types"
import {
  useCreateRuntimeStack,
  useCreateRuntimeStackOperation,
  useRuntimeStackImagePolicy,
} from "../hooks/use-runtime-stacks"

const TRACKED_CREATION_STORAGE_KEY = "mem.stack-create.pending-operation"
const INITIAL_DISPLAY_NAME = "Demo Stack"
const INITIAL_STACK_SLUG = "demo-stack"

type TrackedCreation = Readonly<{
  operationId: string
  stackId: string
  stackSlug: string
  displayName: string
  domain: string
}>

function readTrackedCreation(): TrackedCreation | null {
  try {
    const raw = window.localStorage.getItem(TRACKED_CREATION_STORAGE_KEY)
    if (!raw) return null
    const value = JSON.parse(raw) as Partial<TrackedCreation>
    if (
      typeof value.operationId !== "string" ||
      typeof value.stackId !== "string" ||
      typeof value.stackSlug !== "string" ||
      typeof value.displayName !== "string" ||
      typeof value.domain !== "string"
    ) {
      return null
    }
    return value as TrackedCreation
  } catch {
    return null
  }
}

function persistTrackedCreation(value: TrackedCreation | null) {
  try {
    if (value) {
      window.localStorage.setItem(TRACKED_CREATION_STORAGE_KEY, JSON.stringify(value))
    } else {
      window.localStorage.removeItem(TRACKED_CREATION_STORAGE_KEY)
    }
  } catch {
    // Progress tracking remains functional for the current page even when storage is unavailable.
  }
}

export function StackCreatePage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const createStack = useCreateRuntimeStack()
  const domainsQuery = useOperatorDomains()
  const imagePolicyQuery = useRuntimeStackImagePolicy()

  const [slug, setSlug] = useState(INITIAL_STACK_SLUG)
  const [displayName, setDisplayName] = useState(INITIAL_DISPLAY_NAME)
  const [slugInputMode, setSlugInputMode] = useState<
    "pristine" | "auto-seeded" | "manual"
  >("pristine")
  const displayNameWasEdited = useRef(false)
  const [category, setCategory] = useState("")
  const [selectedDomainId, setSelectedDomainId] = useState("")
  const [trackedCreation, setTrackedCreation] = useState<TrackedCreation | null>(
    () => readTrackedCreation(),
  )
  const [creationPhase, setCreationPhase] = useState<
    "closed" | "accepting" | "tracking" | "failed"
  >(() => (readTrackedCreation() ? "tracking" : "closed"))
  const [creationContext, setCreationContext] = useState<{
    displayName: string
    slug: string
    domain: string
  } | null>(() => {
    const tracked = readTrackedCreation()
    return tracked
      ? {
          displayName: tracked.displayName,
          slug: tracked.stackSlug,
          domain: tracked.domain,
        }
      : null
  })

  const creationOperation = useCreateRuntimeStackOperation(
    trackedCreation?.operationId ?? null,
  )

  const normalizedSlug = useMemo(() => normalizeSlug(slug), [slug])
  const domains = domainsQuery.data ?? []
  const defaultDomain =
    domains.find((domain) => domain.isMainPlatformDomain) ?? domains[0] ?? null
  const effectiveDomainId = selectedDomainId || defaultDomain?.id || ""
  const selectedDomain =
    domains.find((domain) => domain.id === effectiveDomainId) ?? defaultDomain
  const matrixPublicUrl =
    normalizedSlug && selectedDomain?.baseDomain
      ? `https://matrix-${normalizedSlug}.${selectedDomain.baseDomain}`
      : t("stacks.create.domainUnavailable")
  const elementPublicUrl =
    normalizedSlug && selectedDomain?.baseDomain
      ? `https://chat-${normalizedSlug}.${selectedDomain.baseDomain}`
      : t("stacks.create.domainUnavailable")
  const canSubmit =
    normalizedSlug.length > 0 &&
    effectiveDomainId.length > 0 &&
    !domainsQuery.isLoading &&
    imagePolicyQuery.isSuccess &&
    !createStack.isPending &&
    trackedCreation === null

  const updateDisplayName = (value: string) => {
    displayNameWasEdited.current = true
    setDisplayName(value)

    if (slugInputMode === "pristine") {
      setSlug(normalizeSlug(value))
    }
  }

  const finishInitialSlugSuggestion = () => {
    if (
      slugInputMode === "pristine" &&
      displayNameWasEdited.current &&
      normalizeSlug(displayName).length > 0
    ) {
      setSlugInputMode("auto-seeded")
    }
  }

  useEffect(() => {
    const operation = creationOperation.data
    if (!trackedCreation || !operation?.terminal) {
      return
    }

    if (operation.succeeded) {
      const completedSlug = trackedCreation.stackSlug
      persistTrackedCreation(null)
      setTrackedCreation(null)
      setCreationPhase("closed")
      setCreationContext(null)
      navigate(`/stacks/${encodeURIComponent(completedSlug)}`)
      return
    }

    setCreationPhase("failed")
  }, [creationOperation.data, navigate, trackedCreation])

  async function runCreation(
    request: CreateRuntimeStackRequest,
    context: { displayName: string; slug: string; domain: string },
  ) {
    createStack.reset()
    setCreationContext(context)
    setCreationPhase("accepting")

    try {
      const accepted = await createStack.mutateAsync(request)
      const tracked: TrackedCreation = {
        operationId: accepted.operationId,
        stackId: accepted.stackId,
        stackSlug: accepted.stackSlug,
        displayName: context.displayName,
        domain: context.domain,
      }
      persistTrackedCreation(tracked)
      setTrackedCreation(tracked)
      setCreationPhase("tracking")
    } catch {
      setCreationPhase("failed")
    }
  }

  function onSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!canSubmit) {
      return
    }

    const request: CreateRuntimeStackRequest = {
      slug: normalizedSlug,
      displayName,
      category: category.trim() || null,
      requestedDomainId: effectiveDomainId,
    }

    void runCreation(request, {
      displayName: displayName || normalizedSlug,
      slug: normalizedSlug,
      domain:
        selectedDomain?.baseDomain ?? t("stacks.create.domainUnavailable"),
    })
  }

  function closeFailure() {
    createStack.reset()
    persistTrackedCreation(null)
    setTrackedCreation(null)
    setCreationPhase("closed")
    setCreationContext(null)
  }

  return (
    <div data-stack-create-page className="mx-auto w-full max-w-[1120px] space-y-5">
      <div>
        <Button variant="ghost" size="sm" asChild className="mb-3 -ml-2">
          <Link to="/stacks">
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("stacks.create.back")}
          </Link>
        </Button>
        <h1 className="text-2xl font-semibold tracking-tight">
          {t("stacks.create.title")}
        </h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("stacks.create.description")}
        </p>
      </div>

      <form onSubmit={onSubmit}>
        <div
          data-stack-create-layout
          className="grid items-start gap-5 min-[900px]:grid-cols-[minmax(320px,1fr)_minmax(360px,1fr)] 2xl:grid-cols-[minmax(300px,1.05fr)_minmax(250px,0.78fr)_minmax(330px,1.05fr)] 2xl:gap-4"
        >
          <Card data-stack-create-identity>
            <CardHeader>
              <CardTitle>{t("stacks.create.identityTitle")}</CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("stacks.create.identityDescription")}
              </p>
            </CardHeader>
            <CardContent className="space-y-5">
              <Field
                label={t("stacks.create.displayName")}
                htmlFor="displayName"
                helper={t("stacks.create.displayNameHelper")}
              >
                <Input
                  id="displayName"
                  value={displayName}
                  onChange={(event) => updateDisplayName(event.target.value)}
                  onBlur={finishInitialSlugSuggestion}
                  placeholder={t("stacks.create.displayNamePlaceholder")}
                  maxLength={200}
                />
              </Field>

              <Field
                label={t("stacks.create.category")}
                htmlFor="category"
                helper={t("stacks.create.categoryHelper")}
              >
                <Input
                  id="category"
                  value={category}
                  onChange={(event) => setCategory(event.target.value)}
                  placeholder={t("stacks.create.categoryPlaceholder")}
                  maxLength={40}
                />
              </Field>

              <Field
                label={t("stacks.create.slug")}
                htmlFor="slug"
                helper={t("stacks.create.slugHelper")}
              >
                <Input
                  id="slug"
                  value={slug}
                  onChange={(event) => {
                    setSlug(event.target.value)
                    setSlugInputMode("manual")
                  }}
                  placeholder="demo-stack"
                  required
                />
                {slug !== normalizedSlug && (
                  <div className="text-xs text-muted-foreground">
                    {t("stacks.create.normalizedSlug", {
                      slug: normalizedSlug || t("stacks.create.invalidSlug"),
                    })}
                  </div>
                )}
              </Field>

              <Field
                label={t("stacks.create.domain")}
                htmlFor="domain"
                helper={t("stacks.create.domainHelper")}
              >
                <Select
                  id="domain"
                  value={effectiveDomainId}
                  onChange={(event) => setSelectedDomainId(event.target.value)}
                  disabled={domainsQuery.isLoading || domains.length === 0}
                  wrapperClassName="w-full"
                  className="w-full"
                >
                  {domainsQuery.isLoading ? (
                    <option value="">{t("stacks.create.domainLoading")}</option>
                  ) : domains.length === 0 ? (
                    <option value="">{t("stacks.create.domainEmpty")}</option>
                  ) : (
                    domains.map((domain) => (
                      <option key={domain.id} value={domain.id}>
                        {domain.baseDomain}
                        {domain.isMainPlatformDomain
                          ? ` (${t("stacks.create.defaultDomain")})`
                          : ""}
                      </option>
                    ))
                  )}
                </Select>
                {domainsQuery.error ? (
                  <p className="text-xs text-destructive">
                    {t("stacks.create.domainError")}
                  </p>
                ) : null}
              </Field>
            </CardContent>
          </Card>

          <Card
            data-stack-create-images
            className="min-[900px]:col-start-1 min-[900px]:row-start-2 2xl:col-start-2 2xl:row-start-1"
          >
            <CardHeader>
              <CardTitle>{t("stacks.create.imagesTitle")}</CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("stacks.create.imagesDescription")}
              </p>
            </CardHeader>
            <CardContent className="space-y-5">
              {imagePolicyQuery.isLoading ? (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <LoaderCircle className="h-4 w-4 animate-spin" />
                  {t("stacks.create.imagesLoading")}
                </div>
              ) : imagePolicyQuery.error ? (
                <Alert variant="destructive">
                  <AlertTitle>{t("stacks.create.imagesErrorTitle")}</AlertTitle>
                  <AlertDescription>{t("stacks.create.imagesError")}</AlertDescription>
                </Alert>
              ) : imagePolicyQuery.data ? (
                <>
                  <ManagedImageRow
                    imageLabel={t("stacks.create.matrixImage")}
                    versionLabel={t("stacks.create.version")}
                    repository={imagePolicyQuery.data.synapse.repository}
                    version={imagePolicyQuery.data.synapse.version}
                  />
                  <ManagedImageRow
                    imageLabel={t("stacks.create.elementImage")}
                    versionLabel={t("stacks.create.version")}
                    repository={imagePolicyQuery.data.element.repository}
                    version={imagePolicyQuery.data.element.version}
                  />
                  <div className="rounded-md border border-border/70 bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
                    {t("stacks.create.imagesManagedNote")}
                  </div>
                </>
              ) : null}
            </CardContent>
          </Card>

          <Card
            data-stack-create-summary
            className="min-[900px]:col-start-2 min-[900px]:row-start-1 min-[900px]:row-span-2 2xl:col-start-3 2xl:row-start-1 2xl:row-span-2"
          >
            <CardHeader>
              <CardTitle>{t("stacks.create.summaryTitle")}</CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("stacks.create.summaryDescription")}
              </p>
            </CardHeader>
            <CardContent className="space-y-6">
              <SummarySection
                icon={<UserRound className="h-4 w-4" />}
                title={t("stacks.create.summaryIdentity")}
              >
                <SummaryRow
                  label={t("stacks.create.displayName")}
                  value={displayName || "—"}
                />
                {category.trim() ? (
                  <SummaryRow
                    label={t("stacks.create.category")}
                    value={category.trim()}
                  />
                ) : null}
                <SummaryRow
                  label={t("stacks.create.slug")}
                  value={normalizedSlug || "—"}
                />
                <SummaryRow
                  label={t("stacks.create.domain")}
                  value={
                    selectedDomain?.baseDomain ??
                    t("stacks.create.domainUnavailable")
                  }
                  detail={
                    selectedDomain?.isMainPlatformDomain
                      ? t("stacks.create.defaultDomain")
                      : undefined
                  }
                />
              </SummarySection>

              <SummarySection
                icon={<Box className="h-4 w-4" />}
                title={t("stacks.create.summaryImages")}
              >
                <SummaryRow
                  label={t("stacks.create.matrixImage")}
                  value={imagePolicyQuery.data
                    ? `${imagePolicyQuery.data.synapse.repository} · ${imagePolicyQuery.data.synapse.version}`
                    : t("stacks.create.imagesManagedValue")}
                />
                <SummaryRow
                  label={t("stacks.create.elementImage")}
                  value={imagePolicyQuery.data
                    ? `${imagePolicyQuery.data.element.repository} · ${imagePolicyQuery.data.element.version}`
                    : t("stacks.create.imagesManagedValue")}
                />
              </SummarySection>

              <SummarySection
                icon={<Globe2 className="h-4 w-4" />}
                title={t("stacks.create.summaryNetwork")}
              >
                <SummaryRow
                  label={t("stacks.create.matrixPublicUrl")}
                  value={matrixPublicUrl}
                  layout="stacked"
                />
                <SummaryRow
                  label={t("stacks.create.elementPublicUrl")}
                  value={elementPublicUrl}
                  layout="stacked"
                />
                <SummaryRow
                  label={t("stacks.create.publicAccess")}
                  value={t("stacks.create.automaticRecommended")}
                />
                <SummaryRow
                  label={t("stacks.create.ports")}
                  value={t("stacks.create.automaticRecommended")}
                />
              </SummarySection>
            </CardContent>
          </Card>

          <Alert
            data-stack-create-notice
            className="border-emerald-500/30 bg-emerald-500/5 min-[900px]:col-span-2 min-[900px]:col-start-1 min-[900px]:row-start-3 2xl:col-span-2 2xl:col-start-1 2xl:row-start-2"
          >
            <Info className="h-4 w-4 text-emerald-400" />
            <AlertTitle>{t("stacks.create.localNoticeTitle")}</AlertTitle>
            <AlertDescription>
              {t("stacks.create.localNoticeDescription")}
            </AlertDescription>
          </Alert>
        </div>

        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t pt-5">
          <Button variant="outline" type="button" asChild>
            <Link to="/stacks">{t("stacks.create.cancel")}</Link>
          </Button>
          <Button type="submit" disabled={!canSubmit}>
            {createStack.isPending
              ? t("stacks.create.creating")
              : t("stacks.create.submit")}
          </Button>
        </div>
      </form>

      <CreationProgressDialog
        open={creationPhase !== "closed"}
        phase={creationPhase === "failed" ? "failed" : "creating"}
        displayName={creationContext?.displayName ?? displayName}
        domain={
          creationContext?.domain ??
          selectedDomain?.baseDomain ??
          t("stacks.create.domainUnavailable")
        }
        operationId={trackedCreation?.operationId ?? null}
        currentStep={creationOperation.data?.currentStep ??
          (creationPhase === "accepting" ? "accepting" : "queued")}
        statusUnavailable={Boolean(creationOperation.error)}
        onClose={closeFailure}
      />
    </div>
  )
}

function CreationProgressDialog({
  open,
  phase,
  displayName,
  domain,
  operationId,
  currentStep,
  statusUnavailable,
  onClose,
}: {
  open: boolean
  phase: "creating" | "failed"
  displayName: string
  domain: string
  operationId: string | null
  currentStep: string | null
  statusUnavailable: boolean
  onClose: () => void
}) {
  const { t } = useI18n()
  const isCreating = phase === "creating"

  return (
    <AlertDialogPrimitive.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen && !isCreating) {
          onClose()
        }
      }}
    >
      <AlertDialogPrimitive.Portal>
        <AlertDialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/70 backdrop-blur-[1px]" />
        <AlertDialogPrimitive.Content
          className="fixed top-1/2 left-1/2 z-50 grid w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 gap-5 rounded-xl border border-border bg-card p-6 text-card-foreground shadow-2xl outline-none"
          onEscapeKeyDown={(event) => {
            if (isCreating) {
              event.preventDefault()
            }
          }}
        >
          <div className="flex items-start gap-4">
            <div
              className={
                isCreating
                  ? "rounded-full bg-primary/10 p-3 text-primary"
                  : "rounded-full bg-destructive/10 p-3 text-destructive"
              }
            >
              {isCreating ? (
                <LoaderCircle
                  className="h-6 w-6 animate-spin"
                  aria-hidden="true"
                />
              ) : (
                <Info className="h-6 w-6" aria-hidden="true" />
              )}
            </div>
            <div className="min-w-0 space-y-2">
              <AlertDialogPrimitive.Title className="text-lg font-semibold tracking-tight">
                {isCreating
                  ? t("stacks.create.progressTitle")
                  : t("stacks.create.failureTitle")}
              </AlertDialogPrimitive.Title>
              <AlertDialogPrimitive.Description className="text-sm leading-relaxed text-muted-foreground">
                {isCreating
                  ? t("stacks.create.progressDescription")
                  : operationId
                    ? t("stacks.create.failureDescription")
                    : t("stacks.create.failureAcceptanceDescription")}
              </AlertDialogPrimitive.Description>
            </div>
          </div>

          <div className="rounded-lg border bg-muted/20 p-4 text-sm">
            <div className="grid grid-cols-[110px_minmax(0,1fr)] gap-3">
              <span className="text-muted-foreground">
                {t("stacks.create.displayName")}
              </span>
              <span className="break-words font-medium">{displayName}</span>
              <span className="text-muted-foreground">
                {t("stacks.create.domain")}
              </span>
              <span className="break-words font-medium">{domain}</span>
            </div>
          </div>

          {operationId ? (
            <div className="rounded-lg border bg-muted/10 p-4 text-sm">
              <div className="font-medium">{creationStepLabel(t, currentStep)}</div>
              <div className="mt-1 break-all text-xs text-muted-foreground">
                {t("stacks.create.operationReference", { operationId })}
              </div>
              {statusUnavailable ? (
                <div className="mt-2 text-xs text-amber-500">
                  {t("stacks.create.progressReconnecting")}
                </div>
              ) : null}
            </div>
          ) : null}

          {isCreating ? (
            <p className="text-xs text-muted-foreground">
              {operationId
                ? t("stacks.create.progressSafeToLeave")
                : t("stacks.create.progressDoNotClose")}
            </p>
          ) : (
            <div className="space-y-3">
              {operationId && currentStep ? (
                <p className="text-sm text-muted-foreground">
                  {t("stacks.create.failureStage", {
                    stage: creationStepLabel(t, currentStep),
                  })}
                </p>
              ) : null}
              {currentStep === "require-platform-turn" ? (
                <Alert className="border-amber-500/30 bg-amber-500/5">
                  <AlertTitle>{t("stacks.create.failureTurnRequiredTitle")}</AlertTitle>
                  <AlertDescription>
                    {t("stacks.create.failureTurnRequiredDescription")}
                  </AlertDescription>
                </Alert>
              ) : null}
              <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                <Button type="button" variant="outline" onClick={onClose}>
                  {t("stacks.create.failureClose")}
                </Button>
                {currentStep === "require-platform-turn" ? (
                  <Button type="button" variant="secondary" asChild>
                    <Link to="/services/coturn">
                      {t("stacks.create.failureOpenPlatformTurn")}
                    </Link>
                  </Button>
                ) : null}
                {operationId ? (
                  <Button type="button" asChild>
                    <Link to="/diagnostics/logs?tab=incidents">
                      {t("stacks.create.failureOpenDiagnostics")}
                    </Link>
                  </Button>
                ) : null}
              </div>
            </div>
          )}
        </AlertDialogPrimitive.Content>
      </AlertDialogPrimitive.Portal>
    </AlertDialogPrimitive.Root>
  )
}

function creationStepLabel(
  t: (key: TranslationKey, values?: TranslationValues) => string,
  step: string | null,
) {
  const keyByStep: Readonly<Record<string, TranslationKey>> = {
    accepting: "stacks.create.stage.accepting",
    queued: "stacks.create.stage.queued",
    "resolve-domain": "stacks.create.stage.resolveDomain",
    "plan-runtime": "stacks.create.stage.planRuntime",
    "require-platform-turn": "stacks.create.stage.requirePlatformTurn",
    "resolve-secrets": "stacks.create.stage.resolveSecrets",
    "provision-database": "stacks.create.stage.provisionDatabase",
    "prepare-filesystem": "stacks.create.stage.prepareFilesystem",
    "resolve-turn": "stacks.create.stage.resolveTurn",
    "generate-synapse-config": "stacks.create.stage.generateSynapse",
    "verify-turn-config": "stacks.create.stage.verifyTurnConfig",
    "start-matrix": "stacks.create.stage.startMatrix",
    "write-element-config": "stacks.create.stage.writeElement",
    "start-element": "stacks.create.stage.startElement",
    "publish-matrix-route": "stacks.create.stage.publishMatrix",
    "publish-element-route": "stacks.create.stage.publishElement",
    "verify-readiness": "stacks.create.stage.verifyReadiness",
    "persist-manifest": "stacks.create.stage.persistManifest",
    "persist-database-ownership": "stacks.create.stage.persistOwnership",
    "persist-stack-secret": "stacks.create.stage.persistSecret",
    "attach-runtime-stack": "stacks.create.stage.attachStack",
    complete: "stacks.create.stage.complete",
  }

  if (!step) {
    return t("stacks.create.stage.pending")
  }

  const key = keyByStep[step]
  return key ? t(key) : step
}

function ManagedImageRow({
  imageLabel,
  versionLabel,
  repository,
  version,
}: {
  imageLabel: string
  versionLabel: string
  repository: string
  version: string
}) {
  return (
    <div data-managed-image-row className="space-y-3">
      <div className="space-y-2">
        <Label>{imageLabel}</Label>
        <div className="min-h-9 break-words rounded-md border bg-muted/30 px-3 py-2 text-sm">
          {repository}
        </div>
      </div>
      <div data-managed-image-version className="space-y-2">
        <Label>{versionLabel}</Label>
        <div className="min-h-9 w-fit min-w-24 whitespace-nowrap rounded-md border bg-muted/30 px-3 py-2 text-center text-sm font-medium tabular-nums">
          {version}
        </div>
      </div>
    </div>
  )
}

function Field({
  label,
  htmlFor,
  helper,
  children,
}: {
  label: string
  htmlFor: string
  helper?: string
  children: React.ReactNode
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor={htmlFor}>{label}</Label>
      {children}
      {helper && <p className="text-xs text-muted-foreground">{helper}</p>}
    </div>
  )
}

function SummarySection({
  icon,
  title,
  children,
}: {
  icon: React.ReactNode
  title: string
  children: React.ReactNode
}) {
  return (
    <section className="space-y-3 border-b pb-5 last:border-b-0 last:pb-0">
      <div className="flex items-center gap-2 font-medium text-emerald-400">
        {icon}
        <h2>{title}</h2>
      </div>
      <div className="space-y-2 pl-6">{children}</div>
    </section>
  )
}

function SummaryRow({
  label,
  value,
  detail,
  layout = "split",
}: {
  label: string
  value: string
  detail?: string
  layout?: "split" | "stacked"
}) {
  const valueContent = (
    <>
      {value}
      {detail ? (
        <span className="mt-0.5 block text-xs font-normal text-emerald-400">
          {detail}
        </span>
      ) : null}
    </>
  )

  if (layout === "stacked") {
    return (
      <div className="min-w-0 space-y-1 text-sm">
        <span className="block text-xs text-muted-foreground">{label}</span>
        <span className="block min-w-0 break-all font-medium leading-5">
          {valueContent}
        </span>
      </div>
    )
  }

  return (
    <div className="grid grid-cols-1 gap-1 text-sm sm:grid-cols-[minmax(0,1fr)_minmax(0,1.25fr)] sm:gap-3">
      <span className="text-muted-foreground">{label}</span>
      <span className="min-w-0 break-words font-medium">{valueContent}</span>
    </div>
  )
}
