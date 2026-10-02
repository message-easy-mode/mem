import { useI18n } from "@/app/i18n/i18n-context"
import {
  ServiceRuntimeWorkspace,
  type ServiceWorkspaceAction,
} from "@/features/operator/services/components/service-runtime-workspace"
import { useNpm } from "@/features/operator/services/hooks/use-npm"

export function NpmPage() {
  const { t } = useI18n()
  const runtime = useNpm()
  const data = runtime.data
  const adminUrl = data?.adminHostPort
    ? `http://localhost:${data.adminHostPort}`
    : null

  const actions: ServiceWorkspaceAction[] = [
    ...(adminUrl
      ? [
          {
            label: t("services.npm.open"),
            href: adminUrl,
            external: true,
          },
        ]
      : []),
    {
      label: t("services.npm.manageDomains"),
      href: "/domains/certificates",
    },
  ]

  return (
    <ServiceRuntimeWorkspace
      serviceName="npm"
      title={t("services.npm.title")}
      description={t("services.npm.description")}
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
          label: t("services.workspace.field.httpPort"),
          value: data?.httpHostPort,
        },
        {
          label: t("services.workspace.field.adminPort"),
          value: data?.adminHostPort,
        },
        {
          label: t("services.workspace.field.httpsPort"),
          value: data?.httpsHostPort,
        },
      ]}
      warnings={data?.warnings}
      actions={actions}
      boundary={t("services.npm.boundary")}
    />
  )
}
