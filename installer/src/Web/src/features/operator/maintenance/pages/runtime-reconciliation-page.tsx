import { useState } from "react"
import { useNavigate } from "react-router-dom"
import {
  AlertTriangle,
  CheckCircle2,
  RefreshCw,
  ShieldAlert,
  Trash2,
  Wrench,
} from "lucide-react"

import { formatDateTime, formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { isStepUpRequiredRuntimeStackProblem } from "@/features/operator/stacks/api/stacks.api"
import type { RuntimeStackDestroyRequest } from "@/features/operator/stacks/api/stacks.types"
import { useDestroyRuntimeStack } from "@/features/operator/stacks/hooks/use-runtime-stacks"
import { persistTrackedDestroy } from "@/features/operator/stacks/lib/destroy-operation-tracking"

import { isStepUpRequiredRuntimeReconciliationProblem } from "../api/runtime-reconciliation.api"
import type {
  RuntimeReconciliationActiveStack,
  RuntimeReconciliationNpmProxyHost,
} from "../api/runtime-reconciliation.types"
import {
  useDeleteSelectedOrphanedNpmProxyHosts,
  useRuntimeReconciliationReport,
} from "../hooks/use-runtime-reconciliation"

const CLEANUP_CONFIRMATION = "DELETE ORPHANED NPM HOSTS"

type PendingReconciliationRemoval = Readonly<{
  stack: RuntimeReconciliationActiveStack
  request: RuntimeStackDestroyRequest
}>

export function RuntimeReconciliationPage() {
  const { language, t } = useI18n()
  const navigate = useNavigate()
  const report = useRuntimeReconciliationReport()
  const cleanup = useDeleteSelectedOrphanedNpmProxyHosts()
  const removeUnlistedStack = useDestroyRuntimeStack()
  const [selectedProxyHostIds, setSelectedProxyHostIds] = useState<number[]>([])
  const [confirmOpen, setConfirmOpen] = useState(false)
  const [confirmationText, setConfirmationText] = useState("")
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [removalConfirmation, setRemovalConfirmation] =
    useState<RuntimeReconciliationActiveStack | null>(null)
  const [removalConfirmationText, setRemovalConfirmationText] = useState("")
  const [pendingRemoval, setPendingRemoval] =
    useState<PendingReconciliationRemoval | null>(null)
  const [removalStepUpOpen, setRemovalStepUpOpen] = useState(false)

  const performReconciliationRemoval = (pending: PendingReconciliationRemoval) => {
    removeUnlistedStack.mutate(
      {
        slugOrId: pending.stack.stackId,
        request: pending.request,
      },
      {
        onSuccess: (accepted) => {
          persistTrackedDestroy({
            operationId: accepted.operationId,
            runtimeStackId: accepted.runtimeStackId,
            slug: accepted.slug,
            request: pending.request,
          })
          setRemovalConfirmation(null)
          setRemovalConfirmationText("")
          setPendingRemoval(null)
          setRemovalStepUpOpen(false)
          navigate("/stacks")
        },
        onError: (error) => {
          if (!isStepUpRequiredRuntimeStackProblem(error)) return

          removeUnlistedStack.reset()
          setRemovalConfirmation(null)
          setRemovalConfirmationText("")
          setPendingRemoval(pending)
          setRemovalStepUpOpen(true)
        },
      },
    )
  }

  const beginReconciliationRemoval = (stack: RuntimeReconciliationActiveStack) => {
    removeUnlistedStack.reset()
    setPendingRemoval(null)
    setRemovalStepUpOpen(false)
    setRemovalConfirmationText("")
    setRemovalConfirmation(stack)
  }

  const confirmReconciliationRemoval = () => {
    if (!removalConfirmation) return

    const expectedConfirmation = removalConfirmationValue(removalConfirmation)
    if (removalConfirmationText !== expectedConfirmation) return

    const pending: PendingReconciliationRemoval = {
      stack: removalConfirmation,
      request: {
        removeContainers: true,
        removeRoutes: true,
        removeDatabase: false,
        removeFiles: false,
        force: false,
        idempotencyKey: `reconcile-remove-${removalConfirmation.stackId}-${crypto.randomUUID()}`,
      },
    }

    setPendingRemoval(pending)
    performReconciliationRemoval(pending)
  }

  const retryReconciliationRemovalAfterStepUp = () => {
    const pending = pendingRemoval
    setRemovalStepUpOpen(false)
    if (pending) performReconciliationRemoval(pending)
  }

  const runCleanup = () => {
    cleanup.mutate(
      {
        proxyHostIds: selectedProxyHostIds,
        confirmationText,
      },
      {
        onSuccess: () => {
          setConfirmOpen(false)
          setConfirmationText("")
          setSelectedProxyHostIds([])
        },
        onError: (error) => {
          if (isStepUpRequiredRuntimeReconciliationProblem(error)) {
            // Close the Radix confirmation prompt before opening the separate
            // credential dialog so its password and TOTP fields remain usable.
            setConfirmOpen(false)
            setStepUpOpen(true)
          }
        },
      },
    )
  }

  const retryAfterStepUp = () => {
    setStepUpOpen(false)
    runCleanup()
  }

  if (report.isLoading) {
    return (
      <div className="space-y-6">
        <PageHeader onRefresh={() => void report.refetch()} refreshing={report.isFetching} />
        <Card>
          <CardHeader>
            <CardTitle>{t("maintenance.reconciliation.loadingTitle")}</CardTitle>
            <CardDescription>{t("maintenance.reconciliation.loadingDescription")}</CardDescription>
          </CardHeader>
        </Card>
      </div>
    )
  }

  if (report.isError) {
    return (
      <div className="space-y-6">
        <PageHeader onRefresh={() => void report.refetch()} refreshing={report.isFetching} />
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("maintenance.reconciliation.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{String(report.error)}</AlertDescription>
        </Alert>
      </div>
    )
  }

  const data = report.data

  if (!data) {
    return null
  }

  const selectedCount = selectedProxyHostIds.length
  const canConfirmCleanup = confirmationText === CLEANUP_CONFIRMATION && selectedCount > 0
  const manifestlessActiveStacks = data.activeStacks.filter((stack) => !stack.hasManifest)
  const expectedRemovalConfirmation = removalConfirmation
    ? removalConfirmationValue(removalConfirmation)
    : ""
  const canConfirmRemoval = Boolean(
    removalConfirmation &&
    removalConfirmation.canRemoveFromReconciliation &&
    removalConfirmationText === expectedRemovalConfirmation,
  )

  const statusTone = data.status === "ok"
    ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
    : data.status === "needs_attention"
      ? "border-amber-500/30 bg-amber-500/10 text-amber-300"
      : "border-destructive/30 bg-destructive/10 text-destructive"

  return (
    <div className="space-y-6">
      <PageHeader onRefresh={() => void report.refetch()} refreshing={report.isFetching} />

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center gap-3">
            <CardTitle>{t("maintenance.reconciliation.reportTitle")}</CardTitle>
            <Badge variant="outline" className={statusTone}>
              {formatStatus(data.status, t)}
            </Badge>
          </div>
          <CardDescription>
            {formatReportDetail(data.status, t)}
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4 text-sm">
          <div className="text-muted-foreground">
            {t("maintenance.reconciliation.checkedAt", {
              value: formatDateTime(data.checkedAtUtc, language),
            })}
          </div>

          {data.warnings.length > 0 ? (
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("maintenance.reconciliation.warningsTitle")}</AlertTitle>
              <AlertDescription>
                <ul className="list-disc space-y-1 pl-5">
                  {data.warnings.map((warning) => (
                    <li key={warning}>{warning}</li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          ) : null}
        </CardContent>
      </Card>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
        <MetricCard
          title={t("maintenance.reconciliation.metric.activeStacks")}
          value={formatNumber(data.summary.activeStackCount, language)}
          description={t("maintenance.reconciliation.metric.activeStacksDescription")}
          attention={data.summary.activeDatabaseRowsWithoutManifestCount > 0}
        />
        <MetricCard
          title={t("maintenance.reconciliation.metric.destroyedHistory")}
          value={formatNumber(data.summary.destroyedStackHistoryCount, language)}
          description={t("maintenance.reconciliation.metric.destroyedHistoryDescription")}
        />
        <MetricCard
          title={t("maintenance.reconciliation.metric.npmStackHosts")}
          value={formatNumber(data.summary.memManagedNpmProxyHostCount, language)}
          description={t("maintenance.reconciliation.metric.npmStackHostsDescription")}
        />
        <MetricCard
          title={t("maintenance.reconciliation.metric.removableUnlisted")}
          value={formatNumber(data.summary.removableManifestlessStackCount, language)}
          description={t("maintenance.reconciliation.metric.removableUnlistedDescription")}
          attention={data.summary.removableManifestlessStackCount > 0}
        />
      </div>

      {removeUnlistedStack.error &&
      !isStepUpRequiredRuntimeStackProblem(removeUnlistedStack.error) ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("maintenance.reconciliation.unlisted.errorTitle")}</AlertTitle>
          <AlertDescription>{String(removeUnlistedStack.error)}</AlertDescription>
        </Alert>
      ) : null}

      <Card>
        <CardHeader>
          <div className="flex items-start gap-3">
            <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full border bg-muted">
              <Wrench className="h-4 w-4 text-amber-300" />
            </div>
            <div>
              <CardTitle>{t("maintenance.reconciliation.unlisted.title")}</CardTitle>
              <CardDescription>{t("maintenance.reconciliation.unlisted.description")}</CardDescription>
            </div>
          </div>
        </CardHeader>
        <CardContent>
          <UnlistedStackTable
            stacks={manifestlessActiveStacks}
            onRemove={beginReconciliationRemoval}
          />
        </CardContent>
      </Card>

      {cleanup.data ? (
        <Alert variant={cleanup.data.failedCount > 0 ? "destructive" : "default"}>
          <CheckCircle2 className="h-4 w-4" />
          <AlertTitle>{t("maintenance.reconciliation.cleanup.successTitle")}</AlertTitle>
          <AlertDescription>
            {t("maintenance.reconciliation.cleanup.deletedSummary", {
              deleted: formatNumber(cleanup.data.deletedCount, language),
              missing: formatNumber(cleanup.data.alreadyMissingCount, language),
              skipped: formatNumber(cleanup.data.skippedCount, language),
              failed: formatNumber(cleanup.data.failedCount, language),
            })}
          </AlertDescription>
        </Alert>
      ) : null}

      {cleanup.error && !isStepUpRequiredRuntimeReconciliationProblem(cleanup.error) ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("maintenance.reconciliation.cleanup.errorTitle")}</AlertTitle>
          <AlertDescription>{String(cleanup.error)}</AlertDescription>
        </Alert>
      ) : null}

      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
            <div>
              <CardTitle>{t("maintenance.reconciliation.orphanedNpmTitle")}</CardTitle>
              <CardDescription>{t("maintenance.reconciliation.orphanedNpmDescription")}</CardDescription>
            </div>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              onClick={() => setConfirmOpen(true)}
              disabled={selectedCount === 0 || cleanup.isPending}
            >
              <Trash2 className="mr-2 h-4 w-4" />
              {cleanup.isPending
                ? t("maintenance.reconciliation.cleanup.deleting")
                : t("maintenance.reconciliation.cleanup.deleteSelected")}
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          <NpmProxyHostTable
            hosts={data.orphanedNpmProxyHosts}
            emptyText={t("maintenance.reconciliation.orphanedNpmEmpty")}
            selectedProxyHostIds={selectedProxyHostIds}
            onSelectionChange={setSelectedProxyHostIds}
          />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{t("maintenance.reconciliation.activeRoutesTitle")}</CardTitle>
          <CardDescription>{t("maintenance.reconciliation.activeRoutesDescription")}</CardDescription>
        </CardHeader>
        <CardContent>
          {data.activeRoutes.length === 0 ? (
            <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              {t("maintenance.reconciliation.activeRoutesEmpty")}
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{t("maintenance.reconciliation.table.stack")}</TableHead>
                  <TableHead>{t("maintenance.reconciliation.table.service")}</TableHead>
                  <TableHead>{t("maintenance.reconciliation.table.publicHost")}</TableHead>
                  <TableHead>{t("maintenance.reconciliation.table.routeId")}</TableHead>
                  <TableHead>{t("maintenance.reconciliation.table.status")}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.activeRoutes.map((route) => (
                  <TableRow key={route.routeId}>
                    <TableCell className="font-medium">{route.stackSlug}</TableCell>
                    <TableCell>{route.serviceKey}</TableCell>
                    <TableCell className="font-mono text-xs">{route.publicHost}</TableCell>
                    <TableCell>{route.providerRouteId ?? "—"}</TableCell>
                    <TableCell>{route.status}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      <ConfirmationDialog
        open={removalConfirmation !== null}
        onOpenChange={(open) => {
          if (open) return
          removeUnlistedStack.reset()
          setRemovalConfirmation(null)
          setRemovalConfirmationText("")
        }}
        title={removalConfirmation
          ? t(removalTitleKey(removalConfirmation), { slug: removalConfirmation.slug })
          : t("maintenance.reconciliation.unlisted.confirmTitle", { slug: "" })}
        description={removalConfirmation
          ? t(removalDescriptionKey(removalConfirmation))
          : ""}
        confirmLabel={removalConfirmation
          ? t(removalActionKey(removalConfirmation))
          : t("maintenance.reconciliation.unlisted.remove")}
        confirmingLabel={t("maintenance.reconciliation.unlisted.accepting")}
        cancelLabel={t("maintenance.reconciliation.cleanup.cancel")}
        confirmVariant="destructive"
        onConfirm={confirmReconciliationRemoval}
        isConfirming={removeUnlistedStack.isPending}
        confirmDisabled={!canConfirmRemoval}
      >
        {removalConfirmation ? (
          <div className="space-y-4">
            <div className="rounded-lg border p-3 text-sm">
              <div className="font-medium">{removalConfirmation.slug}</div>
              <div className="mt-1 font-mono text-xs text-muted-foreground">
                {removalConfirmation.stackId}
              </div>
              <p className="mt-2 text-muted-foreground">
                {t(reconciliationReasonKey(removalConfirmation))}
              </p>
            </div>
            <Alert>
              <ShieldAlert className="h-4 w-4" />
              <AlertTitle>{t("maintenance.reconciliation.unlisted.retainedTitle")}</AlertTitle>
              <AlertDescription>
                {t("maintenance.reconciliation.unlisted.retainedDescription")}
              </AlertDescription>
            </Alert>
            <div className="space-y-2">
              <p className="text-sm text-muted-foreground">
                {t("maintenance.reconciliation.unlisted.confirmInstruction", {
                  value: expectedRemovalConfirmation,
                })}
              </p>
              <Input
                aria-label={t("maintenance.reconciliation.unlisted.confirmInputLabel")}
                value={removalConfirmationText}
                onChange={(event) => setRemovalConfirmationText(event.target.value)}
                placeholder={expectedRemovalConfirmation}
              />
            </div>
          </div>
        ) : null}
      </ConfirmationDialog>

      <ConfirmationDialog
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        title={t("maintenance.reconciliation.cleanup.confirmTitle")}
        description={t("maintenance.reconciliation.cleanup.confirmDescription")}
        confirmLabel={t("maintenance.reconciliation.cleanup.confirmLabel")}
        confirmingLabel={t("maintenance.reconciliation.cleanup.confirmingLabel")}
        cancelLabel={t("maintenance.reconciliation.cleanup.cancel")}
        confirmVariant="destructive"
        onConfirm={runCleanup}
        isConfirming={cleanup.isPending}
        confirmDisabled={!canConfirmCleanup}
      >
        <div className="space-y-2">
          <p className="text-sm text-muted-foreground">
            {t("maintenance.reconciliation.cleanup.confirmInstruction")}
          </p>
          <Input
            value={confirmationText}
            onChange={(event) => setConfirmationText(event.target.value)}
            placeholder={CLEANUP_CONFIRMATION}
          />
        </div>
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={removalStepUpOpen}
        onOpenChange={(open) => {
          setRemovalStepUpOpen(open)
          if (!open) setPendingRemoval(null)
        }}
        onVerified={retryReconciliationRemovalAfterStepUp}
      />

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={retryAfterStepUp}
      />
    </div>
  )
}

function UnlistedStackTable({
  stacks,
  onRemove,
}: {
  stacks: RuntimeReconciliationActiveStack[]
  onRemove: (stack: RuntimeReconciliationActiveStack) => void
}) {
  const { language, t } = useI18n()

  if (stacks.length === 0) {
    return (
      <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
        {t("maintenance.reconciliation.unlisted.empty")}
      </div>
    )
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead>{t("maintenance.reconciliation.table.stack")}</TableHead>
          <TableHead>{t("maintenance.reconciliation.unlisted.classification")}</TableHead>
          <TableHead>{t("maintenance.reconciliation.unlisted.evidence")}</TableHead>
          <TableHead className="text-right">{t("maintenance.reconciliation.unlisted.action")}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {stacks.map((stack) => (
          <TableRow key={stack.stackId}>
            <TableCell className="align-top">
              <div className="font-medium">{stack.slug}</div>
              <div className="mt-1 font-mono text-xs text-muted-foreground">{stack.stackId}</div>
              {stack.lastVerifiedAtUtc ? (
                <div className="mt-1 text-xs text-muted-foreground">
                  {formatDateTime(stack.lastVerifiedAtUtc, language)}
                </div>
              ) : null}
            </TableCell>
            <TableCell className="align-top">
              <Badge variant="outline" className={reconciliationStateTone(stack)}>
                {t(reconciliationStateKey(stack))}
              </Badge>
              <p className="mt-2 max-w-sm text-sm text-muted-foreground">
                {t(reconciliationReasonKey(stack))}
              </p>
            </TableCell>
            <TableCell className="align-top text-sm text-muted-foreground">
              <div>
                {t("maintenance.reconciliation.unlisted.serviceEvidence", {
                  count: formatNumber(stack.recordedServiceCount, language),
                })}
              </div>
              <div>
                {t("maintenance.reconciliation.unlisted.routeEvidence", {
                  total: formatNumber(stack.recordedRouteCount, language),
                  matching: formatNumber(stack.matchingNpmRouteCount, language),
                  missing: formatNumber(stack.missingNpmRouteCount, language),
                  mismatched: formatNumber(stack.mismatchedNpmRouteCount, language),
                })}
              </div>
            </TableCell>
            <TableCell className="align-top text-right">
              {stack.canRemoveFromReconciliation && stack.removalActionCode ? (
                <Button
                  type="button"
                  variant="destructive"
                  size="sm"
                  onClick={() => onRemove(stack)}
                >
                  <Trash2 className="mr-2 h-4 w-4" />
                  {t(removalActionKey(stack))}
                </Button>
              ) : (
                <span className="text-sm text-muted-foreground">
                  {t("maintenance.reconciliation.unlisted.reviewOnly")}
                </span>
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  )
}

function PageHeader({ onRefresh, refreshing }: { onRefresh: () => void; refreshing: boolean }) {
  const { t } = useI18n()

  return (
    <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
      <div>
        <PageBreadcrumbs items={[
          { label: t("diagnostics.title"), to: "/diagnostics" },
          { label: t("maintenance.reconciliation.title") },
        ]} />
        <h1 className="text-2xl font-semibold tracking-tight">{t("maintenance.reconciliation.title")}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("maintenance.reconciliation.description")}
        </p>
      </div>
      <Button variant="outline" size="sm" onClick={onRefresh} disabled={refreshing}>
        <RefreshCw className="mr-2 h-4 w-4" />
        {refreshing ? t("maintenance.reconciliation.refreshing") : t("maintenance.reconciliation.refresh")}
      </Button>
    </div>
  )
}

function MetricCard({
  title,
  value,
  description,
  attention = false,
}: {
  title: string
  value: string
  description: string
  attention?: boolean
}) {
  const Icon = attention ? ShieldAlert : CheckCircle2

  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex h-10 w-10 items-center justify-center rounded-full border bg-muted">
          <Icon className={attention ? "h-5 w-5 text-amber-300" : "h-5 w-5 text-emerald-300"} />
        </div>
        <div>
          <CardTitle className="text-2xl">{value}</CardTitle>
          <CardDescription className="font-medium text-foreground">{title}</CardDescription>
        </div>
      </CardHeader>
      <CardContent className="text-sm text-muted-foreground">{description}</CardContent>
    </Card>
  )
}

function NpmProxyHostTable({
  hosts,
  emptyText,
  selectedProxyHostIds,
  onSelectionChange,
}: {
  hosts: RuntimeReconciliationNpmProxyHost[]
  emptyText: string
  selectedProxyHostIds: number[]
  onSelectionChange: (ids: number[]) => void
}) {
  const { t } = useI18n()

  if (hosts.length === 0) {
    return (
      <div className="rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
        {emptyText}
      </div>
    )
  }

  const hostIds = hosts.map((host) => host.proxyHostId)
  const selectedVisibleHostIds = selectedProxyHostIds.filter((id) => hostIds.includes(id))
  const allVisibleHostsSelected = selectedVisibleHostIds.length === hosts.length
  const someVisibleHostsSelected = selectedVisibleHostIds.length > 0 && !allVisibleHostsSelected

  const toggle = (proxyHostId: number, checked: boolean) => {
    onSelectionChange(
      checked
        ? Array.from(new Set([...selectedProxyHostIds, proxyHostId])).sort((a, b) => a - b)
        : selectedProxyHostIds.filter((id) => id !== proxyHostId),
    )
  }

  const toggleAllVisibleHosts = (checked: boolean) => {
    onSelectionChange(
      checked
        ? Array.from(new Set([...selectedProxyHostIds, ...hostIds])).sort((a, b) => a - b)
        : selectedProxyHostIds.filter((id) => !hostIds.includes(id)),
    )
  }

  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-12">
            <Checkbox
              checked={allVisibleHostsSelected ? true : someVisibleHostsSelected ? "indeterminate" : false}
              aria-label={t("maintenance.reconciliation.cleanup.selectAllLabel")}
              onCheckedChange={(checked) => toggleAllVisibleHosts(checked === true)}
            />
          </TableHead>
          <TableHead>{t("maintenance.reconciliation.table.proxyId")}</TableHead>
          <TableHead>{t("maintenance.reconciliation.table.domain")}</TableHead>
          <TableHead>{t("maintenance.reconciliation.table.forward")}</TableHead>
          <TableHead>{t("maintenance.reconciliation.table.status")}</TableHead>
          <TableHead>{t("maintenance.reconciliation.table.reason")}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {hosts.map((host) => {
          const domainLabel = host.domainNames.join(", ")

          return (
            <TableRow key={host.proxyHostId}>
              <TableCell>
                <Checkbox
                  checked={selectedProxyHostIds.includes(host.proxyHostId)}
                  aria-label={t("maintenance.reconciliation.cleanup.selectionLabel", { domain: domainLabel })}
                  onCheckedChange={(checked) => toggle(host.proxyHostId, checked === true)}
                />
              </TableCell>
              <TableCell>{host.proxyHostId}</TableCell>
              <TableCell className="font-mono text-xs">{domainLabel}</TableCell>
              <TableCell className="font-mono text-xs">
                {host.forwardHost ? `${host.forwardHost}:${host.forwardPort ?? "?"}` : "—"}
              </TableCell>
              <TableCell>
                {host.enabled ? t("maintenance.reconciliation.enabled") : t("maintenance.reconciliation.disabled")}
              </TableCell>
              <TableCell className="max-w-md text-muted-foreground">{host.reason ?? "—"}</TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}

function removalConfirmationValue(stack: RuntimeReconciliationActiveStack) {
  return `REMOVE ${stack.slug}`
}

function removalActionKey(stack: RuntimeReconciliationActiveStack): TranslationKey {
  switch (stack.removalActionCode) {
    case "finish_interrupted_removal":
      return "maintenance.reconciliation.unlisted.finishRemoval"
    case "cleanup_failed_creation":
      return "maintenance.reconciliation.unlisted.cleanFailedCreation"
    case "remove_unlisted_runtime":
      return "maintenance.reconciliation.unlisted.remove"
    default:
      return "maintenance.reconciliation.unlisted.reviewOnly"
  }
}

function removalTitleKey(stack: RuntimeReconciliationActiveStack): TranslationKey {
  switch (stack.removalActionCode) {
    case "finish_interrupted_removal":
      return "maintenance.reconciliation.unlisted.confirmInterruptedTitle"
    case "cleanup_failed_creation":
      return "maintenance.reconciliation.unlisted.confirmFailedCreationTitle"
    default:
      return "maintenance.reconciliation.unlisted.confirmTitle"
  }
}

function removalDescriptionKey(stack: RuntimeReconciliationActiveStack): TranslationKey {
  switch (stack.removalActionCode) {
    case "finish_interrupted_removal":
      return "maintenance.reconciliation.unlisted.confirmInterruptedDescription"
    case "cleanup_failed_creation":
      return "maintenance.reconciliation.unlisted.confirmFailedCreationDescription"
    default:
      return "maintenance.reconciliation.unlisted.confirmDescription"
  }
}

function reconciliationStateKey(stack: RuntimeReconciliationActiveStack): TranslationKey {
  switch (stack.reconciliationState) {
    case "interrupted_destroy_candidate":
      return "maintenance.reconciliation.unlisted.state.interrupted"
    case "failed_creation_candidate":
      return "maintenance.reconciliation.unlisted.state.failedCreation"
    case "unlisted_runtime":
      return "maintenance.reconciliation.unlisted.state.unlisted"
    case "inspection_unavailable":
      return "maintenance.reconciliation.unlisted.state.inspectionUnavailable"
    case "ownership_ambiguous":
      return "maintenance.reconciliation.unlisted.state.ambiguous"
    default:
      return "maintenance.reconciliation.unlisted.state.review"
  }
}


function reconciliationReasonKey(stack: RuntimeReconciliationActiveStack): TranslationKey {
  switch (stack.reconciliationState) {
    case "interrupted_destroy_candidate":
      return "maintenance.reconciliation.unlisted.reason.interrupted"
    case "failed_creation_candidate":
      return "maintenance.reconciliation.unlisted.reason.failedCreation"
    case "unlisted_runtime":
      return "maintenance.reconciliation.unlisted.reason.unlisted"
    case "inspection_unavailable":
      return "maintenance.reconciliation.unlisted.reason.inspectionUnavailable"
    case "ownership_ambiguous":
      return "maintenance.reconciliation.unlisted.reason.ambiguous"
    default:
      return "maintenance.reconciliation.unlisted.reason.review"
  }
}

function formatReportDetail(
  status: string,
  t: (key: TranslationKey) => string,
) {
  switch (status) {
    case "ok":
      return t("maintenance.reconciliation.reportDetail.ok")
    case "needs_attention":
      return t("maintenance.reconciliation.reportDetail.needsAttention")
    case "degraded":
      return t("maintenance.reconciliation.reportDetail.degraded")
    default:
      return t("maintenance.reconciliation.reportDescription")
  }
}

function reconciliationStateTone(stack: RuntimeReconciliationActiveStack) {
  if (stack.canRemoveFromReconciliation) {
    return "border-amber-500/30 bg-amber-500/10 text-amber-300"
  }

  return "border-border bg-muted text-muted-foreground"
}

function formatStatus(status: string, t: (key: TranslationKey) => string) {
  switch (status) {
    case "ok":
      return t("maintenance.reconciliation.status.ok")
    case "needs_attention":
      return t("maintenance.reconciliation.status.needsAttention")
    case "degraded":
      return t("maintenance.reconciliation.status.degraded")
    default:
      return status
  }
}
