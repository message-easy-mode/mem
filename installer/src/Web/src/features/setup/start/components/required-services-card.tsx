import { Link } from "react-router-dom"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

import {
  getServiceStatusClassName,
  getServiceStatusLabel,
  installerServiceVisuals,
  type InstallerServiceSummary,
} from "../api/platform-status"

function ServiceCard({ service }: { service: InstallerServiceSummary }) {
  const visual = installerServiceVisuals[service.key]
  const Icon = visual.icon

  return (
    <Card>
      <CardHeader className="space-y-0 pb-3">
        <div className="flex items-start justify-between gap-4">
          <div className="flex items-start gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border bg-muted">
              <Icon className="h-5 w-5" />
            </div>

            <div>
              <CardTitle className="text-base">{service.displayName}</CardTitle>
              <CardDescription className="mt-1">{service.description}</CardDescription>

              {service.containerName && (
                <div className="mt-2 text-xs text-muted-foreground">
                  Container: <span className="font-mono">{service.containerName}</span>
                </div>
              )}

              {service.image && (
                <div className="mt-1 text-xs text-muted-foreground">
                  Image: <span className="font-mono">{service.image}</span>
                </div>
              )}
            </div>
          </div>

          <span
            className={[
              "shrink-0 rounded-full border px-2.5 py-1 text-xs font-medium",
              getServiceStatusClassName(service),
            ].join(" ")}
          >
            {getServiceStatusLabel(service)}
          </span>
        </div>
      </CardHeader>

      {visual.detailPath && (
        <CardContent className="pt-0">
          <Button asChild variant="outline" size="sm">
            <Link to={visual.detailPath}>Details</Link>
          </Button>
        </CardContent>
      )}
    </Card>
  )
}

export function RequiredServicesCard({ services }: { services: InstallerServiceSummary[] }) {
  return (
    <section className="space-y-3">
      <div>
        <h2 className="text-lg font-semibold tracking-tight">Mandatory platform</h2>
        <p className="mt-1 text-sm text-muted-foreground">
          These dependencies are required for MEM 0.2.0 platform hosting, ingress, and recovery workflows.
        </p>
      </div>

      <div className="grid gap-3 lg:grid-cols-2">
        {services.map((service) => (
          <ServiceCard key={service.key} service={service} />
        ))}
      </div>
    </section>
  )
}