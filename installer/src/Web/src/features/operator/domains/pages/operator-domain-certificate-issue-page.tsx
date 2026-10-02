import { useEffect, useRef, useState } from "react"
import { Link, useParams } from "react-router-dom"
import { useForm } from "react-hook-form"
import { ArrowLeft, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { IssueCertificateCard } from "@/features/shared/domains/components/issue-certificate-card"
import { LastResultCard } from "@/features/shared/domains/components/last-result-card"
import { getErrorMessage } from "@/features/shared/domains/components/ingress-tls-shared"
import type {
  CertificateOperationResult,
  DomainCertificateIssueStartRequest,
} from "@/features/shared/domains/api/domains.types"
import type { IssueFormValues, LastResult } from "@/features/shared/domains/components/types"
import {
  useLatestOperatorDomainCertificateIssuance,
  useOperatorDomain,
  useOperatorDomains,
  useStartOperatorDomainCertificateIssuance,
} from "@/features/shared/domains/hooks/use-domains"
import { getMemApiProblem, getMemApiProblemCode } from "@/lib/api-problem"

export function OperatorDomainCertificateIssuePage() {
  const { domainId } = useParams()

  return domainId ? (
    <DomainCertificateIssueForm domainId={domainId} />
  ) : (
    <DomainCertificateOwnerSelection />
  )
}

function DomainCertificateOwnerSelection() {
  const { t } = useI18n()
  const domainsQuery = useOperatorDomains()

  return (
    <div className="mx-auto max-w-4xl space-y-6">
      <div className="space-y-2">
        <PageBreadcrumbs
          items={[
            { label: t("operatorDomains.common.domains"), to: "/domains" },
            { label: t("operatorDomains.inventory.title"), to: "/domains/certificates" },
            { label: t("operatorDomains.issueRoute.title") },
          ]}
        />
        <h1 className="text-2xl font-semibold tracking-tight">
          {t("operatorDomains.issueRoute.title")}
        </h1>
        <p className="max-w-3xl text-sm text-muted-foreground">
          {t("operatorDomains.issueRoute.ownerDescription")}
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorDomains.issueRoute.chooseDomain")}</CardTitle>
          <CardDescription>{t("operatorDomains.issueRoute.chooseDomainDescription")}</CardDescription>
        </CardHeader>
        <CardContent className="space-y-3">
          {domainsQuery.isLoading ? (
            <div className="text-sm text-muted-foreground">
              {t("operatorDomains.list.loading")}
            </div>
          ) : null}
          {domainsQuery.error ? (
            <div className="rounded-lg border border-red-500/30 bg-red-500/5 p-4 text-sm text-red-600 dark:text-red-300">
              {t("operatorDomains.list.loadFailed", { error: getErrorMessage(domainsQuery.error) })}
            </div>
          ) : null}
          {domainsQuery.data?.map((domain) => (
            <Link
              key={domain.id}
              to={`/domains/${domain.id}/certificates/new`}
              className="flex flex-col gap-2 rounded-xl border border-border bg-background/40 p-4 transition hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50 sm:flex-row sm:items-center sm:justify-between"
            >
              <div className="min-w-0">
                <div className="font-medium">{domain.displayName || domain.baseDomain}</div>
                <div className="text-sm text-muted-foreground">{domain.baseDomain}</div>
              </div>
              <div className="flex flex-wrap gap-2">
                {domain.isMainPlatformDomain ? <Badge>{t("operatorDomains.common.main")}</Badge> : null}
                <Badge variant="outline">{displayProvider(domain.dnsProvider)}</Badge>
              </div>
            </Link>
          ))}
          {!domainsQuery.isLoading && (domainsQuery.data?.length ?? 0) === 0 ? (
            <div className="rounded-lg border border-dashed border-border p-5 text-sm text-muted-foreground">
              {t("operatorDomains.issueRoute.noDomains")}
            </div>
          ) : null}
        </CardContent>
      </Card>

      <Button asChild variant="outline">
        <Link to="/domains/certificates">
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("operatorDomains.issueRoute.backToInventory")}
        </Link>
      </Button>
    </div>
  )
}

