import { useNavigate, useSearchParams } from "react-router-dom"
import { Eye, PlayCircle } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

import { HostCheckGroup } from "../components/host-check-group"
import { HostCheckNextStepCard } from "../components/host-check-next-step-card"
import { HostCheckSummaryCard } from "../components/host-check-summary-card"
import {
  useCurrentHostCheckRun,
  useStartHostCheckRun,
} from "../hooks/host-checks.queries"

export function HostChecksPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const currentRun = useCurrentHostCheckRun()
  const startRun = useStartHostCheckRun()
  const readOnlyPreview = searchParams.get("setupPreview") === "1"

  async function handleRunChecks() {
    const nextRun = await startRun.mutateAsync()
    navigate(`/setup/check-server/${nextRun.id}`)
  }

  const run = currentRun.data

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">{t("setup.checks.title")}</h1>
        <p className="max-w-3xl text-sm text-muted-foreground">
          {t("setup.checks.description")}
        </p>
      </div>

      {currentRun.isLoading && (
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            {t("setup.checks.loadingLatest")}
          </CardContent>
        </Card>
      )}

      {currentRun.error && (
        <Card>
          <CardContent className="p-6 text-sm text-red-300">
            {t("setup.checks.loadFailed", { error: String(currentRun.error) })}
          </CardContent>
        </Card>
      )}

      {!currentRun.isLoading && !currentRun.error && !run && (
        <Card>
          <CardHeader>
            <CardTitle>
              {readOnlyPreview
                ? t("setup.checks.previewNoRun.title")
                : t("setup.checks.noRun.title")}
            </CardTitle>
            <CardDescription>
              {readOnlyPreview
                ? t("setup.checks.previewNoRun.description")
                : t("setup.checks.noRun.description")}
            </CardDescription>
          </CardHeader>

          <CardContent>
            {readOnlyPreview ? (
              <div className="flex items-start gap-2 text-sm text-muted-foreground">
                <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                {t("setup.checks.previewLocked")}
              </div>
            ) : (
              <Button onClick={handleRunChecks} disabled={startRun.isPending}>
                <PlayCircle className="mr-2 h-4 w-4" />
                {startRun.isPending ? t("setup.checks.running") : t("setup.checks.run")}
              </Button>
            )}
          </CardContent>
        </Card>
      )}

      {run && (
        <>
          <HostCheckSummaryCard run={run} />

          <HostCheckNextStepCard
            run={run}
            onRerun={handleRunChecks}
            rerunPending={startRun.isPending}
            readOnly={readOnlyPreview}
          />

          <div className="space-y-4">
            {run.groups.map((group) => (
              <HostCheckGroup key={group.key} group={group} />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

export { HostChecksPage as PreflightPage }
