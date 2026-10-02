import { ExternalLink } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { localizeServiceState } from "@/features/operator/services/lib/service-state"

import { ServiceIcon } from "./service-icon"
import { ServiceStatusPill } from "./service-status-pill"

export type ServiceRuntimeField = {
  label: string
  value: string | number | null | undefined
  technical?: boolean
}

export type ServiceWorkspaceAction = {
  label: string
  href: string
  external?: boolean
}

type Props = {
  serviceName: string
  title: string
  description: string
  loading: boolean
  hasData: boolean
  unavailable: boolean
  exists: boolean
  running: boolean
  state: string | null | undefined
  fields: ServiceRuntimeField[]
  warnings?: readonly string[]
  actions?: ServiceWorkspaceAction[]
  boundary: string
}

export function ServiceRuntimeWorkspace({
  serviceName,
  title,
  description,
  loading,
  hasData,
  unavailable,
  exists,
  running,
  state,
  fields,
  warnings = [],
  actions = [],
  boundary,
}: Props) {
  const { t } = useI18n()
  const localizedState = localizeServiceState(t, state)

  return (
    <div className="space-y-6">
      <div>
        <PageBreadcrumbs
          items={[
            { label: t("services.title"), to: "/services" },
            { label: title },
          ]}
        />
        <div className="flex items-start gap-3">
          <ServiceIcon serviceName={serviceName} />
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
            <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
              {description}
            </p>
          </div>
        </div>
      </div>

      {loading && !hasData ? (
        <Card>
          <CardContent className="py-8 text-sm text-muted-foreground">
            {t("services.workspace.loading")}
          </CardContent>
        </Card>
      ) : null}

      {unavailable && !hasData ? (
        <Alert variant="destructive">
          <AlertTitle>{t("services.workspace.unavailable.title")}</AlertTitle>
          <AlertDescription>
            {t("services.workspace.unavailable.description")}
          </AlertDescription>
        </Alert>
      ) : null}

      {hasData ? (
        <>
          <Card>
            <CardHeader>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <CardTitle>{t("services.workspace.overview.title")}</CardTitle>
                  <CardDescription>
                    {t("services.workspace.overview.description")}
                  </CardDescription>
                </div>
                <ServiceStatusPill
                  exists={exists}
                  running={running}
                  state={localizedState}
                  supported
                />
              </div>
            </CardHeader>
            <CardContent className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              {fields.map((field) => (
                <RuntimeField key={field.label} field={field} />
              ))}
            </CardContent>
          </Card>

          {actions.length > 0 ? (
            <Card>
              <CardHeader>
                <CardTitle>{t("services.workspace.actions.title")}</CardTitle>
                <CardDescription>
                  {t("services.workspace.actions.description")}
                </CardDescription>
              </CardHeader>
              <CardContent className="flex flex-wrap gap-2">
                {actions.map((action) =>
                  action.external ? (
                    <Button key={`${action.label}-${action.href}`} asChild size="sm">
                      <a href={action.href} target="_blank" rel="noreferrer">
                        {action.label}
                        <ExternalLink className="ml-2 h-3.5 w-3.5" />
                      </a>
                    </Button>
                  ) : (
                    <Button
                      key={`${action.label}-${action.href}`}
                      asChild
                      size="sm"
                      variant="outline"
                    >
                      <Link to={action.href}>{action.label}</Link>
                    </Button>
                  ),
                )}
              </CardContent>
            </Card>
          ) : null}

          {warnings.length > 0 ? (
            <Alert>
              <AlertTitle>{t("services.workspace.warnings.title")}</AlertTitle>
              <AlertDescription>
                <ul className="list-disc space-y-1 pl-5">
                  {warnings.map((warning, index) => (
                    <li key={`${warning}-${index}`}>{warning}</li>
                  ))}
                </ul>
              </AlertDescription>
            </Alert>
          ) : null}

          <Card>
            <CardHeader>
              <CardTitle>{t("services.workspace.boundary.title")}</CardTitle>
            </CardHeader>
            <CardContent className="text-sm text-muted-foreground">
              {boundary}
            </CardContent>
          </Card>
        </>
      ) : null}
    </div>
  )
}

function RuntimeField({ field }: { field: ServiceRuntimeField }) {
  const value = field.value === null || field.value === undefined || field.value === ""
    ? "-"
    : String(field.value)

  return (
    <div className="min-w-0 rounded-lg border border-border/80 bg-background/30 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {field.label}
      </div>
      <div
        className={
          field.technical
            ? "mt-1 break-all font-mono text-xs text-foreground"
            : "mt-1 break-words text-sm text-foreground"
        }
      >
        {value}
      </div>
    </div>
  )
}
