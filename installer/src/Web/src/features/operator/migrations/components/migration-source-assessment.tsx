import { formatMigrationDateTime, migrationUtcIso } from "./migration-time"
import { startMigrationConversion, getMigrationConversionOptions } from "../api/migration-conversions"
import { useMigrationGuidedState, useGuidedMigrationCommand, useGuidedMigrationEvidence } from "./migration-guided-state-context"
import { isMigrationOutcomeUncertain } from "../api/migration-guided-state"
import { useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Clock3,
  Database,
  Loader2,
  Server,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import type {
  MigrationSessionDetail,
  MigrationSessionSource,
} from "@/features/operator/migrations/api/migration-sessions"
import { MigrationBoundSourceStack } from "@/features/operator/migrations/components/migration-bound-source-stack"

type MigrationSourceAssessmentProps = {
  detail: MigrationSessionDetail
  onChanged?: () => Promise<unknown>
}

export function MigrationSourceAssessment({
  detail,
  onChanged,
}: MigrationSourceAssessmentProps) {
  const { intlLocale, t } = useI18n()
  const guide = useMigrationGuidedState()
  const { session, sources } = detail
  const supported = session.sourceAdapter === "mem-v010"
  const pending = session.sourceAdapter === "pending"
  const legacy = session.sourceAdapter === "legacy-neutral-contract"
  const primarySource = sources[0] ?? null
  const preparationAvailable =
    supported &&
    detail.package?.status === "package-validated" &&
    session.blockerCount === 0
  const optionsQuery = useGuidedMigrationEvidence("conversion", "conversion-options", () => getMigrationConversionOptions(session.migrationId), preparationAvailable)
  const conversionMutation = useGuidedMigrationCommand("conversion", (input: Parameters<typeof startMigrationConversion>[1]) => startMigrationConversion(session.migrationId, input))
  const [actionError, setActionError] = useState<string | null>(null)
  const conversionOptions = optionsQuery.data
  const canPrepare = guide.allows("start-conversion", "retry-conversion")

  const prepareMigrationData = async () => {
    if (!canPrepare) return
    try {
      setActionError(null)
      await conversionMutation.mutateAsync({})
      await onChanged?.().catch(() => undefined)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Server className="h-5 w-5" aria-hidden="true" />
              {t("migrationWorkspace.assessment.title")}
            </CardTitle>
            <CardDescription className="mt-1">{t("migrationWorkspace.assessment.description")}</CardDescription>
          </div>
          {supported ? <Badge variant="outline">{t("migrationWorkspace.assessment.supportedBadge")}</Badge> : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {supported ? (
          <>
            <p className="flex items-center gap-2 text-sm">
              <CheckCircle2 className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
              {t("migrationWorkspace.assessment.supportedTitle")}
            </p>

            <div className="rounded-xl border p-4 sm:p-5">
              <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {t("migrationWorkspace.assessment.oldServer")}
              </div>
              <div className="mt-2 text-lg font-semibold">{session.sourceDisplay}</div>
              <div className="mt-4 grid gap-3 sm:grid-cols-3">
                <AssessmentFact
                  label={t("migrationWorkspace.assessment.product")}
                  value={formatProduct(primarySource, session.sourceDisplay)}
                />
                <AssessmentFact
                  label={t("migrationWorkspace.detail.stackCount")}
                  value={String(session.stackCount)}
                />
                <AssessmentFact
                  label={t("migrationWorkspace.assessment.captured")}
                  value={formatOptionalDate(
                    primarySource?.capturedAtUtc ?? null,
                    intlLocale,
                    t("migrationWorkspace.notAvailable"),
                  )}
                />
              </div>
            </div>
          </>
        ) : null}

        {pending ? (
          <Alert>
            <Clock3 className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assessment.pendingTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.assessment.pendingDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {!supported && !pending && !legacy ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assessment.unknownTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.assessment.unknownDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {legacy ? (
          <Alert>
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assessment.legacyTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.assessment.legacyDescription")}</AlertDescription>
          </Alert>
        ) : null}

        {actionError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.prepare.actionErrorTitle")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        {optionsQuery.isError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.guided.evidenceTitle")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.guided.evidenceUnavailable")}</AlertDescription>
          </Alert>
        ) : null}

        {preparationAvailable && conversionOptions ? (
          <MigrationBoundSourceStack stack={conversionOptions.boundSourceStack} />
        ) : null}

        {preparationAvailable ? (
          <div className="rounded-xl border bg-primary/5 p-4 sm:p-5">
            <div className="flex items-start gap-3">
              <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
                <Database className="h-5 w-5" aria-hidden="true" />
              </span>
              <div className="min-w-0 flex-1">
                <h3 className="font-semibold">
                  {t("migrationWorkspace.prepare.prepareDataTitle")}
                </h3>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("migrationWorkspace.prepare.prepareDataDescription")}
                </p>
                <Button
                  className="mt-4"
                  disabled={!canPrepare || conversionMutation.isPending}
                  onClick={() => void prepareMigrationData()}
                >
                  {conversionMutation.isPending ? (
                    <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                  ) : (
                    <Database className="mr-2 h-4 w-4" aria-hidden="true" />
                  )}
                  {t("migrationWorkspace.prepare.prepareDataAction")}
                </Button>
              </div>
            </div>
          </div>
        ) : null}

        <details className="rounded-lg border bg-muted/10">
          <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
            {t("migrationWorkspace.assessment.technicalDetails")}
          </summary>
          <div className="space-y-4 border-t px-4 py-4">
            {supported ? <p className="text-sm text-muted-foreground">{t("migrationWorkspace.assessment.supportedDescription")}</p> : null}
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <AssessmentFact label={t("migrationWorkspace.detail.sourceAdapter")} value={session.sourceAdapter} mono />
              <AssessmentFact label={t("migrationWorkspace.detail.sourceDisplay")} value={session.sourceDisplay} />
              <AssessmentFact label={t("migrationWorkspace.detail.sourceCount")} value={String(session.sourceCount)} />
              <AssessmentFact label={t("migrationWorkspace.detail.stackCount")} value={String(session.stackCount)} />
            </div>

            {sources.length === 0 ? (
              <p className="text-sm text-muted-foreground">{t("migrationWorkspace.detail.noSources")}</p>
            ) : (
              <div className="space-y-3">
                {sources.map((source) => (
                  <div key={source.sourceId} className="rounded-lg border p-4 text-sm">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <div className="font-medium">{formatProduct(source, session.sourceDisplay)}</div>
                        <div className="mt-1 text-muted-foreground">{source.kind} · {source.sourceId}</div>
                      </div>
                      {source.capturedAtUtc ? (
                        <div className="text-xs text-muted-foreground">
                          {migrationUtcIso(source.capturedAtUtc) ?? t("migrationWorkspace.notAvailable")}
                        </div>
                      ) : null}
                    </div>
                    {source.sourceFingerprint ? (
                      <div className="mt-3 break-all font-mono text-xs text-muted-foreground">
                        {source.sourceFingerprint}
                      </div>
                    ) : null}
                  </div>
                ))}
              </div>
            )}
          </div>
        </details>
      </CardContent>
    </Card>
  )
}

function AssessmentFact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 break-all text-sm font-medium"}>{value}</div>
    </div>
  )
}

function formatProduct(source: MigrationSessionSource | null, fallback: string) {
  if (!source) return fallback
  return [source.product, source.productVersion].filter(Boolean).join(" ") || fallback
}

function formatOptionalDate(value: string | null, locale: string, fallback: string) {
  return formatMigrationDateTime(value, locale, fallback)
}
