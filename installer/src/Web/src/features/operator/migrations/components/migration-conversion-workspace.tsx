import { formatMigrationDateTime } from "./migration-time"
import { AlertCircle, CheckCircle2, Loader2, RefreshCw, RotateCcw, Wrench } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import {
  useMigrationConversionAttempts,
  useStartMigrationConversion,
} from "@/features/operator/migrations/hooks/use-migration-conversions"

export function MigrationConversionWorkspace({ detail }: { detail: MigrationSessionDetail }) {
  const { intlLocale, t } = useI18n()
  const migrationId = detail.session.migrationId
  const packageReady = detail.package?.status === "package-validated"
  const attemptsQuery = useMigrationConversionAttempts(migrationId, packageReady)
  const startMutation = useStartMigrationConversion(migrationId)
  const attempts = attemptsQuery.data ?? []
  const activeAttempt = attempts.find((attempt) =>
    attempt.status === "pending" || attempt.status === "running",
  )
  const latestAttempt = attempts[0]
  const latestCandidate = attempts.find((attempt) => attempt.candidateArtifactId)

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Wrench className="h-5 w-5" aria-hidden="true" />
              {t("migrationWorkspace.conversion.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("migrationWorkspace.conversion.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void attemptsQuery.refetch()}
            disabled={attemptsQuery.isFetching}
          >
            <RefreshCw
              className={attemptsQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"}
              aria-hidden="true"
            />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {!packageReady ? (
          <Alert>
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.conversion.notReadyTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.conversion.notReadyDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {attemptsQuery.isError ? (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.conversion.loadErrorTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.conversion.loadErrorDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {startMutation.isError ? (
          <Alert variant="destructive">
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.conversion.startErrorTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.conversion.startErrorDescription")}</AlertDescription>
          </Alert>
        ) : null}

        <div className="flex flex-wrap items-center gap-3">
          <Button
            onClick={() => startMutation.mutate({})}
            disabled={!packageReady || Boolean(activeAttempt) || startMutation.isPending}
          >
            {startMutation.isPending ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
            ) : (
              <Wrench className="mr-2 h-4 w-4" aria-hidden="true" />
            )}
            {t("migrationWorkspace.conversion.start")}
          </Button>
          <p className="text-sm text-muted-foreground">
            {activeAttempt
              ? t("migrationWorkspace.conversion.activeDescription")
              : latestCandidate
                ? t("migrationWorkspace.conversion.completedDescription")
                : t("migrationWorkspace.conversion.startDescription")}
          </p>
        </div>

        {latestCandidate ? (
          <div className="rounded-lg border border-emerald-500/30 bg-emerald-500/5 p-4">
            <div className="flex items-center gap-2">
              <CheckCircle2 className="h-5 w-5 text-emerald-500" aria-hidden="true" />
              <h3 className="font-medium">{t("migrationWorkspace.conversion.candidateTitle")}</h3>
              <Badge variant="outline">{latestCandidate.candidateVerificationStatus}</Badge>
            </div>
            <div className="mt-4 grid gap-3 md:grid-cols-2">
              <Fact label={t("migrationWorkspace.conversion.candidateId")} value={latestCandidate.candidateArtifactId} mono />
              <Fact label={t("migrationWorkspace.conversion.candidateKind")} value={latestCandidate.candidateArtifactKind} />
              <Fact label={t("migrationWorkspace.conversion.sourceHash")} value={latestCandidate.candidateSourcePackageSha256} mono />
              <Fact label={t("migrationWorkspace.conversion.artifactHash")} value={latestCandidate.candidateArtifactSha256} mono />
              <Fact label={t("migrationWorkspace.conversion.manifestHash")} value={latestCandidate.candidateManifestSha256} mono />
              <Fact label={t("migrationWorkspace.conversion.checksumsHash")} value={latestCandidate.candidateChecksumsSha256} mono />
              <Fact
                label={t("migrationWorkspace.conversion.verifiedAt")}
                value={formatDate(latestCandidate.candidateVerifiedAtUtc, intlLocale)}
              />
              <Fact label={t("migrationWorkspace.conversion.retention")} value={latestCandidate.candidateRetentionState} />
            </div>
            <p className="mt-4 text-sm text-muted-foreground">
              {t("migrationWorkspace.conversion.noCatalog")}
            </p>
          </div>
        ) : null}

        <div>
          <h3 className="font-medium">{t("migrationWorkspace.conversion.historyTitle")}</h3>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("migrationWorkspace.conversion.historyDescription")}
          </p>

          {attemptsQuery.isLoading ? (
            <div className="mt-4 flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
              {t("migrationWorkspace.conversion.loading")}
            </div>
          ) : attempts.length === 0 ? (
            <div className="mt-4 rounded-lg border border-dashed p-4 text-sm text-muted-foreground">
              {t("migrationWorkspace.conversion.empty")}
            </div>
          ) : (
            <div className="mt-4 space-y-3">
              {attempts.map((attempt) => {
                const canRetry = attempt.status === "failed" || attempt.status === "cancelled"
                return (
                  <div key={attempt.conversionAttemptId} className="rounded-lg border p-4">
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                      <div>
                        <div className="flex flex-wrap items-center gap-2">
                          <span className="font-mono text-sm">{attempt.conversionAttemptId}</span>
                          <Badge variant={attempt.status === "failed" ? "destructive" : "outline"}>
                            {attempt.status}
                          </Badge>
                        </div>
                        <p className="mt-2 text-sm text-muted-foreground">
                          {t("migrationWorkspace.conversion.currentStep")}: {attempt.currentStep}
                        </p>
                      </div>
                      {canRetry ? (
                        <Button
                          size="sm"
                          variant="outline"
                          onClick={() => startMutation.mutate({ retryOfConversionAttemptId: attempt.conversionAttemptId })}
                          disabled={Boolean(activeAttempt) || startMutation.isPending}
                        >
                          <RotateCcw className="mr-2 h-4 w-4" aria-hidden="true" />
                          {t("migrationWorkspace.conversion.retry")}
                        </Button>
                      ) : null}
                    </div>
                    <div className="mt-3 grid gap-2 text-sm md:grid-cols-3">
                      <Fact label={t("migrationWorkspace.conversion.startedAt")} value={formatDate(attempt.startedAtUtc, intlLocale)} />
                      <Fact label={t("migrationWorkspace.conversion.completedAt")} value={formatDate(attempt.completedAtUtc, intlLocale)} />
                      <Fact label={t("migrationWorkspace.conversion.resultCode")} value={attempt.resultCode} />
                    </div>
                    {attempt.failureSummary ? (
                      <Alert variant="destructive" className="mt-3">
                        <AlertCircle className="h-4 w-4" />
                        <AlertTitle>{attempt.failureCode ?? t("migrationWorkspace.conversion.failed")}</AlertTitle>
                        <AlertDescription>{attempt.failureSummary}</AlertDescription>
                      </Alert>
                    ) : null}
                  </div>
                )
              })}
            </div>
          )}
        </div>

        {latestAttempt?.status === "running" ? (
          <p className="text-sm text-muted-foreground">
            {t("migrationWorkspace.conversion.refreshSafe")}
          </p>
        ) : null}
      </CardContent>
    </Card>
  )
}

function Fact({ label, value, mono = false }: { label: string; value: string | null | undefined; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 text-sm"}>{value || "—"}</div>
    </div>
  )
}

function formatDate(value: string | null | undefined, locale: string) {
  return formatMigrationDateTime(value, locale)
}
