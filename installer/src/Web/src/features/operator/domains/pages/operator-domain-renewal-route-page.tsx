import { useEffect, useRef, useState } from "react"
import { Link, useParams } from "react-router-dom"
import {
  CheckCircle2,
  History,
  KeyRound,
  Loader2,
  Play,
  RefreshCw,
  ShieldAlert,
  Trash2,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { formatDateTime } from "@/app/formatters"
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
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import type {
  OperatorDomainRenewalStatus,
  UpdateOperatorDomainRenewalCredentialRequest,
  UpdateOperatorDomainRenewalPolicyRequest,
} from "@/features/shared/domains/api/domains.types"
import {
  useOperatorDomainRenewalHistory,
  useOperatorDomainRenewalStatus,
  useOperatorDomainRenewalStatuses,
  useRemoveOperatorDomainRenewalCredential,
  useRunOperatorDomainRenewal,
  useUpdateOperatorDomainRenewalCredential,
  useUpdateOperatorDomainRenewalPolicy,
} from "@/features/shared/domains/hooks/use-domains"
import { getMemApiProblemCode, getMemApiProblemDetail } from "@/lib/api-problem"

type PendingAction =
  | {
      kind: "credential"
      domainId: string
      request: UpdateOperatorDomainRenewalCredentialRequest
    }
  | {
      kind: "policy"
      domainId: string
      request: UpdateOperatorDomainRenewalPolicyRequest
    }
  | { kind: "remove"; domainId: string }
  | { kind: "run"; domainId: string }
  | null

export function OperatorDomainRenewalRoutePage() {
  const { domainId } = useParams()

  return domainId ? (
    <DomainRenewalWorkspacePage domainId={domainId} />
  ) : (
    <RenewalInventoryPage />
  )
}

function RenewalInventoryPage() {
  const { t } = useI18n()
  const inventory = useOperatorDomainRenewalStatuses()
  const items = inventory.data ?? []
  const summary = summarize(items)

  return (
    <div className="space-y-6">
      <PageBreadcrumbs
        items={[
          { label: t("operatorDomains.common.domains"), to: "/domains" },
          { label: t("operatorDomains.workspace.renewal.title") },
        ]}
      />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("operatorDomains.workspace.renewal.title")}
          </h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.workspace.renewal.description")}
          </p>
        </div>
        <Button
          type="button"
          variant="outline"
          disabled={inventory.isFetching}
          onClick={() => void inventory.refetch()}
        >
          {inventory.isFetching ? (
            <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          ) : (
            <RefreshCw className="mr-2 h-4 w-4" />
          )}
          {t("operatorDomains.workspace.renewal.refresh")}
        </Button>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <SummaryFact
          label={t("operatorDomains.workspace.renewal.summary.enabled")}
          value={summary.enabled}
        />
        <SummaryFact
          label={t("operatorDomains.workspace.renewal.summary.active")}
          value={summary.active}
        />
        <SummaryFact
          label={t("operatorDomains.workspace.renewal.summary.attention")}
          value={summary.attention}
        />
        <SummaryFact
          label={t("operatorDomains.workspace.renewal.summary.unready")}
          value={summary.unready}
        />
      </div>

      <Alert>
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>{t("operatorDomains.workspace.renewal.foundationTitle")}</AlertTitle>
        <AlertDescription>
          {t("operatorDomains.workspace.renewal.foundationDescription")}
        </AlertDescription>
      </Alert>

      {inventory.isLoading && !inventory.data ? (
        <Card>
          <CardContent className="flex items-center gap-2 py-8 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            {t("operatorDomains.workspace.renewal.loading")}
          </CardContent>
        </Card>
      ) : null}

      {inventory.error && !inventory.data ? (
        <Alert variant="destructive">
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("operatorDomains.workspace.renewal.loadFailedTitle")}</AlertTitle>
          <AlertDescription>
            {getMemApiProblemDetail(
              inventory.error,
              t("operatorDomains.workspace.renewal.loadFailedDescription"),
            )}
          </AlertDescription>
        </Alert>
      ) : null}

      {!inventory.isLoading && !inventory.error && items.length === 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("operatorDomains.workspace.renewal.emptyTitle")}</CardTitle>
            <CardDescription>
              {t("operatorDomains.workspace.renewal.emptyDescription")}
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {items.length > 0 ? <RenewalInventoryTable items={items} /> : null}
    </div>
  )
}

