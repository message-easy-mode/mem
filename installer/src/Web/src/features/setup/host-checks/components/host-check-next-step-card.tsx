import { ArrowRight, Eye, RotateCw, AlertTriangle } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

import type { HostCheckRunResponse } from "../api/host-checks.types"

type Props = {
  run: HostCheckRunResponse
  onRerun: () => void
  rerunPending?: boolean
  readOnly?: boolean
}

const NEXT_SETUP_ROUTE = "/setup/domain"

export function HostCheckNextStepCard({ run, onRerun, rerunPending, readOnly = false }: Props) {
  const { t } = useI18n()
  const blockingIssues = getBlockingIssues(run)
  const canContinue = blockingIssues.length === 0

  if (!canContinue) {
    return (
      <Card className="border-red-500/30 bg-red-500/5">
        <CardHeader className="pb-3">
          <CardTitle className="flex items-center gap-2">
            <AlertTriangle className="h-5 w-5 text-red-300" />
            {t("setup.checks.fixBlocking")}
          </CardTitle>
          <CardDescription>
            {t("setup.checks.blocking", { count: blockingIssues.length })}
          </CardDescription>
        </CardHeader>

        <CardContent className={readOnly ? "text-sm text-muted-foreground" : "flex justify-end"}>
          {readOnly ? (
            <div className="flex items-start gap-2">
              <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              {t("setup.checks.previewReadOnly")}
            </div>
          ) : (
            <Button variant="outline" onClick={onRerun} disabled={rerunPending}>
              <RotateCw className="mr-2 h-4 w-4" />
              {rerunPending ? t("setup.checks.rerunning") : t("setup.checks.rerun")}
            </Button>
          )}
        </CardContent>
      </Card>
    )
  }

  return (
    <Card className="border-emerald-500/30 bg-emerald-500/5">
      <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div>
          <div className="font-medium">{t("setup.checks.ready")}</div>
          <div className="mt-1 text-sm text-muted-foreground">
            {t("setup.checks.readyDescription")}
          </div>
        </div>

        {readOnly ? (
          <div className="flex max-w-sm shrink-0 items-start gap-2 text-sm text-muted-foreground">
            <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            {t("setup.checks.previewContinueLocked")}
          </div>
        ) : (
          <div className="flex shrink-0 flex-col gap-2 sm:flex-row">
            <Button variant="outline" onClick={onRerun} disabled={rerunPending}>
              <RotateCw className="mr-2 h-4 w-4" />
              {rerunPending ? t("setup.checks.rerunning") : t("setup.checks.rerun")}
            </Button>

            <Button asChild>
              <Link to={NEXT_SETUP_ROUTE}>
                {t("setup.checks.continueDomain")}
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function getBlockingIssues(run: HostCheckRunResponse) {
  return run.groups.flatMap((group) =>
    group.checks.filter(
      (check) =>
        check.blocking &&
        (check.status === "Fail" ||
          check.status === "Warning" ||
          check.status === "Unavailable" ||
          check.status === "Unknown"),
    ),
  )
}
