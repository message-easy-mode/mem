import { useEffect, useRef, useState } from "react"
import { Archive } from "lucide-react"
import { useLocation, useNavigate, useParams } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent } from "@/components/ui/card"

import {
  getRuntimeStackProblemDetail,
  RuntimeStackDoctorTransportTimeoutError,
} from "../api/stacks.api"
import { StackDiagnosticsPanel } from "../components/stack-diagnostics-panel"
import { StackDoctorHistoryPanel } from "../components/stack-doctor-history-panel"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import { isStackDiagnosticsDoctorRequest } from "../hooks/use-stack-diagnostics-navigation"
import {
  useBackupRuntimeStack,
  useCurrentRuntimeStackDoctor,
  useDoctorRuntimeStack,
  useRuntimeStack,
  useRuntimeStackDoctorHistory,
  useRuntimeStackDoctorReport,
} from "../hooks/use-runtime-stacks"

/**
 * Stack-scoped readiness diagnostics. This route projects the latest persisted
 * Doctor report and replaces it with the live result of a newly completed run.
 * It does not invent host diagnostics, log access, or repair actions.
 */
export function StackDiagnosticsPage() {
  const { slugOrId } = useParams()
  const location = useLocation()
  const navigate = useNavigate()
  const doctorRequestStartedRef = useRef(false)
  const [historyPage, setHistoryPage] = useState(1)
  const [selectedReportId, setSelectedReportId] = useState<string | null>(null)
  const stackQuery = useRuntimeStack(slugOrId)
  const currentDoctorQuery = useCurrentRuntimeStackDoctor(slugOrId)
  const historyQuery = useRuntimeStackDoctorHistory(slugOrId, historyPage)
  const selectedReportQuery = useRuntimeStackDoctorReport(slugOrId, selectedReportId)
  const doctor = useDoctorRuntimeStack(slugOrId ?? "")
  const backup = useBackupRuntimeStack(slugOrId ?? "")
  const { language, t } = useI18n()

  const stack = stackQuery.data
  const {
    data: doctorData,
    error: doctorError,
    isPending: doctorPending,
    mutate: runDoctor,
  } = doctor
  const currentDoctor = doctorData ?? currentDoctorQuery.data ?? null
  const shouldRunDoctor = isStackDiagnosticsDoctorRequest(location.state)
  const doctorInProgress = doctorPending

  useEffect(() => {
    if (!shouldRunDoctor || !slugOrId || doctorRequestStartedRef.current || doctorPending) return

    doctorRequestStartedRef.current = true
    navigate(`${location.pathname}${location.search}`, { replace: true, state: null })
    runDoctor()
  }, [
    doctorPending,
    location.pathname,
    location.search,
    navigate,
    runDoctor,
    shouldRunDoctor,
    slugOrId,
  ])

  function refreshWorkspace() {
    void stackQuery.refetch()
    void currentDoctorQuery.refetch()
    void historyQuery.refetch()
    if (selectedReportId) void selectedReportQuery.refetch()
  }

  function showPreviousHistoryPage() {
    setSelectedReportId(null)
    setHistoryPage((page) => Math.max(1, page - 1))
  }

  function showNextHistoryPage() {
    setSelectedReportId(null)
    setHistoryPage((page) => page + 1)
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
        refreshing={
          stackQuery.isFetching ||
          currentDoctorQuery.isFetching ||
          historyQuery.isFetching ||
          selectedReportQuery.isFetching
        }
        onRunDoctor={() => runDoctor()}
        doctorPending={doctorInProgress}
        onCreateBackup={() => backup.mutate()}
        backupPending={backup.isPending}
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="diagnostics" /> : null}

      {stackQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.diagnostics.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{stackQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {currentDoctorQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.diagnostics.reportLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{currentDoctorQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {doctorError ? (
        <Alert variant="destructive">
          <AlertTitle>
            {t(doctorError instanceof RuntimeStackDoctorTransportTimeoutError
              ? "stacks.diagnostics.doctorTransportTimeoutTitle"
              : "stacks.diagnostics.doctorFailedTitle")}
          </AlertTitle>
          <AlertDescription>
            {doctorError instanceof RuntimeStackDoctorTransportTimeoutError
              ? t("stacks.diagnostics.doctorTransportTimeoutDescription")
              : getRuntimeStackProblemDetail(doctorError) ?? doctorError.message}
          </AlertDescription>
        </Alert>
      ) : null}

      {historyQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.diagnostics.historyLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{historyQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {backup.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.workspace.backupFailedTitle")}</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {backup.data ? (
        <Alert className="border-emerald-500/20 bg-emerald-500/10">
          <Archive className="h-4 w-4" />
          <AlertTitle>{t("stacks.workspace.backupCreatedTitle")}</AlertTitle>
          <AlertDescription>
            {t("stacks.workspace.backupCreatedDescription", {
              backupId: backup.data.backupId,
              date: formatOptionalDate(backup.data.createdAtUtc, language, t("stacks.common.unknown")),
            })}
          </AlertDescription>
        </Alert>
      ) : null}

      {!stack && stackQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.diagnostics.loading")}
          </CardContent>
        </Card>
      ) : stack && !doctorData && currentDoctorQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.diagnostics.loadingReport")}
          </CardContent>
        </Card>
      ) : stack ? (
        <>
          <StackDiagnosticsPanel doctor={currentDoctor} isRunning={doctorInProgress} />
          <StackDoctorHistoryPanel
            history={historyQuery.data ?? null}
            loading={historyQuery.isLoading}
            fetching={historyQuery.isFetching}
            selectedReportId={selectedReportId}
            selectedReport={selectedReportQuery.data ?? null}
            selectedReportLoading={selectedReportQuery.isLoading}
            selectedReportError={
              selectedReportQuery.error
                ? getRuntimeStackProblemDetail(selectedReportQuery.error) ?? selectedReportQuery.error.message
                : null
            }
            onSelectReport={setSelectedReportId}
            onCloseReport={() => setSelectedReportId(null)}
            onPreviousPage={showPreviousHistoryPage}
            onNextPage={showNextHistoryPage}
          />
        </>
      ) : null}
    </div>
  )
}

function formatOptionalDate(
  value: string | null,
  language: ReturnType<typeof useI18n>["language"],
  fallback: string,
) {
  return value ? formatDateTime(value, language) : fallback
}