function DomainRenewalWorkspacePage({ domainId }: { domainId: string }) {
  const { t } = useI18n()
  const { session } = useOperatorSession()
  const isOwner = session.roles.includes("platform_owner")
  const domain = useOperatorDomainRenewalStatus(domainId)
  const item = domain.data

  return (
    <div className="space-y-6">
      <PageBreadcrumbs
        items={[
          { label: t("operatorDomains.common.domains"), to: "/domains" },
          {
            label: item?.baseDomain ?? t("operatorDomains.workspace.renewal.title"),
            to: `/domains/${encodeURIComponent(domainId)}`,
          },
          { label: t("operatorDomains.workspace.renewal.title") },
        ]}
      />

      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            {item
              ? t("operatorDomains.workspace.renewal.domainTitle", {
                  domain: item.baseDomain,
                })
              : t("operatorDomains.workspace.renewal.title")}
          </h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.workspace.renewal.domainPageDescription")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button asChild type="button" variant="outline">
            <Link to="/domains/renewal">
              {t("operatorDomains.workspace.renewal.backToInventory")}
            </Link>
          </Button>
          <Button
            type="button"
            variant="outline"
            disabled={domain.isFetching}
            onClick={() => void domain.refetch()}
          >
            {domain.isFetching ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-2 h-4 w-4" />
            )}
            {t("operatorDomains.workspace.renewal.refresh")}
          </Button>
        </div>
      </div>

      <Alert>
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>{t("operatorDomains.workspace.renewal.foundationTitle")}</AlertTitle>
        <AlertDescription>
          {t("operatorDomains.workspace.renewal.foundationDescription")}
        </AlertDescription>
      </Alert>

      {!isOwner ? (
        <Alert>
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("operatorDomains.workspace.renewal.ownerOnlyTitle")}</AlertTitle>
          <AlertDescription>
            {t("operatorDomains.workspace.renewal.ownerOnlyDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      {domain.isLoading && !item ? (
        <Card>
          <CardContent className="flex items-center gap-2 py-8 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            {t("operatorDomains.workspace.renewal.loading")}
          </CardContent>
        </Card>
      ) : null}

      {domain.error && !item ? (
        <Alert variant="destructive">
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("operatorDomains.workspace.renewal.loadFailedTitle")}</AlertTitle>
          <AlertDescription>
            {getMemApiProblemDetail(
              domain.error,
              t("operatorDomains.workspace.renewal.loadFailedDescription"),
            )}
          </AlertDescription>
        </Alert>
      ) : null}

      {!domain.isLoading && !domain.error && !item ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("operatorDomains.workspace.renewal.domainMissingTitle")}</CardTitle>
            <CardDescription>
              {t("operatorDomains.workspace.renewal.domainMissingDescription")}
            </CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {item ? <RenewalDomainCard item={item} canManage={isOwner} detailed /> : null}
    </div>
  )
}

