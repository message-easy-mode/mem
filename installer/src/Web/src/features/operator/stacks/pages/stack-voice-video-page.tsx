import { Archive, RadioTower } from "lucide-react"
import { useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import { StackTurnStatusCard } from "../components/stack-turn-status-card"
import { formatOptionalStackDateTime } from "../components/stack-workspace-formatting"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useRuntimeStack,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

/**
 * Stack-scoped voice and video configuration evidence. The TURN panel uses
 * server-authored live inspection of Synapse, persisted runtime metadata, and
 * platform Coturn readiness. It does not issue credentials, perform calls,
 * open relay ports, or change stack or Coturn configuration.
 */
export function StackVoiceVideoPage() {
  const { language, t } = useI18n()
  const { slugOrId } = useParams()
  const stackQuery = useRuntimeStack(slugOrId)
  const runDoctorInDiagnostics = useRunDoctorInStackDiagnostics(slugOrId)
  const backup = useBackupRuntimeStack(slugOrId ?? "")

  const stack = stackQuery.data

  function refreshWorkspace() {
    void stackQuery.refetch()
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
        refreshing={stackQuery.isFetching}
        onRunDoctor={runDoctorInDiagnostics}
        doctorPending={false}
        onCreateBackup={() => backup.mutate()}
        backupPending={backup.isPending}
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="voice-video" /> : null}

      {stackQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.voice.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{stackQuery.error.message}</AlertDescription>
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
              date: formatOptionalStackDateTime(backup.data.createdAtUtc, language, t("stacks.common.unknown")),
            })}
          </AlertDescription>
        </Alert>
      ) : null}

      {!stack && stackQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.voice.pageLoading")}
          </CardContent>
        </Card>
      ) : stack ? (
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <RadioTower className="h-5 w-5" />
                {t("stacks.voice.title")}
              </CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("stacks.voice.description")}
              </p>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">
              {t("stacks.voice.readOnlyNotice")}
            </CardContent>
          </Card>

          <StackTurnStatusCard slugOrId={slugOrId} />
        </div>
      ) : null}
    </div>
  )
}
