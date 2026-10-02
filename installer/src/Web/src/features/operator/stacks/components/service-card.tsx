import { Database, ExternalLink, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { RuntimeStackServiceInspectResponse } from "../api/stacks.types"

type Props = {
  title: string
  service: RuntimeStackServiceInspectResponse | null
}

export function ServiceCard({ title, service }: Props) {
  const { t } = useI18n()
  const unknown = t("stacks.common.unknown")

  if (!service) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{title}</CardTitle>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          {t("stacks.services.serviceMissing")}
        </CardContent>
      </Card>
    )
  }

  const database = getDatabaseMetadata(service)

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start justify-between gap-3">
          <div>
            <CardTitle>{title}</CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">{service.serviceKey}</p>
          </div>
          {service.publicBaseUrl && (
            <Button variant="outline" size="sm" asChild>
              <a href={service.publicBaseUrl} target="_blank" rel="noreferrer">
                <ExternalLink className="mr-2 h-4 w-4" />
                {t("stacks.services.openService", { title })}
              </a>
            </Button>
          )}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 text-sm md:grid-cols-2">
          <Info label={t("stacks.services.container")} value={service.containerName ?? unknown} />
          <Info label={t("stacks.services.instanceId")} value={service.instanceId} />
          <Info label={t("stacks.services.publicHost")} value={service.publicHost ?? unknown} />
          <Info label={t("stacks.services.publicUrl")} value={service.publicBaseUrl ?? unknown} />
          <Info label={t("stacks.services.internalHost")} value={service.internalHost ?? unknown} />
          <Info label={t("stacks.services.internalUrl")} value={service.internalBaseUrl ?? unknown} />
          <Info label={t("stacks.services.routeId")} value={service.publicRouteId ?? unknown} />
          <Info label={t("stacks.services.npmCertificateId")} value={service.npmCertificateId?.toString() ?? unknown} />
        </div>

        {database.hasDatabase && (
          <div className="rounded-xl border border-border bg-background/40 p-3 text-sm">
            <div className="mb-3 flex items-center justify-between gap-3">
              <div className="flex items-center gap-2 text-xs uppercase tracking-wide text-muted-foreground">
                <Database className="h-4 w-4" />
                {t("stacks.services.database")}
              </div>
              <div className="flex items-center gap-1 text-xs text-emerald-300">
                <ShieldCheck className="h-3.5 w-3.5" />
                {t("stacks.services.secretHidden")}
              </div>
            </div>

            <div className="grid gap-3 md:grid-cols-2">
              <Info label={t("stacks.services.engine")} value={database.engine ?? unknown} />
              <Info label={t("stacks.services.host")} value={formatHost(database.host, database.port, unknown)} />
              <Info label={t("stacks.services.databaseName")} value={database.name ?? unknown} />
              <Info label={t("stacks.services.username")} value={database.username ?? unknown} />
              <Info label={t("stacks.services.secretKind")} value={database.passwordSecretKind ?? unknown} />
              <Info label={t("stacks.services.status")} value={database.status ?? unknown} />
            </div>
          </div>
        )}

        <div className="rounded-xl border border-border bg-background/40 p-3 text-sm">
          <div className="text-xs uppercase tracking-wide text-muted-foreground">
            {t("stacks.services.dataPath")}
          </div>
          <div className="mt-1 break-all font-mono text-xs text-foreground">
            {service.dataPath ?? unknown}
          </div>
        </div>

        <div className="rounded-xl border border-border bg-background/40 p-3 text-sm">
          <div className="text-xs uppercase tracking-wide text-muted-foreground">
            {t("stacks.services.configPath")}
          </div>
          <div className="mt-1 break-all font-mono text-xs text-foreground">
            {service.configPath ?? unknown}
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function getDatabaseMetadata(service: RuntimeStackServiceInspectResponse) {
  const metadata = service.runtimeMetadata ?? {}

  const engine = metadata.databaseEngine ?? metadata.matrixDatabaseEngine ?? null
  const host = metadata.databaseHost ?? metadata.matrixDatabaseHost ?? null
  const port = metadata.databasePort ?? metadata.matrixDatabasePort ?? null
  const name = metadata.databaseName ?? metadata.matrixDatabaseName ?? null
  const username = metadata.databaseUsername ?? metadata.matrixDatabaseUsername ?? null
  const passwordSecretKind =
    metadata.databasePasswordSecretKind ??
    metadata.matrixDatabasePasswordSecretKind ??
    null
  const status = metadata.databaseStatus ?? metadata.matrixDatabaseStatus ?? null

  return {
    engine,
    host,
    port,
    name,
    username,
    passwordSecretKind,
    status,
    hasDatabase:
      Boolean(engine) ||
      Boolean(host) ||
      Boolean(name) ||
      Boolean(username) ||
      Boolean(passwordSecretKind),
  }
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-foreground">{value}</div>
    </div>
  )
}

function formatHost(host: string | null, port: string | null, fallback: string) {
  if (!host && !port) return fallback
  if (host && port) return `${host}:${port}`
  return host ?? port ?? fallback
}