function RenewalInventoryTable({ items }: { items: OperatorDomainRenewalStatus[] }) {
  const { t, language } = useI18n()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("operatorDomains.workspace.renewal.inventoryTitle")}</CardTitle>
        <CardDescription>
          {t("operatorDomains.workspace.renewal.inventoryDescription")}
        </CardDescription>
      </CardHeader>
      <CardContent className="px-0 sm:px-0">
        <div className="border-y border-border" data-testid="renewal-inventory">
          <Table className="min-w-[1160px]" data-testid="renewal-inventory-table">
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="min-w-52">
                  {t("operatorDomains.common.domain")}
                </TableHead>
                <TableHead className="min-w-44">
                  {t("operatorDomains.common.status")}
                </TableHead>
                <TableHead className="min-w-32">
                  {t("operatorDomains.workspace.renewal.autoRenew")}
                </TableHead>
                <TableHead className="min-w-36">
                  {t("operatorDomains.workspace.renewal.dnsCredential")}
                </TableHead>
                <TableHead className="min-w-48">
                  {t("operatorDomains.workspace.renewal.expires")}
                </TableHead>
                <TableHead className="min-w-44">
                  {t("operatorDomains.workspace.renewal.nextAttempt")}
                </TableHead>
                <TableHead className="sticky right-0 z-30 min-w-40 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]">
                  {t("operatorDomains.common.actions")}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {items.map((item) => (
                <TableRow key={item.domainId} data-testid={`renewal-row-${item.domainId}`}>
                  <TableCell className="min-w-52 align-top">
                    <RenewalDomainIdentity item={item} />
                  </TableCell>
                  <TableCell className="min-w-44 align-top">
                    <RenewalInventoryStatus item={item} />
                  </TableCell>
                  <TableCell className="min-w-32 align-top">
                    <div className="text-sm font-medium text-foreground">
                      {item.autoRenewEnabled
                        ? t("operatorDomains.workspace.renewal.enabled")
                        : t("operatorDomains.workspace.renewal.disabled")}
                    </div>
                  </TableCell>
                  <TableCell className="min-w-36 align-top">
                    <div className="text-sm font-medium text-foreground">
                      {item.credentialConfigured
                        ? t("operatorDomains.workspace.renewal.configured")
                        : t("operatorDomains.workspace.renewal.required")}
                    </div>
                    {item.credentialUpdatedAtUtc ? (
                      <div className="mt-1 text-xs text-muted-foreground">
                        {t("operatorDomains.workspace.renewal.updatedAt", {
                          timestamp: formatDateTime(item.credentialUpdatedAtUtc, language),
                        })}
                      </div>
                    ) : null}
                  </TableCell>
                  <TableCell className="min-w-48 align-top">
                    <div className="text-sm font-medium text-foreground">
                      {expiryValue(item, t)}
                    </div>
                    {item.nextEligibleRenewalAtUtc ? (
                      <div className="mt-1 max-w-48 text-xs text-muted-foreground">
                        {t("operatorDomains.workspace.renewal.nextWindow", {
                          timestamp: formatDateTime(item.nextEligibleRenewalAtUtc, language),
                        })}
                      </div>
                    ) : null}
                  </TableCell>
                  <TableCell className="min-w-44 align-top">
                    <div className="whitespace-nowrap text-sm text-foreground">
                      {item.nextAutomaticAttemptAtUtc
                        ? formatDateTime(item.nextAutomaticAttemptAtUtc, language)
                        : t("operatorDomains.workspace.renewal.notScheduled")}
                    </div>
                  </TableCell>
                  <TableCell
                    className="sticky right-0 z-20 min-w-40 border-l border-border bg-card align-top text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]"
                    data-testid={`renewal-actions-${item.domainId}`}
                  >
                    <RenewalOpenAction item={item} />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </CardContent>
    </Card>
  )
}

function RenewalDomainIdentity({ item }: { item: OperatorDomainRenewalStatus }) {
  const { t } = useI18n()

  return (
    <div className="min-w-44">
      <Link
        to={`/domains/${encodeURIComponent(item.domainId)}/renewal`}
        className="font-medium text-emerald-400 transition-colors hover:text-emerald-300 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
      >
        {item.baseDomain}
      </Link>
      <div className="mt-1 text-xs text-muted-foreground">
        {t("operatorDomains.workspace.renewal.domainDescription", {
          provider: item.dnsProvider,
          zone: item.dnsZone ?? item.baseDomain,
        })}
      </div>
    </div>
  )
}

function RenewalInventoryStatus({ item }: { item: OperatorDomainRenewalStatus }) {
  const { t } = useI18n()
  const activePhase = ["queued", "running", "awaiting-activation"].includes(
    item.operationalStatus,
  )

  return (
    <div className="min-w-40 space-y-1.5">
      <Badge variant={operationalBadgeVariant(item.operationalStatus)}>
        {operationalLabel(item.operationalStatus, t, item.readinessStatus)}
      </Badge>
      {activePhase && item.latestOperationStep ? (
        <div className="text-xs text-muted-foreground">
          {phaseLabel(item.latestOperationStep, t)}
        </div>
      ) : null}
    </div>
  )
}

function RenewalOpenAction({ item }: { item: OperatorDomainRenewalStatus }) {
  const { t } = useI18n()

  return (
    <Button asChild variant="outline" size="sm">
      <Link to={`/domains/${encodeURIComponent(item.domainId)}/renewal`}>
        {t("operatorDomains.workspace.renewal.openDetails")}
      </Link>
    </Button>
  )
}

function RenewalDomainCard({
  item,
  canManage,
  detailed,
}: {
  item: OperatorDomainRenewalStatus
  canManage: boolean
  detailed: boolean
}) {
  const { t, language } = useI18n()
  const credential = useUpdateOperatorDomainRenewalCredential()
  const policy = useUpdateOperatorDomainRenewalPolicy()
  const removeCredential = useRemoveOperatorDomainRenewalCredential()
  const runRenewal = useRunOperatorDomainRenewal()
  const history = useOperatorDomainRenewalHistory(item.domainId, detailed)
  const [email, setEmail] = useState(item.acmeEmail ?? "")
  const [providerToken, setProviderToken] = useState("")
  const [message, setMessage] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const pendingAction = useRef<PendingAction>(null)

  useEffect(() => {
    if (!providerToken) {
      setEmail(item.acmeEmail ?? "")
    }
  }, [item.acmeEmail, providerToken])

  const busy =
    credential.isPending ||
    policy.isPending ||
    removeCredential.isPending ||
    runRenewal.isPending

  const resetMessages = () => {
    setMessage(null)
    setErrorMessage(null)
  }

  const handleStepUp = (error: unknown, action: Exclude<PendingAction, null>) => {
    if (getMemApiProblemCode(error) !== "step_up_required") {
      return false
    }

    pendingAction.current = action
    setStepUpOpen(true)
    return true
  }

  const runCredential = (
    domainId: string,
    request: UpdateOperatorDomainRenewalCredentialRequest,
  ) => {
    resetMessages()
    credential.reset()
    credential.mutate(
      { domainId, request },
      {
        onSuccess: () => {
          pendingAction.current = null
          setProviderToken("")
          setMessage(t("operatorDomains.workspace.renewal.credentialSaved"))
          credential.reset()
        },
        onError: (error) => {
          if (handleStepUp(error, { kind: "credential", domainId, request })) {
            credential.reset()
            return
          }

          pendingAction.current = null
          setProviderToken("")
          setErrorMessage(t("operatorDomains.workspace.renewal.credentialSaveFailed"))
          credential.reset()
        },
      },
    )
  }

  const runPolicy = (
    domainId: string,
    request: UpdateOperatorDomainRenewalPolicyRequest,
  ) => {
    resetMessages()
    policy.reset()
    policy.mutate(
      { domainId, request },
      {
        onSuccess: () => {
          pendingAction.current = null
          setMessage(
            request.autoRenewEnabled
              ? t("operatorDomains.workspace.renewal.enabledSaved")
              : t("operatorDomains.workspace.renewal.disabledSaved"),
          )
          policy.reset()
        },
        onError: (error) => {
          if (handleStepUp(error, { kind: "policy", domainId, request })) {
            policy.reset()
            return
          }

          pendingAction.current = null
          setErrorMessage(t("operatorDomains.workspace.renewal.policySaveFailed"))
          policy.reset()
        },
      },
    )
  }

  const runRemove = (domainId: string) => {
    resetMessages()
    removeCredential.reset()
    removeCredential.mutate(domainId, {
      onSuccess: () => {
        pendingAction.current = null
        setMessage(t("operatorDomains.workspace.renewal.credentialRemoved"))
        removeCredential.reset()
      },
      onError: (error) => {
        if (handleStepUp(error, { kind: "remove", domainId })) {
          removeCredential.reset()
          return
        }

        pendingAction.current = null
        setErrorMessage(t("operatorDomains.workspace.renewal.credentialRemoveFailed"))
        removeCredential.reset()
      },
    })
  }

  const runNow = (domainId: string) => {
    resetMessages()
    runRenewal.reset()
    runRenewal.mutate(domainId, {
      onSuccess: () => {
        pendingAction.current = null
        setMessage(t("operatorDomains.workspace.renewal.runQueued"))
        runRenewal.reset()
      },
      onError: (error) => {
        if (handleStepUp(error, { kind: "run", domainId })) {
          runRenewal.reset()
          return
        }

        pendingAction.current = null
        setErrorMessage(
          getMemApiProblemDetail(
            error,
            t("operatorDomains.workspace.renewal.runFailed"),
          ),
        )
        runRenewal.reset()
      },
    })
  }

  const resumeAfterStepUp = () => {
    const pending = pendingAction.current
    pendingAction.current = null
    if (!pending) return

    if (pending.kind === "credential") {
      runCredential(pending.domainId, pending.request)
    } else if (pending.kind === "policy") {
      runPolicy(pending.domainId, pending.request)
    } else if (pending.kind === "remove") {
      runRemove(pending.domainId)
    } else {
      runNow(pending.domainId)
    }
  }

  const submitCredential = () => {
    const request = {
      providerToken: providerToken.trim(),
      acmeEmail: email.trim() || null,
    }
    setProviderToken("")
    runCredential(item.domainId, request)
  }

  const togglePolicy = () => {
    runPolicy(item.domainId, {
      autoRenewEnabled: !item.autoRenewEnabled,
      acmeEmail: email.trim() || item.acmeEmail,
    })
  }

  const confirmRemove = () => {
    if (
      !window.confirm(
        t("operatorDomains.workspace.renewal.removeConfirm", {
          domain: item.baseDomain,
        }),
      )
    ) {
      return
    }
    runRemove(item.domainId)
  }

  const statusTone = operationalTone(item.operationalStatus, item.daysRemaining)

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{item.baseDomain}</CardTitle>
            <CardDescription className="mt-1">
              {t("operatorDomains.workspace.renewal.domainDescription", {
                provider: item.dnsProvider,
                zone: item.dnsZone ?? item.baseDomain,
              })}
            </CardDescription>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={statusTone === "good" ? "default" : "outline"}>
              {operationalLabel(item.operationalStatus, t, item.readinessStatus)}
            </Badge>
            {!detailed ? (
              <Button asChild variant="outline" size="sm">
                <Link to={`/domains/${encodeURIComponent(item.domainId)}/renewal`}>
                  {t("operatorDomains.workspace.renewal.openDetails")}
                </Link>
              </Button>
            ) : null}
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        <RenewalStateAlert item={item} />

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Fact
            label={t("operatorDomains.workspace.renewal.autoRenew")}
            value={
              item.autoRenewEnabled
                ? t("operatorDomains.workspace.renewal.enabled")
                : t("operatorDomains.workspace.renewal.disabled")
            }
          />
          <Fact
            label={t("operatorDomains.workspace.renewal.dnsCredential")}
            value={
              item.credentialConfigured
                ? t("operatorDomains.workspace.renewal.configured")
                : t("operatorDomains.workspace.renewal.required")
            }
            detail={
              item.credentialUpdatedAtUtc
                ? t("operatorDomains.workspace.renewal.updatedAt", {
                    timestamp: formatDateTime(item.credentialUpdatedAtUtc, language),
                  })
                : undefined
            }
          />
          <Fact
            label={t("operatorDomains.workspace.renewal.expires")}
            value={expiryValue(item, t)}
            detail={
              item.nextEligibleRenewalAtUtc
                ? t("operatorDomains.workspace.renewal.nextWindow", {
                    timestamp: formatDateTime(item.nextEligibleRenewalAtUtc, language),
                  })
                : undefined
            }
          />
          <Fact
            label={t("operatorDomains.workspace.renewal.nextAttempt")}
            value={
              item.nextAutomaticAttemptAtUtc
                ? formatDateTime(item.nextAutomaticAttemptAtUtc, language)
                : t("operatorDomains.workspace.renewal.notScheduled")
            }
          />
        </div>

        <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <Fact
            label={t("operatorDomains.workspace.renewal.lastAttempt")}
            value={
              item.lastAttemptAtUtc
                ? formatDateTime(item.lastAttemptAtUtc, language)
                : t("operatorDomains.workspace.renewal.never")}
          />
          <Fact
            label={t("operatorDomains.workspace.renewal.lastSuccess")}
            value={
              item.lastSuccessfulRenewalAtUtc
                ? formatDateTime(item.lastSuccessfulRenewalAtUtc, language)
                : t("operatorDomains.workspace.renewal.never")}
          />
          <Fact
            label={t("operatorDomains.workspace.renewal.currentPhase")}
            value={
              item.latestOperationStep
                ? phaseLabel(item.latestOperationStep, t)
                : t("operatorDomains.workspace.renewal.notRunning")
            }
          />
          <Fact
            label={t("operatorDomains.workspace.renewal.attempts")}
            value={String(item.latestOperationAttemptCount)}
            detail={
              item.latestRequestedBy
                ? t("operatorDomains.workspace.renewal.requestedBy", {
                    source:
                      item.latestRequestedBy === "operator"
                        ? t("operatorDomains.workspace.renewal.requestedByOperator")
                        : t("operatorDomains.workspace.renewal.requestedByAutomatic"),
                  })
                : undefined
            }
          />
        </div>

        <div className="rounded-xl border border-border p-4 text-sm">
          <div className="font-medium text-foreground">
            {t("operatorDomains.workspace.renewal.productionCertificate")}
          </div>
          <div className="mt-1 text-muted-foreground">
            {item.hasActiveProductionCertificate && item.activeCertificateId
              ? t("operatorDomains.workspace.renewal.productionCertificateValue", {
                  certificateId: item.activeCertificateId,
                  expiry: item.activeCertificateExpiresAtUtc
                    ? formatDateTime(item.activeCertificateExpiresAtUtc, language)
                    : t("operatorDomains.workspace.renewal.expiryUnknown"),
                })
              : t("operatorDomains.workspace.renewal.productionCertificateMissing")}
          </div>
        </div>

        {item.operationalStatus === "failed" ? (
          <div className="flex flex-col gap-2 rounded-xl border border-amber-500/30 bg-amber-500/5 p-4 text-sm sm:flex-row sm:items-center sm:justify-between">
            <div className="min-w-0">
              <div className="font-medium text-foreground">
                {t("operatorDomains.workspace.renewal.failureTitle")}
              </div>
              <div className="mt-1 text-muted-foreground">
                {t("operatorDomains.workspace.renewal.failureDescription", {
                  code: item.latestErrorCode ?? t("operatorDomains.common.unknown"),
                })}
              </div>
            </div>
            <div className="flex shrink-0 flex-wrap gap-2">
              {item.diagnosticsHref ? (
                <Button asChild variant="outline" size="sm">
                  <Link to={item.diagnosticsHref}>
                    {t("operatorDomains.workspace.renewal.openDiagnostics")}
                  </Link>
                </Button>
              ) : null}
              {canManage ? (
                <Button
                  type="button"
                  size="sm"
                  disabled={busy || !item.manualRenewAvailable}
                  onClick={() => runNow(item.domainId)}
                >
                  <Play className="mr-2 h-4 w-4" />
                  {t("operatorDomains.workspace.renewal.retryNow")}
                </Button>
              ) : null}
            </div>
          </div>
        ) : canManage ? (
          <Button
            type="button"
            variant="outline"
            disabled={busy || !item.manualRenewAvailable}
            onClick={() => runNow(item.domainId)}
          >
            {runRenewal.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <Play className="mr-2 h-4 w-4" />
            )}
            {t("operatorDomains.workspace.renewal.runNow")}
          </Button>
        ) : null}

        {message ? (
          <Alert>
            <CheckCircle2 className="h-4 w-4" />
            <AlertTitle>{t("operatorDomains.workspace.renewal.savedTitle")}</AlertTitle>
            <AlertDescription>{message}</AlertDescription>
          </Alert>
        ) : null}

        {errorMessage ? (
          <Alert variant="destructive">
            <ShieldAlert className="h-4 w-4" />
            <AlertTitle>{t("operatorDomains.workspace.renewal.actionFailedTitle")}</AlertTitle>
            <AlertDescription>{errorMessage}</AlertDescription>
          </Alert>
        ) : null}

        {canManage ? (
          <div className="space-y-4 rounded-xl border border-border p-4">
            <div>
              <div className="font-medium text-foreground">
                {item.credentialConfigured
                  ? t("operatorDomains.workspace.renewal.rotateTitle")
                  : t("operatorDomains.workspace.renewal.enrolTitle")}
              </div>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("operatorDomains.workspace.renewal.credentialHelp")}
              </p>
              <p className="mt-1 text-xs text-muted-foreground">
                {t("operatorDomains.workspace.renewal.credentialConsequence")}
              </p>
            </div>

            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor={`renewal-email-${item.domainId}`}>
                  {t("operatorDomains.workspace.renewal.acmeContact")}
                </Label>
                <Input
                  id={`renewal-email-${item.domainId}`}
                  type="email"
                  autoComplete="email"
                  value={email}
                  disabled={busy}
                  onChange={(event) => setEmail(event.target.value)}
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor={`renewal-token-${item.domainId}`}>
                  {t("operatorDomains.workspace.renewal.desecToken")}
                </Label>
                <Input
                  id={`renewal-token-${item.domainId}`}
                  type="password"
                  autoComplete="off"
                  value={providerToken}
                  disabled={busy}
                  placeholder={t("operatorDomains.workspace.renewal.tokenPlaceholder")}
                  onChange={(event) => setProviderToken(event.target.value)}
                />
              </div>
            </div>

            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
              <Button
                type="button"
                disabled={busy || !providerToken.trim() || !email.trim()}
                onClick={submitCredential}
              >
                {credential.isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <KeyRound className="mr-2 h-4 w-4" />
                )}
                {item.credentialConfigured
                  ? t("operatorDomains.workspace.renewal.rotateAction")
                  : t("operatorDomains.workspace.renewal.enrolAction")}
              </Button>

              <Button
                type="button"
                variant="outline"
                disabled={busy || (!item.autoRenewEnabled && !email.trim())}
                onClick={togglePolicy}
              >
                {policy.isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : null}
                {item.autoRenewEnabled
                  ? t("operatorDomains.workspace.renewal.disableAction")
                  : t("operatorDomains.workspace.renewal.enableAction")}
              </Button>

              <Button
                type="button"
                variant="destructive"
                disabled={busy || !item.credentialConfigured}
                onClick={confirmRemove}
              >
                {removeCredential.isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <Trash2 className="mr-2 h-4 w-4" />
                )}
                {t("operatorDomains.workspace.renewal.removeAction")}
              </Button>
            </div>
          </div>
        ) : null}

        {detailed ? (
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <History className="h-4 w-4 text-muted-foreground" />
              <div className="font-medium text-foreground">
                {t("operatorDomains.workspace.renewal.historyTitle")}
              </div>
            </div>
            {history.isLoading && !history.data ? (
              <div className="text-sm text-muted-foreground">
                {t("operatorDomains.workspace.renewal.historyLoading")}
              </div>
            ) : history.data?.length ? (
              <div className="overflow-x-auto rounded-xl border border-border">
                <div className="min-w-[620px] divide-y divide-border">
                  {history.data.map((entry) => (
                    <div
                      key={entry.operationId}
                      className="grid grid-cols-[1.2fr_1fr_0.7fr_0.8fr] gap-3 px-4 py-3 text-sm"
                    >
                      <div>
                        <div className="font-medium text-foreground">
                          {operationalLabel(entry.status, t)}
                        </div>
                        <div className="mt-1 text-xs text-muted-foreground">
                          {formatDateTime(entry.requestedAtUtc, language)}
                        </div>
                      </div>
                      <div className="text-muted-foreground">
                        {entry.step ? phaseLabel(entry.step, t) : "—"}
                      </div>
                      <div className="text-muted-foreground">
                        {t("operatorDomains.workspace.renewal.historyAttempts", {
                          count: entry.attemptCount,
                        })}
                      </div>
                      <div className="text-right">
                        {entry.diagnosticsHref ? (
                          <Link
                            className="text-sm font-medium text-primary hover:underline"
                            to={entry.diagnosticsHref}
                          >
                            {t("operatorDomains.workspace.renewal.openDiagnostics")}
                          </Link>
                        ) : (
                          <span className="text-xs text-muted-foreground">
                            {entry.requestedBy === "operator"
                              ? t("operatorDomains.workspace.renewal.requestedByOperator")
                              : t("operatorDomains.workspace.renewal.requestedByAutomatic")}
                          </span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            ) : (
              <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
                {t("operatorDomains.workspace.renewal.historyEmpty")}
              </div>
            )}
          </div>
        ) : null}
      </CardContent>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            window.setTimeout(() => {
              pendingAction.current = null
            }, 0)
          }
        }}
        onVerified={resumeAfterStepUp}
      />
    </Card>
  )
}

