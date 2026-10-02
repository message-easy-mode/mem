import { Archive } from "lucide-react"
import { useParams } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent } from "@/components/ui/card"

import { StackUsersPanel } from "../components/stack-users-panel"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useRuntimeStack,
  useRuntimeStackUsers,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

/**
 * Stack-scoped Matrix homeserver accounts. This route reuses the existing
 * HostAgent user inventory and creation commands; it does not expose MEM
 * control-plane identities, password resets, or direct Synapse administration.
 */
export function StackUsersPage() {
  const { language, t } = useI18n()
  const { slugOrId } = useParams()
  const stackQuery = useRuntimeStack(slugOrId)
  const usersQuery = useRuntimeStackUsers(slugOrId)
  const runDoctorInDiagnostics = useRunDoctorInStackDiagnostics(slugOrId)
  const backup = useBackupRuntimeStack(slugOrId ?? "")

  const stack = stackQuery.data

  function refreshWorkspace() {
    void stackQuery.refetch()
    void usersQuery.refetch()
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
        refreshing={stackQuery.isFetching || usersQuery.isFetching}
        onRunDoctor={runDoctorInDiagnostics}
        doctorPending={false}
        onCreateBackup={() => backup.mutate()}
        backupPending={backup.isPending}
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="users" /> : null}

      {stackQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.users.pageLoadErrorTitle")}</AlertTitle>
          <AlertDescription>{stackQuery.error.message}</AlertDescription>
        </Alert>
      )}

      {backup.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.workspace.backupFailedTitle")}</AlertTitle>
          <AlertDescription>{backup.error.message}</AlertDescription>
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
            {t("stacks.users.loading")}
          </CardContent>
        </Card>
      ) : stack && slugOrId ? (
        <StackUsersPanel slugOrId={slugOrId} />
      ) : null}
    </div>
  )
}
