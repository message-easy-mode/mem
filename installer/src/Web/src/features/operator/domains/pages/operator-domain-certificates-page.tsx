import { useEffect, useMemo, useState } from "react"
import { Link, useNavigate, useSearchParams } from "react-router-dom"
import { useForm } from "react-hook-form"
import { ArrowLeft, ChevronRight, RefreshCw, Settings2, ShieldCheck } from "lucide-react"

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
import { CertificateActionsCard } from "@/features/shared/domains/components/certificate-actions-card"
import { CertificateIssuancePlaceholder } from "@/features/shared/domains/components/certificate-issuance-placeholder"
import { IngressTlsIntroCard } from "@/features/shared/domains/components/ingress-tls-intro-card"
import { LastResultCard } from "@/features/shared/domains/components/last-result-card"
import { NpmReadinessCard } from "@/features/shared/domains/components/npm-readiness-card"
import { ProxyHostTestCard } from "@/features/shared/domains/components/proxy-host-test-card"
import { getErrorMessage } from "@/features/shared/domains/components/ingress-tls-shared"
import {
  readAuthoritativeCertificateImportState,
  reconcileCertificateImportOutcome,
} from "@/features/shared/domains/api/certificate-import-outcome"
import type {
  CertificateOperationResult,
  OperatorCertificateSummary,
  StoredCertificateMetadata,
} from "@/features/shared/domains/api/domains.types"
import type {
  LastResult,
  ProxyHostFormValues,
} from "@/features/shared/domains/components/types"
import {
  useActiveOperatorDomainCertificateIssuances,
  useCertificates,
  useImportCertificateToNpm,
  useNpmIngressStatus,
  useOperatorDomains,
  useProbeNpmCertificate,
  useTestNpmProxyHost,
  useValidateCertificate,
} from "@/features/shared/domains/hooks/use-domains"
import { getMemApiProblem, isMemApiProblemError } from "@/lib/api-problem"

