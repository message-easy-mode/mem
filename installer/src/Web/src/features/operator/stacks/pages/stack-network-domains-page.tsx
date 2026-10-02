import { Archive } from "lucide-react"
import { useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent } from "@/components/ui/card"

import { formatOptionalStackDateTime } from "../components/stack-workspace-formatting"
import { StackNetworkDomainsCard } from "../components/stack-network-domains-card"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useRuntimeStack,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

/**
 * Stack-scoped public routing and domain facts. It deliberately projects the
 * existing runtime inspection only; domain editing, DNS inspection, NPM edits,
 * and certificate lifecycle actions stay in their existing operator workflows.
 */
export function StackNetworkDomainsPage() {
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

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="network-domains" /> : null}

      {stackQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.network.loadErrorTitle")}</AlertTitle>
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
            {t("stacks.network.loading")}
          </CardContent>
        </Card>
      ) : stack ? (
        <StackNetworkDomainsCard stack={stack} />
      ) : null}
    </div>
  )
}
