import { useI18n } from "@/app/i18n/i18n-context"
import { ServiceRuntimeWorkspace } from "@/features/operator/services/components/service-runtime-workspace"
import { useSeq } from "@/features/operator/services/hooks/use-seq"

export function SeqPage() {
  const { t } = useI18n()
  const runtime = useSeq()
  const data = runtime.data

  return (
    <ServiceRuntimeWorkspace
      serviceName="seq"
      title={t("services.seq.title")}
      description={t("services.seq.description")}
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
          label: t("services.workspace.field.uiPort"),
          value: data?.uiHostPort,
        },
      ]}
      warnings={data?.warnings}
      actions={[
        {
          label: t("services.seq.openDiagnostics"),
          href: "/diagnostics/seq",
        },
      ]}
      boundary={t("services.seq.boundary")}
    />
  )
}