export function OperatorDomainCertificatesPage() {
  const { intlLocale, t } = useI18n()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const legacyDomain = normalizeBaseDomain(searchParams.get("domain"))

  const domainsQuery = useOperatorDomains()
  const certificatesQuery = useCertificates()
  const activeIssuancesQuery = useActiveOperatorDomainCertificateIssuances()
  const npmStatusQuery = useNpmIngressStatus()
  const validateCertificateMutation = useValidateCertificate()
  const probeNpmCertificateMutation = useProbeNpmCertificate()
  const importCertificateToNpmMutation = useImportCertificateToNpm()

  const certificates = certificatesQuery.data ?? []
  const activeIssuances = activeIssuancesQuery.data ?? []
  const activeIssuanceKey = activeIssuances.map((item) => item.operationId).join("|")
  const refetchCertificates = certificatesQuery.refetch
  const [selectedCertificateId, setSelectedCertificateId] = useState<string | null>(null)
  const [lastResult, setLastResult] = useState<LastResult>(null)
  const [importWorkflowPending, setImportWorkflowPending] = useState(false)

  useEffect(() => {
    if (!legacyDomain || domainsQuery.isLoading) {
      return
    }

    const owner = domainsQuery.data?.find(
      (domain) => domain.baseDomain.toLowerCase() === legacyDomain.toLowerCase(),
    )

    navigate(
      owner
        ? `/domains/${owner.id}/certificates/new`
        : "/domains/certificates",
      { replace: true },
    )
  }, [domainsQuery.data, domainsQuery.isLoading, legacyDomain, navigate])

  useEffect(() => {
    if (!activeIssuancesQuery.isLoading) {
      void refetchCertificates()
    }
  }, [activeIssuanceKey, activeIssuancesQuery.isLoading, refetchCertificates])

  useEffect(() => {
    if (selectedCertificateId && certificates.some((item) => item.certificateId === selectedCertificateId)) {
      return
    }

    setSelectedCertificateId(certificates[0]?.certificateId ?? null)
  }, [certificates, selectedCertificateId])

  const selectedCertificate = useMemo(
    () => certificates.find((item) => item.certificateId === selectedCertificateId) ?? null,
    [certificates, selectedCertificateId],
  )
  const selectedId = selectedCertificate?.certificateId ?? null
  const testProxyHostMutation = useTestNpmProxyHost(selectedId)

  const proxyForm = useForm<ProxyHostFormValues>({
    defaultValues: {
      domain: "",
      forwardHost: "",
      forwardPort: 80,
      forwardScheme: "http",
    },
  })

  function handleValidate() {
    if (!selectedId) return
    validateCertificateMutation.mutate(selectedId, {
      onSuccess: setLastResult,
      onError: (error) => setLastResult(errorToResult(error, t)),
    })
  }

  function handleProbe() {
    if (!selectedId) return
    probeNpmCertificateMutation.mutate(selectedId, {
      onSuccess: setLastResult,
      onError: (error) => setLastResult(errorToResult(error, t)),
    })
  }

  async function handleImport() {
    if (!selectedId || !selectedCertificate) return

    setImportWorkflowPending(true)
    try {
      const previous = await readAuthoritativeCertificateImportState(
        selectedId,
        selectedCertificate.zone,
      ).catch(() => null)

      try {
        const result = await importCertificateToNpmMutation.mutateAsync(selectedId)
        setLastResult(result)
        await Promise.all([certificatesQuery.refetch(), npmStatusQuery.refetch()])
      } catch (error) {
        if (isMemApiProblemError(error)) {
          setLastResult(errorToResult(error, t))
          return
        }

        setLastResult(importCheckingResult(t))
        const confirmed = await reconcileCertificateImportOutcome(
          selectedId,
          selectedCertificate.zone,
          previous,
        )

        if (confirmed) {
          setLastResult(importReconciledSuccessResult(confirmed, t))
          await Promise.all([certificatesQuery.refetch(), npmStatusQuery.refetch()])
        } else {
          setLastResult(importUnconfirmedResult(t))
        }
      }
    } finally {
      setImportWorkflowPending(false)
    }
  }

  function handleTestProxyHost(values: ProxyHostFormValues) {
    testProxyHostMutation.mutate(
      {
        domain: values.domain,
        forwardHost: values.forwardHost,
        forwardPort: Number(values.forwardPort),
        forwardScheme: values.forwardScheme,
      },
      {
        onSuccess: setLastResult,
        onError: (error) => setLastResult(errorToResult(error, t)),
      },
    )
  }

  const diagnosticsBusy =
    validateCertificateMutation.isPending ||
    probeNpmCertificateMutation.isPending ||
    importCertificateToNpmMutation.isPending ||
    importWorkflowPending ||
    testProxyHostMutation.isPending

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("operatorDomains.common.domains"), to: "/domains" },
              { label: t("operatorDomains.inventory.title") },
            ]}
          />
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("operatorDomains.inventory.title")}
          </h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.inventory.description")}
          </p>
        </div>

        <div className="flex flex-col gap-2 sm:flex-row">
          <Button asChild>
            <Link to="/domains/certificates/new">
              <ShieldCheck className="mr-2 h-4 w-4" />
              {t("operatorDomains.inventory.issueCertificate")}
            </Link>
          </Button>
          <Button asChild variant="outline">
            <Link to="/domains">
              <ArrowLeft className="mr-2 h-4 w-4" />
              {t("operatorDomains.common.backToDomains")}
            </Link>
          </Button>
        </div>
      </div>

      <Card>
        <CardHeader className="flex flex-row items-start justify-between gap-3">
          <div className="space-y-1">
            <CardTitle>{t("operatorDomains.inventory.listTitle")}</CardTitle>
            <CardDescription>{t("operatorDomains.inventory.listDescription")}</CardDescription>
          </div>
          <Button variant="outline" size="sm" onClick={() => void certificatesQuery.refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("operatorDomains.common.refresh")}
          </Button>
        </CardHeader>
        <CardContent className="space-y-3">
          {certificatesQuery.isLoading ? (
            <div className="text-sm text-muted-foreground">
              {t("operatorDomains.inventory.loading")}
            </div>
          ) : null}
          {certificatesQuery.error ? (
            <div className="rounded-lg border border-red-500/30 bg-red-500/5 p-4 text-sm text-red-600 dark:text-red-300">
              {t("operatorDomains.inventory.loadFailed", {
                error: getErrorMessage(certificatesQuery.error),
              })}
            </div>
          ) : null}
          {activeIssuancesQuery.error ? (
            <div className="rounded-lg border border-amber-500/30 bg-amber-500/5 p-4 text-sm text-amber-700 dark:text-amber-300">
              {t("operatorDomains.inventory.activeIssuanceLoadFailed", {
                error: getErrorMessage(activeIssuancesQuery.error),
              })}
            </div>
          ) : null}
          {activeIssuances.length > 0 ? (
            <div className="text-xs font-medium text-sky-500 dark:text-sky-300">
              {t("operatorDomains.inventory.activeIssuanceCount", {
                count: activeIssuances.length,
              })}
            </div>
          ) : null}

          {activeIssuances.map((operation) => (
            <CertificateIssuancePlaceholder
              key={operation.operationId}
              operation={operation}
              intlLocale={intlLocale}
            />
          ))}
          {!certificatesQuery.isLoading &&
          !activeIssuancesQuery.isLoading &&
          certificates.length === 0 &&
          activeIssuances.length === 0 ? (
            <div className="rounded-lg border border-dashed border-border p-5 text-sm text-muted-foreground">
              {t("operatorDomains.inventory.empty")}
            </div>
          ) : null}

          {certificates.map((certificate) => (
            <CertificateInventoryRow
              key={certificate.certificateId}
              certificate={certificate}
              intlLocale={intlLocale}
            />
          ))}
        </CardContent>
      </Card>

      <details className="group rounded-2xl border border-border bg-card p-4">
        <summary className="cursor-pointer list-none">
          <div className="flex items-start gap-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border bg-muted">
              <Settings2 className="h-4 w-4" />
            </div>
            <div className="min-w-0 flex-1">
              <div className="text-sm font-medium text-foreground">
                {t("operatorDomains.inventory.advancedTitle")}
              </div>
              <div className="mt-1 text-sm text-muted-foreground">
                {t("operatorDomains.inventory.advancedDescription")}
              </div>
            </div>
            <ChevronRight className="mt-2 h-4 w-4 shrink-0 text-muted-foreground transition-transform group-open:rotate-90" />
          </div>
        </summary>

        <div className="mt-4 grid gap-4">
          <IngressTlsIntroCard />
          <NpmReadinessCard
            isLoading={npmStatusQuery.isLoading}
            error={npmStatusQuery.error}
            status={npmStatusQuery.data}
            onRefresh={() => void npmStatusQuery.refetch()}
          />

          <Card>
            <CardHeader>
              <CardTitle>{t("operatorDomains.inventory.diagnosticsCertificateTitle")}</CardTitle>
              <CardDescription>
                {t("operatorDomains.inventory.diagnosticsCertificateDescription")}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <select
                className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
                value={selectedCertificateId ?? ""}
                onChange={(event) => setSelectedCertificateId(event.target.value || null)}
                disabled={certificates.length === 0 || diagnosticsBusy}
                aria-label={t("operatorDomains.inventory.diagnosticsCertificateTitle")}
              >
                {certificates.length === 0 ? (
                  <option value="">{t("operatorDomains.inventory.noDiagnosticCertificate")}</option>
                ) : null}
                {certificates.map((certificate) => (
                  <option key={certificate.certificateId} value={certificate.certificateId}>
                    {certificate.domainDisplayName} · {certificate.commonName} · {certificate.isStaging ? t("operatorDomains.common.staging") : t("operatorDomains.common.production")}
                  </option>
                ))}
              </select>
            </CardContent>
          </Card>

          <CertificateActionsCard
            selectedCertificate={selectedCertificate}
            busy={diagnosticsBusy}
            onValidate={handleValidate}
            onProbeNpm={handleProbe}
            onImportToNpm={() => void handleImport()}
            isImporting={importCertificateToNpmMutation.isPending || importWorkflowPending}
          />
          <ProxyHostTestCard
            form={proxyForm}
            disabled={!selectedCertificate || diagnosticsBusy}
            isPending={testProxyHostMutation.isPending}
            onSubmit={handleTestProxyHost}
          />
          <LastResultCard result={lastResult} />
        </div>
      </details>
    </div>
  )
}