function DomainCertificateIssueForm({ domainId }: { domainId: string }) {
  const { t } = useI18n()
  const domainQuery = useOperatorDomain(domainId)
  const issuanceQuery = useLatestOperatorDomainCertificateIssuance(domainId)
  const startIssuanceMutation = useStartOperatorDomainCertificateIssuance(domainId)
  const [transientResult, setTransientResult] = useState<LastResult>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const pendingCertificateIssue = useRef<DomainCertificateIssueStartRequest | null>(null)
  const domain = domainQuery.data
  const operation = issuanceQuery.data
  const refetchDomain = domainQuery.refetch

  const issueForm = useForm<IssueFormValues>({
    defaultValues: {
      domain: "*.example.com",
      zone: "example.com",
      email: "",
      providerToken: "",
      storageName: "",
      useStaging: false,
    },
  })

  useEffect(() => {
    if (operation?.isTerminal && operation.certificateId) {
      void refetchDomain()
    }
  }, [operation?.certificateId, operation?.isTerminal, operation?.operationId, refetchDomain])

  useEffect(() => {
    if (!domain) return

    issueForm.reset({
      domain: `*.${domain.baseDomain}`,
      zone: domain.dnsZone ?? domain.baseDomain,
      email: "",
      providerToken: "",
      storageName: "",
      useStaging: operation?.useStaging ?? false,
    })
  }, [domain, issueForm, operation?.operationId, operation?.useStaging])

  function runCertificateIssue(request: DomainCertificateIssueStartRequest) {
    startIssuanceMutation.reset()
    setTransientResult(null)
    startIssuanceMutation.mutate(request, {
      onSuccess: (response) => {
        pendingCertificateIssue.current = null
        if (!response.operation) {
          setTransientResult({
            succeeded: false,
            status: "Failed",
            message: t("operatorDomains.certificates.issue.queueMissingOperation"),
            errorCode: "IssuanceOperationMissing",
            errorDetail: null,
            evidence: [],
          })
        }
      },
      onError: (error) => {
        if (!request.useStaging && getMemApiProblemCode(error) === "step_up_required") {
          pendingCertificateIssue.current = request
          setStepUpOpen(true)
          startIssuanceMutation.reset()
          return
        }

        pendingCertificateIssue.current = null
        setTransientResult(errorToResult(error, t))
        startIssuanceMutation.reset()
      },
    })
  }

  function handleIssue(values: IssueFormValues) {
    if (!domain) return

    const request: DomainCertificateIssueStartRequest = {
      requestId: createRequestId(),
      email: values.email,
      providerToken: values.providerToken,
      useStaging: values.useStaging,
    }

    issueForm.setValue("providerToken", "")
    runCertificateIssue(request)
  }

  function resumeAfterStepUp() {
    const request = pendingCertificateIssue.current
    pendingCertificateIssue.current = null
    if (request) runCertificateIssue(request)
  }

  if (domainQuery.isLoading) {
    return <LoadingCard message={t("operatorDomains.detail.loading")} />
  }

  if (domainQuery.error || !domain) {
    return (
      <LoadingCard
        message={
          domainQuery.error
            ? t("operatorDomains.detail.loadFailed", { error: getErrorMessage(domainQuery.error) })
            : t("operatorDomains.detail.notFound")
        }
      />
    )
  }

  const authoritativeResult = operation ? operation.result : transientResult
  const operationActive = operation?.status === "queued" || operation?.status === "running"
  const issuedCertificateExists = Boolean(
    operation?.certificateId &&
      domain.certificates.some(
        (certificate) => certificate.certificateId === operation.certificateId,
      ),
  )

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("operatorDomains.common.domains"), to: "/domains" },
              { label: domain.baseDomain, to: `/domains/${domain.id}` },
              { label: t("operatorDomains.inventory.title"), to: `/domains/${domain.id}/certificates` },
              { label: t("operatorDomains.issueRoute.title") },
            ]}
          />
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("operatorDomains.issueRoute.domainTitle", { domain: domain.baseDomain })}
          </h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.issueRoute.domainDescription")}
          </p>
        </div>

        <Button asChild variant="outline">
          <Link to={`/domains/${domain.id}/certificates`}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("operatorDomains.issueRoute.backToDomainCertificates")}
          </Link>
        </Button>
      </div>

      <Card className="border-sky-500/30 bg-sky-500/5">
        <CardContent className="flex items-start gap-3 p-4 text-sm">
          <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-sky-500" />
          <div>
            <div className="font-medium text-foreground">
              {t("operatorDomains.issueRoute.ownerLocked", { domain: domain.baseDomain })}
            </div>
            <div className="mt-1 text-muted-foreground">
              {t("operatorDomains.issueRoute.ownerLockedDescription")}
            </div>
          </div>
        </CardContent>
      </Card>

      {issuanceQuery.error ? (
        <Card className="border-amber-500/30 bg-amber-500/5">
          <CardContent className="p-4 text-sm text-muted-foreground">
            {t("operatorDomains.certificates.issue.progressReadFailed", {
              error: getErrorMessage(issuanceQuery.error),
            })}
          </CardContent>
        </Card>
      ) : null}

      <IssueCertificateCard
        form={issueForm}
        isSubmitting={startIssuanceMutation.isPending || issuanceQuery.isLoading}
        operation={operation}
        lockDomain
        onSubmit={handleIssue}
      />
      <LastResultCard result={authoritativeResult} />

      {!operationActive &&
      operation?.certificateId &&
      operation.result?.succeeded &&
      !domainQuery.isFetching ? (
        issuedCertificateExists ? (
          <div>
            <Button asChild variant="outline">
              <Link to={`/domains/${domain.id}/certificates/${encodeURIComponent(operation.certificateId)}`}>
                {t("operatorDomains.certificates.issue.openIssuedCertificate")}
              </Link>
            </Button>
          </div>
        ) : (
          <div className="rounded-lg border border-border bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
            {t("operatorDomains.certificates.issue.issuedCertificateDeleted")}
          </div>
        )
      ) : null}

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            window.setTimeout(() => {
              pendingCertificateIssue.current = null
            }, 0)
          }
        }}
        onVerified={resumeAfterStepUp}
      />
    </div>
  )
}

