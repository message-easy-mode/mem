import { useEffect, useState } from "react"
import { useQueryClient } from "@tanstack/react-query"
import { AlertDialog as AlertDialogPrimitive } from "radix-ui"
import { Info, LoaderCircle, Trash2 } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey, TranslationValues } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"

import { isStepUpRequiredRuntimeStackProblem } from "../api/stacks.api"
import type { RuntimeStackDestroyRequest } from "../api/stacks.types"
import {
  runtimeStackKeys,
  useDestroyRuntimeStack,
  useDestroyRuntimeStackOperation,
} from "../hooks/use-runtime-stacks"
import {
  persistTrackedDestroy,
  readTrackedDestroy,
  type TrackedRuntimeStackDestroy,
} from "../lib/destroy-operation-tracking"

type RuntimeStackDestroyTarget = Readonly<{
  stackId: string | null
  slug: string
  displayName?: string | null
}>

type PendingRuntimeStackDestroy = Readonly<{
  slugOrId: string
  request: RuntimeStackDestroyRequest
}>

type CompletedRuntimeStackDestroy = Readonly<{
  slug: string
  request: RuntimeStackDestroyRequest
}>

type StackDestroyControlProps = {
  target?: RuntimeStackDestroyTarget
  onDestroyed?: (completed: CompletedRuntimeStackDestroy) => void
}

/**
 * Owns the existing durable runtime-stack destroy workflow.
 *
 * When a target is supplied it renders the destructive entry point inside the
 * Stack Workspace. Without a target it acts only as a durable operation
 * tracker so the inventory can resume an already-accepted destroy after
 * navigation or refresh without exposing a Delete action there.
 */
