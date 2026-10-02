import { useCallback, useEffect, useState } from "react"
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { Archive } from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent } from "@/components/ui/card"

import { isRuntimeStackNotFoundProblem } from "../api/stacks.api"
import type { RuntimeStackOperationResponse } from "../api/stacks.types"
import { OperationList } from "../components/operation-list"
import { StackIdentityCard } from "../components/stack-identity-card"
import { StackDestroyControl } from "../components/stack-destroy-control"
import { StackOverview } from "../components/stack-overview"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useCurrentRuntimeStackDoctor,
  useRuntimeStack,
  useRuntimeStackOperations,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

/**
 * The existing stack-detail URL is the Stack Workspace Overview route.
 * More focused stack routes are deliberately introduced only when they can
 * project real data and preserve the existing operational contract.
 */
export function StackDetailPage() {
  const { language, t } = useI18n()
  const { slugOrId } = useParams()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()

  const stackQuery = useRuntimeStack(slugOrId)
  const stack = stackQuery.data
  const operationsQuery = useRuntimeStackOperations(slugOrId)

  const currentDoctorQuery = useCurrentRuntimeStackDoctor(slugOrId)
  const runDoctorInDiagnostics = useRunDoctorInStackDiagnostics(slugOrId)
  const backup = useBackupRuntimeStack(slugOrId ?? "")

  useEffect(() => {
    if (searchParams.get("doctor") !== "1" || !slugOrId) return

    const remainingSearch = new URLSearchParams(searchParams)
    remainingSearch.delete("doctor")
    const remainingQuery = remainingSearch.toString()

    navigate(
      `/stacks/${encodeURIComponent(slugOrId)}/diagnostics${remainingQuery ? `?${remainingQuery}` : ""}`,
      { replace: true, state: { runDoctor: true } },
    )
  }, [navigate, searchParams, slugOrId])

  const currentDoctor = currentDoctorQuery.data ?? null
  const operations = operationsQuery.data?.operations ?? []
  const latestBackupOperation =
    operations.find((operation) => operation.operation === "backup-stack") ?? null

  function refreshWorkspace() {
    void stackQuery.refetch()
    void operationsQuery.refetch()
    void currentDoctorQuery.refetch()
  }

  const returnToInventoryAfterDestroy = useCallback(() => {
    navigate("/stacks", { replace: true })
  }, [navigate])

  if (!stack && isRuntimeStackNotFoundProblem(stackQuery.error)) {
    return (
      <div className="space-y-6">
        <Card>
          <CardContent className="space-y-4 py-8">
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">
                {t("stacks.overview.notFoundTitle")}
              </h1>
              <p className="mt-2 max-w-2xl text-sm text-muted-foreground">
                {t("stacks.overview.notFoundDescription")}
              </p>
            </div>
            <Link
              to="/stacks"
              className="inline-flex h-9 items-center justify-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground shadow-sm transition-colors hover:bg-primary/90 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
            >
              {t("stacks.overview.notFoundAction")}
            </Link>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="space-y-6">
      <StackWorkspaceHeader
        slugOrId={slugOrId}
        displayName={stack?.displayName?.trim() || stack?.slug || slugOrId || t("stacks.common.stack")}
        status={stack?.health ?? stack?.status}
        verificationFreshness={stack?.verificationFreshness}
        matrixPublicBaseUrl={stack?.matrix?.publicBaseUrl}
        elementPublicBaseUrl={stack?.element?.publicBaseUrl}
        onRefresh={refreshWorkspace}
        refreshing={stackQuery.isFetching || operationsQuery.isFetching || currentDoctorQuery.isFetching}
        onRunDoctor={runDoctorInDiagnostics}
        doctorPending={false}
        onCreateBackup={() => backup.mutate()}
        backupPending={backup.isPending}
        dangerAction={
          stack ? (
            <StackDestroyControl
              target={{
                stackId: stack.stackId,
                slug: stack.slug,
                displayName: stack.displayName,
              }}
              onDestroyed={returnToInventoryAfterDestroy}
            />
          ) : undefined
        }
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} /> : null}

      {stackQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.overview.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{t("stacks.overview.loadErrorDescription")}</AlertDescription>
        </Alert>
      )}

      {backup.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.workspace.backupFailedTitle")}</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
        </Alert>
      )}

      {currentDoctorQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.diagnostics.reportLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{currentDoctorQuery.error.message}</AlertDescription>
        </Alert>
      )}

      {backup.data && (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <Archive className="h-4 w-4" />
          <AlertTitle>{t("stacks.workspace.backupCreatedTitle")}</AlertTitle>
          <AlertDescription>
            {t("stacks.workspace.backupCreatedDescription", {
              backupId: backup.data.backupId,
              date: formatDateTime(backup.data.createdAtUtc, language),
            })}
          </AlertDescription>
        </Alert>
      )}

      {!stack && stackQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.overview.loading")}
          </CardContent>
        </Card>
      ) : stack ? (
        <>
          <StackIdentityCard stack={stack} />

          <StackOverview
            stack={stack}
            doctor={currentDoctor}
            latestBackupOperation={latestBackupOperation}
            operations={operations}
          />

          <LegacyTechnicalDetails operations={operations} />
        </>
      ) : null}
    </div>
  )
}

function LegacyTechnicalDetails({ operations }: { operations: RuntimeStackOperationResponse[] }) {
  const { t } = useI18n()
  const [expanded, setExpanded] = useState(false)

  return (
    <details
      open={expanded}
      onToggle={(event) => setExpanded(event.currentTarget.open)}
      className="group rounded-xl border border-border bg-card ring-1 ring-foreground/10"
    >
      <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-4 py-4 marker:hidden [&::-webkit-details-marker]:hidden">
        <div>
          <div className="font-medium">{t("stacks.technicalDetails.title")}</div>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("stacks.technicalDetails.description")}
          </p>
        </div>
        <span className="text-xs font-medium text-muted-foreground group-open:hidden">
          {t("stacks.technicalDetails.show")}
        </span>
        <span className="hidden text-xs font-medium text-muted-foreground group-open:inline">
          {t("stacks.technicalDetails.hide")}
        </span>
      </summary>

      {expanded ? (
        <div className="space-y-6 border-t border-border px-4 py-4">
          <OperationList operations={operations} />
        </div>
      ) : null}
    </details>
  )
}