function RenewalStateAlert({ item }: { item: OperatorDomainRenewalStatus }) {
  const { t, language } = useI18n()
  const healthy =
    item.operationalStatus === "ready" ||
    item.operationalStatus === "scheduled" ||
    item.operationalStatus === "queued" ||
    item.operationalStatus === "running" ||
    item.operationalStatus === "awaiting-activation"

  return (
    <div
      className={[
        "rounded-xl border p-4 text-sm",
        healthy
          ? "border-border bg-muted/20"
          : item.daysRemaining !== null && item.daysRemaining <= 7
            ? "border-red-500/40 bg-red-500/5"
            : "border-amber-500/30 bg-amber-500/5",
      ].join(" ")}
    >
      <div className="flex items-start gap-2">
        {healthy ? (
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-500" />
        ) : (
          <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
        )}
        <div className="min-w-0">
          <div className="font-medium text-foreground">
            {operationalLabel(item.operationalStatus, t, item.readinessStatus)}
          </div>
          <div className="mt-1 text-muted-foreground">
            {operationalDescription(item, t, language)}
          </div>
        </div>
      </div>
    </div>
  )
}

function SummaryFact({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-2 text-2xl font-semibold text-foreground">{value}</div>
    </div>
  )
}

function Fact({ label, value, detail }: { label: string; value: string; detail?: string }) {
  return (
    <div className="rounded-xl border border-border p-3 text-sm">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 break-words font-medium text-foreground">{value}</div>
      {detail ? <div className="mt-1 text-xs text-muted-foreground">{detail}</div> : null}
    </div>
  )
}