function CertificateInventoryRow({
  certificate,
  intlLocale,
}: {
  certificate: StoredCertificateMetadata
  intlLocale: string
}) {
  const { t } = useI18n()

  return (
    <Link
      to={`/domains/${certificate.domainId}/certificates/${encodeURIComponent(certificate.certificateId)}`}
      className="block rounded-xl border border-border bg-background/40 p-4 transition hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
    >
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <div className="font-medium">{certificate.commonName}</div>
            {certificate.isInUse ? (
              <Badge>{t("operatorDomains.inventory.activeForDomain")}</Badge>
            ) : null}
            {certificate.isMainPlatformCertificate ? (
              <Badge variant="secondary">{t("operatorDomains.common.mainPlatformCertificate")}</Badge>
            ) : null}
            <Badge variant={certificate.isStaging ? "outline" : "secondary"}>
              {certificate.isStaging
                ? t("operatorDomains.common.staging")
                : t("operatorDomains.common.production")}
            </Badge>
          </div>
          <div className="mt-1 text-sm text-muted-foreground">
            {certificate.domainDisplayName} · {certificate.domainBaseDomain}
          </div>
          <div className="mt-1 break-all text-xs text-muted-foreground">
            {certificate.certificateId}
          </div>
        </div>

        <div className="grid shrink-0 gap-1 text-xs text-muted-foreground md:text-right">
          <div>{t("operatorDomains.common.status")}: {certificate.status}</div>
          <div>{t("operatorDomains.inventory.source")}: {certificate.provider}</div>
          <div>
            {t("operatorDomains.common.expires")}: {certificate.expiresAtUtc
              ? new Date(certificate.expiresAtUtc).toLocaleString(intlLocale)
              : t("operatorDomains.common.expiryUnknown")}
          </div>
        </div>
      </div>
    </Link>
  )
}