export function StackDestroyControl({ target, onDestroyed }: StackDestroyControlProps) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const destroyStack = useDestroyRuntimeStack()
  const [destroyConfirmationOpen, setDestroyConfirmationOpen] = useState(false)
  const [isStepUpOpen, setIsStepUpOpen] = useState(false)
  const [stepUpPending, setStepUpPending] = useState(false)
  const [pendingDestroy, setPendingDestroy] = useState<PendingRuntimeStackDestroy | null>(null)
  const [trackedDestroy, setTrackedDestroy] = useState<TrackedRuntimeStackDestroy | null>(() =>
    readTrackedDestroy(),
  )
  const [destroyPhase, setDestroyPhase] = useState<"tracking" | "failed" | "closed">(() =>
    readTrackedDestroy() ? "tracking" : "closed",
  )
  const [completedDestroy, setCompletedDestroy] = useState<CompletedRuntimeStackDestroy | null>(null)
  const destroyOperation = useDestroyRuntimeStackOperation(trackedDestroy?.operationId ?? null)
  const targetSlug = target?.slug

  useEffect(() => {
    if (!stepUpPending || destroyConfirmationOpen) return

    const timer = window.setTimeout(() => setIsStepUpOpen(true), 0)
    return () => window.clearTimeout(timer)
  }, [destroyConfirmationOpen, stepUpPending])

  useEffect(() => {
    const operation = destroyOperation.data
    if (!trackedDestroy || !operation?.terminal) return

    if (operation.succeeded) {
      const completed: CompletedRuntimeStackDestroy = {
        slug: trackedDestroy.slug,
        request: trackedDestroy.request,
      }

      persistTrackedDestroy(null)
      setTrackedDestroy(null)
      setDestroyPhase("closed")
      setCompletedDestroy(completed)

      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all })
      void queryClient.removeQueries({ queryKey: runtimeStackKeys.detail(completed.slug) })
      void queryClient.removeQueries({ queryKey: runtimeStackKeys.operations(completed.slug) })
      void queryClient.removeQueries({ queryKey: runtimeStackKeys.storage(completed.slug) })
      void queryClient.removeQueries({ queryKey: runtimeStackKeys.users(completed.slug) })
      void queryClient.removeQueries({ queryKey: runtimeStackKeys.doctor(completed.slug) })

      if (!targetSlug || completed.slug === targetSlug) {
        onDestroyed?.(completed)
      }
      return
    }

    setDestroyPhase("failed")
  }, [destroyOperation.data, onDestroyed, queryClient, targetSlug, trackedDestroy])

  function createPartialDestroyRequest(stack: RuntimeStackDestroyTarget): PendingRuntimeStackDestroy {
    return {
      slugOrId: stack.slug,
      request: {
        removeContainers: true,
        removeRoutes: true,
        removeDatabase: false,
        removeFiles: false,
        force: false,
        idempotencyKey: `destroy-stack-${stack.stackId ?? stack.slug}-${crypto.randomUUID()}`,
      },
    }
  }

  function performDestroy(pending: PendingRuntimeStackDestroy) {
    destroyStack.mutate(pending, {
      onSuccess: (accepted) => {
        const tracked: TrackedRuntimeStackDestroy = {
          operationId: accepted.operationId,
          runtimeStackId: accepted.runtimeStackId,
          slug: accepted.slug,
          request: pending.request,
        }

        persistTrackedDestroy(tracked)
        setTrackedDestroy(tracked)
        setDestroyPhase("tracking")
        setCompletedDestroy(null)
        setDestroyConfirmationOpen(false)
        setPendingDestroy(null)
        setStepUpPending(false)
        setIsStepUpOpen(false)
      },
      onError: (error) => {
        if (!isStepUpRequiredRuntimeStackProblem(error)) return

        destroyStack.reset()
        setDestroyConfirmationOpen(false)
        setPendingDestroy(pending)
        setStepUpPending(true)
      },
    })
  }

  function beginDestroyConfirmation() {
    if (!target) return

    destroyStack.reset()
    setCompletedDestroy(null)
    setPendingDestroy(null)
    setStepUpPending(false)
    setIsStepUpOpen(false)
    setDestroyConfirmationOpen(true)
  }

  function changeDestroyConfirmationOpen(open: boolean) {
    if (open) return
    destroyStack.reset()
    setDestroyConfirmationOpen(false)
  }

  function confirmDestroy() {
    if (!target) return
    performDestroy(createPartialDestroyRequest(target))
  }

  function changeStepUpOpen(open: boolean) {
    setIsStepUpOpen(open)
    if (!open) {
      setStepUpPending(false)
      setPendingDestroy(null)
    }
  }

  function resumePendingDestroyAfterStepUp() {
    const pending = pendingDestroy
    setPendingDestroy(null)
    setStepUpPending(false)
    setIsStepUpOpen(false)
    if (pending) performDestroy(pending)
  }

  function closeDestroyFailure() {
    destroyStack.reset()
    persistTrackedDestroy(null)
    setTrackedDestroy(null)
    setDestroyPhase("closed")
  }

  const destroyingTarget = Boolean(target && trackedDestroy?.slug === target.slug)

  return (
    <>
      {target ? (
        <Button
          type="button"
          variant="destructive"
          size="sm"
          aria-label={t("stacks.overview.deleteAction")}
          onClick={beginDestroyConfirmation}
          disabled={destroyStack.isPending || trackedDestroy !== null}
        >
          <Trash2 className="mr-2 h-4 w-4" aria-hidden="true" />
          {destroyingTarget ? t("stacks.list.deleting") : t("stacks.list.delete")}
        </Button>
      ) : null}

      {!target && completedDestroy ? (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <Trash2 className="h-4 w-4" />
          <AlertTitle>{t("stacks.list.destroyedTitle")}</AlertTitle>
          <AlertDescription>
            {completedDestroy.request.removeFiles || completedDestroy.request.removeDatabase
              ? t("stacks.list.destroyedWithDataHandled", { slug: completedDestroy.slug })
              : t("stacks.list.destroyedWithDataKept", { slug: completedDestroy.slug })}
          </AlertDescription>
        </Alert>
      ) : null}

      <DestroyProgressDialog
        open={destroyPhase !== "closed"}
        phase={destroyPhase === "failed" ? "failed" : "destroying"}
        slug={trackedDestroy?.slug ?? ""}
        operationId={trackedDestroy?.operationId ?? null}
        currentStep={destroyOperation.data?.currentStep ?? "queued"}
        statusUnavailable={Boolean(destroyOperation.error)}
        onClose={closeDestroyFailure}
      />

      {target ? (
        <>
          <ConfirmationDialog
            open={destroyConfirmationOpen}
            onOpenChange={changeDestroyConfirmationOpen}
            title={t("stacks.list.destroyConfirm.title", { slug: target.slug })}
            description={t("stacks.list.destroyConfirm.containersAndRoutes")}
            confirmLabel={t("stacks.list.destroyConfirm.action")}
            confirmingLabel={t("stacks.list.destroyConfirm.confirming")}
            cancelLabel={t("stepUp.cancel")}
            confirmVariant="destructive"
            onConfirm={confirmDestroy}
            isConfirming={destroyStack.isPending}
          >
            <div className="rounded-md border p-3 text-sm">
              <div className="font-medium break-all">{target.slug}</div>
              <div className="mt-1 text-muted-foreground">
                {t("stacks.list.destroyConfirm.dataKept")}
              </div>
            </div>
            <Alert variant="destructive">
              <Trash2 className="h-4 w-4" />
              <AlertTitle>{t("stacks.list.destroyConfirm.impactTitle")}</AlertTitle>
              <AlertDescription>{t("stacks.list.destroyConfirm.containersAndRoutes")}</AlertDescription>
            </Alert>
            <Alert>
              <AlertTitle>{t("stacks.list.destroyConfirm.dataKeptTitle")}</AlertTitle>
              <AlertDescription>{t("stacks.list.destroyConfirm.strongerOptions")}</AlertDescription>
            </Alert>
            {destroyStack.error && !isStepUpRequiredRuntimeStackProblem(destroyStack.error) ? (
              <Alert variant="destructive">
                <AlertTitle>{t("stacks.list.destroyErrorTitle")}</AlertTitle>
                <AlertDescription>{destroyStack.error.message}</AlertDescription>
              </Alert>
            ) : null}
          </ConfirmationDialog>

          <OperatorStepUpDialog
            open={isStepUpOpen}
            onOpenChange={changeStepUpOpen}
            onVerified={resumePendingDestroyAfterStepUp}
          />
        </>
      ) : null}
    </>
  )
}