function summarize(items: OperatorDomainRenewalStatus[]) {
  return {
    enabled: items.filter((item) => item.autoRenewEnabled).length,
    active: items.filter((item) =>
      ["scheduled", "queued", "running", "awaiting-activation"].includes(
        item.operationalStatus,
      ),
    ).length,
    attention: items.filter((item) => item.operationalStatus === "failed").length,
    unready: items.filter((item) =>
      ["unready", "disabled"].includes(item.operationalStatus),
    ).length,
  }
}

function operationalBadgeVariant(
  status: OperatorDomainRenewalStatus["operationalStatus"],
): "default" | "secondary" | "destructive" | "outline" {
  if (status === "failed") return "destructive"
  if (["scheduled", "queued", "running", "awaiting-activation"].includes(status)) {
    return "secondary"
  }
  if (status === "ready") return "default"
  return "outline"
}

function operationalTone(
  status: OperatorDomainRenewalStatus["operationalStatus"],
  daysRemaining: number | null,
) {
  if (status === "failed" || status === "unready") {
    return daysRemaining !== null && daysRemaining <= 7 ? "danger" : "warning"
  }

  if (status === "disabled") return "neutral"
  return "good"
}

function expiryValue(
  item: OperatorDomainRenewalStatus,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (item.certificateExpired) {
    return t("operatorDomains.workspace.renewal.expired")
  }

  if (item.daysRemaining === null) {
    return t("operatorDomains.common.unknown")
  }

  return t("operatorDomains.workspace.renewal.daysRemaining", {
    days: item.daysRemaining,
  })
}

