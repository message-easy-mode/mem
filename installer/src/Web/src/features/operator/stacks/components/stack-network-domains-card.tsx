import { ExternalLink, Globe2, Network, Route, Server, ShieldCheck } from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type {
  RuntimeStackInspectResponse,
  RuntimeStackServiceInspectResponse,
} from "../api/stacks.types"

type StackNetworkDomainsCardProps = {
  stack: RuntimeStackInspectResponse
}

type EndpointCardProps = {
  title: string
  openLabel: string
  service: RuntimeStackServiceInspectResponse | null
}

/**
 * Read-only network and public-domain evidence taken from the existing
 * runtime-stack inspection projection. This component intentionally does not
 * infer DNS provider state, certificate expiry, or any editable NPM settings.
 */
export function StackNetworkDomainsCard({ stack }: StackNetworkDomainsCardProps) {
  const { language, t } = useI18n()
  const notReported = t("stacks.network.notReported")

  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <Network className="h-5 w-5" />
                {t("stacks.network.title")}
              </CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("stacks.network.description")}
              </p>
            </div>
            <div className="inline-flex w-fit items-center gap-2 rounded-full border border-sky-500/20 bg-sky-500/10 px-3 py-1 text-xs text-sky-100">
              <Globe2 className="h-3.5 w-3.5" />
              {t("stacks.network.readOnlyEvidence")}
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 text-sm md:grid-cols-3">
            <Info
              label={t("stacks.network.lastVerified")}
              value={stack.lastVerifiedAtUtc ? formatDateTime(stack.lastVerifiedAtUtc, language) : t("stacks.common.notRecorded")}
            />
            <Info label={t("stacks.network.matrixPublicHost")} value={stack.matrix?.publicHost ?? notReported} />
            <Info label={t("stacks.network.elementPublicHost")} value={stack.element?.publicHost ?? notReported} />
          </div>

          <div className="rounded-xl border border-sky-500/20 bg-sky-500/10 p-4 text-sm text-sky-100">
            {t("stacks.network.readOnlyNotice")}
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <NetworkEndpointCard
          title={t("stacks.network.matrixRouteTitle")}
          openLabel={t("stacks.network.openMatrix")}
          service={stack.matrix}
        />
        <NetworkEndpointCard
          title={t("stacks.network.elementRouteTitle")}
          openLabel={t("stacks.network.openElement")}
          service={stack.element}
        />
      </div>
    </div>
  )
}

function NetworkEndpointCard({ title, openLabel, service }: EndpointCardProps) {
  const { t } = useI18n()

  if (!service) {
    return (
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Route className="h-5 w-5" />
            {title}
          </CardTitle>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          {t("stacks.network.routeMissing")}
        </CardContent>
      </Card>
    )
  }

  const hasPublicEndpoint = Boolean(service.publicHost && service.publicBaseUrl)
  const notRecorded = t("stacks.common.notRecorded")

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Route className="h-5 w-5" />
              {title}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">{service.serviceKey}</p>
          </div>
          {service.publicBaseUrl ? (
            <Button variant="outline" size="sm" asChild>
              <a href={service.publicBaseUrl} target="_blank" rel="noreferrer">
                <ExternalLink className="mr-2 h-4 w-4" />
                {openLabel}
              </a>
            </Button>
          ) : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 text-sm md:grid-cols-2">
          <Info label={t("stacks.network.publicDomain")} value={service.publicHost ?? notRecorded} />
          <Info label={t("stacks.network.publicAddress")} value={service.publicBaseUrl ?? notRecorded} />
          <Info label={t("stacks.network.npmRouteId")} value={service.publicRouteId ?? notRecorded} />
          <Info
            label={t("stacks.network.npmCertificateId")}
            value={service.npmCertificateId?.toString() ?? notRecorded}
          />
        </div>

        <div className="rounded-xl border border-border bg-background/40 p-4">
          <div className="mb-3 flex items-center gap-2 text-xs uppercase tracking-wide text-muted-foreground">
            <Server className="h-4 w-4" />
            {t("stacks.network.internalDelivery")}
          </div>
          <div className="grid gap-3 text-sm md:grid-cols-2">
            <Info label={t("stacks.network.internalHost")} value={service.internalHost ?? notRecorded} />
            <Info label={t("stacks.network.internalAddress")} value={service.internalBaseUrl ?? notRecorded} />
          </div>
        </div>

        {hasPublicEndpoint ? (
          <div className="flex items-start gap-2 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4 text-sm text-emerald-100">
            <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{t("stacks.network.publicEndpointRecorded")}</span>
          </div>
        ) : (
          <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 p-4 text-sm text-amber-100">
            {t("stacks.network.publicEndpointIncomplete")}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-foreground">{value}</div>
    </div>
  )
}