function DestroyProgressDialog({
  open,
  phase,
  slug,
  operationId,
  currentStep,
  statusUnavailable,
  onClose,
}: {
  open: boolean
  phase: "destroying" | "failed"
  slug: string
  operationId: string | null
  currentStep: string | null
  statusUnavailable: boolean
  onClose: () => void
}) {
  const { t } = useI18n()
  const isDestroying = phase === "destroying"

  return (
    <AlertDialogPrimitive.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (!nextOpen && !isDestroying) onClose()
      }}
    >
      <AlertDialogPrimitive.Portal>
        <AlertDialogPrimitive.Overlay className="fixed inset-0 z-50 bg-black/70 backdrop-blur-[1px]" />
        <AlertDialogPrimitive.Content
          className="fixed top-1/2 left-1/2 z-50 grid w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 gap-5 rounded-xl border border-border bg-card p-6 text-card-foreground shadow-2xl outline-none"
          onEscapeKeyDown={(event) => {
            if (isDestroying) event.preventDefault()
          }}
        >
          <div className="flex items-start gap-4">
            <div className="rounded-full bg-destructive/10 p-3 text-destructive">
              {isDestroying ? (
                <LoaderCircle className="h-6 w-6 animate-spin" aria-hidden="true" />
              ) : (
                <Info className="h-6 w-6" aria-hidden="true" />
              )}
            </div>
            <div className="min-w-0 space-y-2">
              <AlertDialogPrimitive.Title className="text-lg font-semibold tracking-tight">
                {isDestroying
                  ? t("stacks.list.destroyProgress.title")
                  : t("stacks.list.destroyProgress.failureTitle")}
              </AlertDialogPrimitive.Title>
              <AlertDialogPrimitive.Description className="text-sm leading-relaxed text-muted-foreground">
                {isDestroying
                  ? t("stacks.list.destroyProgress.description")
                  : t("stacks.list.destroyProgress.failureDescription")}
              </AlertDialogPrimitive.Description>
            </div>
          </div>

          <div className="rounded-lg border bg-muted/20 p-4 text-sm">
            <div className="grid grid-cols-[110px_minmax(0,1fr)] gap-3">
              <span className="text-muted-foreground">{t("stacks.list.destroyProgress.stack")}</span>
              <span className="break-words font-medium">{slug}</span>
            </div>
          </div>

          {operationId ? (
            <div className="rounded-lg border bg-muted/10 p-4 text-sm">
              <div className="font-medium">{destroyStepLabel(t, currentStep)}</div>
              <div className="mt-1 break-all text-xs text-muted-foreground">
                {t("stacks.list.destroyProgress.operationReference", { operationId })}
              </div>
              {statusUnavailable ? (
                <div className="mt-2 text-xs text-amber-500">
                  {t("stacks.list.destroyProgress.reconnecting")}
                </div>
              ) : null}
            </div>
          ) : null}

          {isDestroying ? (
            <p className="text-xs text-muted-foreground">
              {t("stacks.list.destroyProgress.safeToLeave")}
            </p>
          ) : (
            <div className="space-y-3">
              {operationId && currentStep ? (
                <p className="text-sm text-muted-foreground">
                  {t("stacks.list.destroyProgress.failureStage", {
                    stage: destroyStepLabel(t, currentStep),
                  })}
                </p>
              ) : null}
              <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                <Button type="button" variant="outline" onClick={onClose}>
                  {t("stacks.list.destroyProgress.close")}
                </Button>
                <Button type="button" asChild>
                  <Link to="/diagnostics/logs?tab=incidents">
                    {t("stacks.list.destroyProgress.openDiagnostics")}
                  </Link>
                </Button>
              </div>
            </div>
          )}
        </AlertDialogPrimitive.Content>
      </AlertDialogPrimitive.Portal>
    </AlertDialogPrimitive.Root>
  )
}

function destroyStepLabel(
  t: (key: TranslationKey, values?: TranslationValues) => string,
  step: string | null,
) {
  const keyByStep: Readonly<Record<string, TranslationKey>> = {
    queued: "stacks.list.destroyStage.queued",
    validate: "stacks.list.destroyStage.validate",
    "remove-matrix-route": "stacks.list.destroyStage.removeMatrixRoute",
    "remove-element-route": "stacks.list.destroyStage.removeElementRoute",
    "remove-matrix-container": "stacks.list.destroyStage.removeMatrixContainer",
    "remove-element-container": "stacks.list.destroyStage.removeElementContainer",
    "remove-database": "stacks.list.destroyStage.removeDatabase",
    "retain-database": "stacks.list.destroyStage.retainDatabase",
    "remove-files": "stacks.list.destroyStage.removeFiles",
    "retain-files": "stacks.list.destroyStage.retainFiles",
    "mark-runtime-stack-destroyed": "stacks.list.destroyStage.markDestroyed",
    "delete-manifest": "stacks.list.destroyStage.deleteManifest",
    complete: "stacks.list.destroyStage.complete",
  }

  if (!step) return t("stacks.list.destroyStage.pending")
  const key = keyByStep[step]
  return key ? t(key) : step
}