function operationalDescription(
  item: OperatorDomainRenewalStatus,
  t: ReturnType<typeof useI18n>["t"],
  language: "en" | "de",
) {
  switch (item.operationalStatus) {
    case "scheduled":
      return t("operatorDomains.workspace.renewal.operational.scheduledDescription", {
        days: item.daysRemaining ?? 0,
      })
    case "queued":
      return t("operatorDomains.workspace.renewal.operational.queuedDescription")
    case "running":
      return t("operatorDomains.workspace.renewal.operational.runningDescription", {
        phase: item.latestOperationStep
          ? phaseLabel(item.latestOperationStep, t)
          : t("operatorDomains.workspace.renewal.notRunning"),
      })
    case "awaiting-activation":
      return t("operatorDomains.workspace.renewal.operational.activationDescription")
    case "failed":
      return item.nextAutomaticAttemptAtUtc
        ? t("operatorDomains.workspace.renewal.operational.failedRetryDescription", {
            retry: formatDateTime(item.nextAutomaticAttemptAtUtc, language),
          })
        : t("operatorDomains.workspace.renewal.operational.failedDescription")
    case "disabled":
      return t("operatorDomains.workspace.renewal.readiness.disabledDescription")
    case "unready":
      return readinessDescription(item.readinessStatus, t)
    default:
      return t("operatorDomains.workspace.renewal.operational.readyDescription", {
        days: item.renewalWindowDays,
      })
  }
}

