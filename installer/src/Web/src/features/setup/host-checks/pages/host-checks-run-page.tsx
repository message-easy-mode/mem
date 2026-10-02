import { Link, useNavigate, useParams } from "react-router-dom"
import { ArrowLeft, RotateCw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"

import { HostCheckGroup } from "../components/host-check-group"
import { HostCheckNextStepCard } from "../components/host-check-next-step-card"
import { HostCheckSummaryCard } from "../components/host-check-summary-card"
import {
  useHostCheckRun,
  useStartHostCheckRun,
} from "../hooks/host-checks.queries"

export function HostCheckRunPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const { runId } = useParams()
  const hostCheckRun = useHostCheckRun(runId)
  const startRun = useStartHostCheckRun()

  async function handleRerun() {
    const nextRun = await startRun.mutateAsync()
    navigate(`/setup/check-server/${nextRun.id}`)
  }

  const run = hostCheckRun.data

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <Button asChild variant="ghost" size="sm" className="-ml-3">
            <Link to="/setup/check-server">
              <ArrowLeft className="mr-2 h-4 w-4" />
              {t("setup.checks.back")}
            </Link>
          </Button>

          <h1 className="text-2xl font-semibold tracking-tight">{t("setup.checks.runTitle")}</h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {run
              ? t("setup.checks.runId", { id: run.id })
              : t("setup.checks.loadingRun")}
          </p>
        </div>

        <Button variant="outline" onClick={handleRerun} disabled={startRun.isPending}>
          <RotateCw className="mr-2 h-4 w-4" />
          {startRun.isPending ? t("setup.checks.rerunning") : t("setup.checks.rerun")}
        </Button>
      </div>

      {hostCheckRun.isLoading && (
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            {t("setup.checks.loadingRun")}
          </CardContent>
        </Card>
      )}

      {hostCheckRun.error && (
        <Card>
          <CardContent className="p-6 text-sm text-red-300">
            {t("setup.checks.loadRunFailed", { error: String(hostCheckRun.error) })}
          </CardContent>
        </Card>
      )}

      {!hostCheckRun.isLoading && !hostCheckRun.error && !run && (
        <Card>
          <CardContent className="p-6 text-sm text-muted-foreground">
            {t("setup.checks.runNotFound")}
          </CardContent>
        </Card>
      )}

      {run && (
        <>
          <HostCheckSummaryCard run={run} />
          <HostCheckNextStepCard run={run} onRerun={handleRerun} rerunPending={startRun.isPending} />
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

export { HostCheckRunPage as PreflightRunPage }