function createRequestId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID()
  }

  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (character) => {
    const random = Math.floor(Math.random() * 16)
    const value = character === "x" ? random : (random & 0x3) | 0x8
    return value.toString(16)
  })
}

function LoadingCard({ message }: { message: string }) {
  return (
    <Card>
      <CardContent className="p-6 text-sm text-muted-foreground">{message}</CardContent>
    </Card>
  )
}

function displayProvider(value: string) {
  return value.toLowerCase() === "desec" ? "deSEC" : value
}

function errorToResult(
  error: unknown,
  t: ReturnType<typeof useI18n>["t"],
): CertificateOperationResult {
  const problem = getMemApiProblem(error)
  if (problem) {
    const evidence: CertificateOperationResult["evidence"] = []
    for (const [key, value] of [
      ["incidentId", problem.incidentId],
      ["correlationId", problem.correlationId],
      ["traceId", problem.traceId],
    ] as const) {
      if (value) evidence.push({ key, value, sensitive: false, status: "Info" })
    }

    return {
      succeeded: false,
      status: "Failed",
      message: problem.detail ?? problem.title ?? t("operatorDomains.certificates.result.operationFailure"),
      errorCode: problem.code ?? problem.error ?? "ApiProblem",
      errorDetail: problem.suggestedAction ?? null,
      evidence,
    }
  }

  return {
    succeeded: false,
    status: "Failed",
    message: t("operatorDomains.certificates.result.frontendFailure"),
    errorCode: "FrontendRequestFailed",
    errorDetail: getErrorMessage(error),
    evidence: [],
  }
}
