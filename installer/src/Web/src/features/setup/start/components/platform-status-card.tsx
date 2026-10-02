import {
  AlertTriangle,
  CheckCircle2,
  Gauge,
  ServerCog,
  ShieldCheck,
} from "lucide-react"

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

import {
  getInstallationStateLabel,
  getRecommendedActionLabel,
  type PlatformStatusResponse,
} from "../api/platform-status"

export function PlatformStatusCard({ status }: { status: PlatformStatusResponse }) {
  const requiredRunningCount = status.requiredServices.filter((service) => service.running).length
  const installed = status.installationState === "installed"

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
          <div>
            <CardTitle>Platform status</CardTitle>
            <CardDescription className="mt-2">
              {installed
                ? "The MEM Platform appears to be running on this host."
                : status.installationState === "partially-installed"
                  ? "Some MEM Platform services appear to exist on this host."
                  : "No complete MEM Platform setup has been detected on this host."}
            </CardDescription>
          </div>

          <div
            className={[
              "inline-flex w-fit items-center gap-2 rounded-full border px-3 py-1 text-sm font-medium",
              installed
                ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
                : status.installationState === "partially-installed"
                  ? "border-amber-500/30 bg-amber-500/10 text-amber-300"
                  : "border-amber-500/30 bg-amber-500/10 text-amber-300",
            ].join(" ")}
          >
            {installed ? <CheckCircle2 className="h-4 w-4" /> : <AlertTriangle className="h-4 w-4" />}
            {getInstallationStateLabel(status.installationState)}
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="grid gap-3 md:grid-cols-4">
          <div className="rounded-xl border border-border bg-muted/30 p-4">
            <div className="flex items-center gap-2 text-sm font-medium">
              <Gauge className="h-4 w-4" />
              Docker
            </div>
            <div className="mt-2 text-sm text-muted-foreground">
              {status.docker.reachable ? "Responsive" : "Unavailable"}
            </div>
          </div>

          <div className="rounded-xl border border-border bg-muted/30 p-4">
            <div className="flex items-center gap-2 text-sm font-medium">
              <ShieldCheck className="h-4 w-4" />
              Installer security
            </div>
            <div className="mt-2 text-sm text-muted-foreground">Unlocked session active</div>
          </div>

          <div className="rounded-xl border border-border bg-muted/30 p-4">
            <div className="flex items-center gap-2 text-sm font-medium">
              <ServerCog className="h-4 w-4" />
              Mandatory services
            </div>
            <div className="mt-2 text-sm text-muted-foreground">
              {requiredRunningCount} of {status.requiredServices.length} running
            </div>
          </div>

          <div className="rounded-xl border border-border bg-muted/30 p-4">
            <div className="flex items-center gap-2 text-sm font-medium">
              <CheckCircle2 className="h-4 w-4" />
              Next step
            </div>
            <div className="mt-2 text-sm text-muted-foreground">
              {getRecommendedActionLabel(status.recommendedAction)}
            </div>
          </div>
        </div>

        {!status.docker.reachable && (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>Docker is not responding</AlertTitle>
            <AlertDescription>
              {status.docker.message ??
                "The installer cannot continue until Docker is reachable from the API container."}
            </AlertDescription>
          </Alert>
        )}

        {status.warnings.length > 0 && (
          <div className="space-y-2">
            {status.warnings.map((warning) => (
              <Alert key={warning.code} variant={warning.blocking ? "destructive" : "default"}>
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{warning.title}</AlertTitle>
                <AlertDescription>{warning.message}</AlertDescription>
              </Alert>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}