function operationalLabel(
  status: string,
  t: ReturnType<typeof useI18n>["t"],
  readinessStatus?: string,
) {
  switch (status) {
    case "scheduled":
      return t("operatorDomains.workspace.renewal.operational.scheduled")
    case "queued":
      return t("operatorDomains.workspace.renewal.operational.queued")
    case "running":
      return t("operatorDomains.workspace.renewal.operational.running")
    case "awaiting-activation":
      return t("operatorDomains.workspace.renewal.operational.awaitingActivation")
    case "failed":
      return t("operatorDomains.workspace.renewal.operational.failed")
    case "disabled":
      return t("operatorDomains.workspace.renewal.readiness.disabled")
    case "unready":
      return readinessLabel(readinessStatus, t)
    case "succeeded":
      return t("operatorDomains.workspace.renewal.operational.succeeded")
    default:
      return t("operatorDomains.workspace.renewal.operational.ready")
  }
}

function readinessLabel(
  status: string | undefined,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (status) {
    case "RenewalCredentialRequired":
      return t("operatorDomains.workspace.renewal.readiness.credentialRequired")
    case "AcmeEmailRequired":
      return t("operatorDomains.workspace.renewal.readiness.emailRequired")
    case "ProductionCertificateRequired":
      return t("operatorDomains.workspace.renewal.readiness.productionRequired")
    case "UnsupportedProvider":
      return t("operatorDomains.workspace.renewal.readiness.unsupportedProvider")
    case "Disabled":
      return t("operatorDomains.workspace.renewal.readiness.disabled")
    case "Ready":
      return t("operatorDomains.workspace.renewal.readiness.ready")
    default:
      return t("operatorDomains.workspace.renewal.operational.unready")
  }
}

