import { Archive } from "lucide-react"
import { useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent } from "@/components/ui/card"

import { StackStorageMediaCard } from "../components/stack-storage-media-card"
import { formatOptionalStackDateTime } from "../components/stack-workspace-formatting"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useRuntimeStack,
  useRuntimeStackStorage,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

/**
 * Stack-scoped filesystem and media evidence. This route deliberately projects
 * the existing HostAgent storage inspection and does not expose file browsing,
 * cleanup, or other filesystem mutation operations.
 */
export function StackStorageMediaPage() {
  const { language, t } = useI18n()
  const { slugOrId } = useParams()
  const stackQuery = useRuntimeStack(slugOrId)
  const storageQuery = useRuntimeStackStorage(slugOrId)
  const runDoctorInDiagnostics = useRunDoctorInStackDiagnostics(slugOrId)
  const backup = useBackupRuntimeStack(slugOrId ?? "")

  const stack = stackQuery.data

  function refreshWorkspace() {
    void stackQuery.refetch()
    void storageQuery.refetch()
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
        refreshing={stackQuery.isFetching || storageQuery.isFetching}
        onRunDoctor={runDoctorInDiagnostics}
        doctorPending={false}
        onCreateBackup={() => backup.mutate()}
        backupPending={backup.isPending}
      />

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="storage-media" /> : null}

      {stackQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.storage.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{stackQuery.error.message}</AlertDescription>
        </Alert>
      )}

      {storageQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.storage.inspectionErrorTitle")}</AlertTitle>
          <AlertDescription>{storageQuery.error.message}</AlertDescription>
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
              date: formatOptionalStackDateTime(backup.data.createdAtUtc, language, t("stacks.common.unknown")),
            })}
          </AlertDescription>
        </Alert>
      )}

      {!stack && stackQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.storage.pageLoading")}
          </CardContent>
        </Card>
      ) : stack ? (
        <StackStorageMediaCard storage={storageQuery.data ?? null} isLoading={storageQuery.isLoading} />
      ) : null}
    </div>
  )
}
