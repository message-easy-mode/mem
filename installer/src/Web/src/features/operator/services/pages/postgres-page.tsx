import { useI18n } from "@/app/i18n/i18n-context"
import { ServiceRuntimeWorkspace } from "@/features/operator/services/components/service-runtime-workspace"
import { usePostgres } from "@/features/operator/services/hooks/use-postgres"

export function PostgresPage() {
  const { t } = useI18n()
  const runtime = usePostgres()
  const data = runtime.data

  return (
    <ServiceRuntimeWorkspace
      serviceName="postgres"
      title={t("services.postgres.title")}
      description={t("services.postgres.description")}
      loading={runtime.isLoading}
      hasData={Boolean(data)}
      unavailable={runtime.isError}
      exists={data?.exists ?? false}
      running={data?.running ?? false}
      state={data?.state}
      fields={[
        {
          label: t("services.workspace.field.container"),
          value: data?.containerName ?? data?.container?.name,
          technical: true,
        },
        {
          label: t("services.workspace.field.image"),
          value: data?.container?.image,
          technical: true,
        },
        {
          label: t("services.workspace.field.dockerState"),
          value: data?.container?.state ?? data?.state,
        },
        {
          label: t("services.workspace.field.hostPort"),
          value: data?.selectedHostPort,
        },
        {
          label: t("services.workspace.field.serviceName"),
          value: data?.serviceName ?? "postgres",
          technical: true,
        },
      ]}
      warnings={data?.warnings}
      boundary={t("services.postgres.boundary")}
    />
  )
}