function phaseLabel(phase: string, t: ReturnType<typeof useI18n>["t"]) {
  switch (phase) {
    case "eligibility":
      return t("operatorDomains.workspace.renewal.phase.eligibility")
    case "credential":
      return t("operatorDomains.workspace.renewal.phase.credential")
    case "dns-challenge":
      return t("operatorDomains.workspace.renewal.phase.dns")
    case "issuance":
      return t("operatorDomains.workspace.renewal.phase.issuance")
    case "validation":
      return t("operatorDomains.workspace.renewal.phase.validation")
    case "awaiting-activation":
      return t("operatorDomains.workspace.renewal.phase.awaitingActivation")
    case "activation":
      return t("operatorDomains.workspace.renewal.phase.activation")
    case "activation-complete":
      return t("operatorDomains.workspace.renewal.phase.complete")
    case "failed":
      return t("operatorDomains.workspace.renewal.phase.failed")
    default:
      return phase
  }
}

function readinessDescription(status: string, t: ReturnType<typeof useI18n>["t"]) {
  switch (status) {
    case "Ready":
      return t("operatorDomains.workspace.renewal.readiness.readyDescription")
    case "Disabled":
      return t("operatorDomains.workspace.renewal.readiness.disabledDescription")
    case "AcmeEmailRequired":
      return t("operatorDomains.workspace.renewal.readiness.emailRequiredDescription")
    case "ProductionCertificateRequired":
      return t("operatorDomains.workspace.renewal.readiness.productionRequiredDescription")
    case "UnsupportedProvider":
      return t("operatorDomains.workspace.renewal.readiness.unsupportedProviderDescription")
    default:
      return t("operatorDomains.workspace.renewal.readiness.credentialRequiredDescription")
  }
}
