import { Archive, Server } from "lucide-react"
import { useParams } from "react-router-dom"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import { ServiceCard } from "../components/service-card"
import { StackNetworkDomainsCard } from "../components/stack-network-domains-card"
import { StackStorageMediaCard } from "../components/stack-storage-media-card"
import { StackTurnStatusCard } from "../components/stack-turn-status-card"
import { StackWorkspaceHeader } from "../components/stack-workspace-header"
import { StackWorkspaceNavigation } from "../components/stack-workspace-navigation"
import {
  useBackupRuntimeStack,
  useRuntimeStack,
  useRuntimeStackStorage,
} from "../hooks/use-runtime-stacks"
import { useRunDoctorInStackDiagnostics } from "../hooks/use-stack-diagnostics-navigation"

/**
 * Stack-scoped runtime facts. Services is the canonical place for container,
 * route, domain, storage, media, and TURN evidence so the workspace can keep a
 * small number of top-level sections.
 */
export function StackServicesPage() {
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

      {slugOrId ? <StackWorkspaceNavigation slugOrId={slugOrId} activeArea="services" /> : null}

      {stackQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.services.loadErrorTitle")}</AlertTitle>
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
              date: formatDateTime(backup.data.createdAtUtc, language),
            })}
          </AlertDescription>
        </Alert>
      )}

      {!stack && stackQuery.isLoading ? (
        <Card>
          <CardContent className="py-6 text-sm text-muted-foreground">
            {t("stacks.services.loading")}
          </CardContent>
        </Card>
      ) : stack ? (
        <div className="space-y-6">
          <Card>
            <CardHeader>
              <CardTitle className="flex items-center gap-2">
                <Server className="h-5 w-5" />
                {t("stacks.services.title")}
              </CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("stacks.services.description")}
              </p>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">
              {t("stacks.services.readOnlyNotice")}
            </CardContent>
          </Card>

          <div className="grid gap-6 xl:grid-cols-2">
            <ServiceCard title={t("stacks.services.matrixHomeserver")} service={stack.matrix} />
            <ServiceCard title={t("stacks.services.elementWebClient")} service={stack.element} />
          </div>

          <StackNetworkDomainsCard stack={stack} />
          <StackStorageMediaCard storage={storageQuery.data ?? null} isLoading={storageQuery.isLoading} />
          <StackTurnStatusCard slugOrId={slugOrId} />
        </div>
      ) : null}
    </div>
  )
}