function normalizeBaseDomain(value: string | null) {
  return value?.trim().replace(/^\*\./, "") ?? ""
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
      if (value) {
        evidence.push({ key, value, sensitive: false, status: "Info" })
      }
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

function importCheckingResult(t: ReturnType<typeof useI18n>["t"]): CertificateOperationResult {
  return {
    succeeded: false,
    status: "Checking",
    message: t("operatorDomains.certificates.result.importChecking"),
    errorCode: null,
    errorDetail: null,
    evidence: [],
  }
}

function importReconciledSuccessResult(
  certificate: OperatorCertificateSummary,
  t: ReturnType<typeof useI18n>["t"],
): CertificateOperationResult {
  return {
    succeeded: true,
    status: "Succeeded",
    message: t("operatorDomains.certificates.result.importReconciled"),
    errorCode: null,
    errorDetail: null,
    evidence: [
      {
        key: "outcomeReconciliation",
        value: t("operatorDomains.certificates.result.importReconciledEvidence"),
        sensitive: false,
        status: "Succeeded",
      },
      {
        key: "npmCertificateId",
        value: certificate.npmCertificateId?.toString() ?? "unknown",
        sensitive: false,
        status: "Succeeded",
      },
    ],
  }
}

function importUnconfirmedResult(t: ReturnType<typeof useI18n>["t"]): CertificateOperationResult {
  return {
    succeeded: false,
    status: "Unconfirmed",
    message: t("operatorDomains.certificates.result.importUnconfirmed"),
    errorCode: "NpmImportOutcomeUnconfirmed",
    errorDetail: t("operatorDomains.certificates.result.importUnconfirmedDetail"),
    evidence: [],
  }
}
