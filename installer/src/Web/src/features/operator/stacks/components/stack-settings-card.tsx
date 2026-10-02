import { FileKey2, LockKeyhole, Settings2 } from "lucide-react"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type {
  RuntimeStackInspectResponse,
  RuntimeStackServiceInspectResponse,
} from "../api/stacks.types"

type StackSettingsCardProps = {
  stack: RuntimeStackInspectResponse
}

/**
 * Read-only runtime-registration facts for a stack.
 *
 * The current inspection contract does not expose a safe settings mutation
 * model, runtime image tags, secret values, or environment variables. This
 * component deliberately presents only the identifiers and configuration
 * references that the runtime manifest already reports.
 */
export function StackSettingsCard({ stack }: StackSettingsCardProps) {
  return (
    <div className="space-y-6">
      <Card>
        <CardHeader>
          <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
            <div>
              <CardTitle className="flex items-center gap-2">
                <Settings2 className="h-5 w-5" />
                Stack settings
              </CardTitle>
              <p className="mt-1 text-sm text-muted-foreground">
                Runtime registration and configuration references already recorded in this stack&apos;s manifest.
              </p>
            </div>
            <div className="inline-flex w-fit items-center gap-2 rounded-full border border-sky-500/20 bg-sky-500/10 px-3 py-1 text-xs text-sky-100">
              <LockKeyhole className="h-3.5 w-3.5" />
              Read-only record
            </div>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
            <Info label="Stack ID" value={stack.stackId ?? "not reported"} mono />
            <Info label="Stack slug" value={stack.slug} />
            <Info label="Inspection source" value={stack.source || "not reported"} />
            <Info label="Manifest status" value={formatStatus(stack.status)} />
            <Info label="Last verified" value={formatDate(stack.lastVerifiedAtUtc)} />
          </div>

          <div className="rounded-xl border border-sky-500/20 bg-sky-500/10 p-4 text-sm text-sky-100">
            This is not an editable form. The current stack inspection API exposes no safe settings-update command, so
            MEM does not offer Save, Edit, or Reset controls here.
          </div>
        </CardContent>
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <RuntimeConfigurationCard title="Matrix runtime record" service={stack.matrix} />
        <RuntimeConfigurationCard title="Element runtime record" service={stack.element} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FileKey2 className="h-5 w-5" />
            Inspection boundaries
          </CardTitle>
          <p className="mt-1 text-sm text-muted-foreground">
            What this read-only settings record deliberately does not infer or reveal.
          </p>
        </CardHeader>
        <CardContent className="grid gap-3 text-sm md:grid-cols-2">
          <BoundaryItem>
            Container image names and tags are not exposed by the current runtime inspection response.
          </BoundaryItem>
          <BoundaryItem>
            Secret values, passwords, and environment variables are never shown here.
          </BoundaryItem>
          <BoundaryItem>
            DNS-provider records, certificate expiry, and live NPM configuration remain in their dedicated workflows.
          </BoundaryItem>
          <BoundaryItem>
            Runtime health and public-route checks belong in Diagnostics, not in this configuration record.
          </BoundaryItem>
        </CardContent>
      </Card>
    </div>
  )
}

function RuntimeConfigurationCard({
  title,
  service,
}: {
  title: string
  service: RuntimeStackServiceInspectResponse | null
}) {
  if (!service) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{title}</CardTitle>
        </CardHeader>
        <CardContent className="text-sm text-muted-foreground">
          This service is not present in the runtime manifest, so MEM cannot describe its registered configuration
          references.
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        <p className="mt-1 text-sm text-muted-foreground">
          Stable identifiers and file references reported for {service.serviceKey}.
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 text-sm md:grid-cols-2">
          <Info label="Service key" value={service.serviceKey} />
          <Info label="Instance ID" value={service.instanceId} mono />
          <Info label="Container ID" value={service.containerId ?? "not reported"} mono />
          <Info label="Container name" value={service.containerName ?? "not reported"} mono />
        </div>

        <div className="grid gap-3 text-sm md:grid-cols-2">
          <Info label="Configuration path" value={service.configPath ?? "not reported"} mono />
          <Info label="Data path" value={service.dataPath ?? "not reported"} mono />
        </div>

        <div className="rounded-xl border border-border bg-background/40 p-4 text-sm text-muted-foreground">
          Image and version values are not exposed by the current runtime inspection response. MEM does not guess them.
        </div>
      </CardContent>
    </Card>
  )
}

function BoundaryItem({ children }: { children: string }) {
  return (
    <div className="rounded-xl border border-border bg-background/40 p-4 text-muted-foreground">{children}</div>
  )
}

function Info({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={`mt-1 break-words text-foreground ${mono ? "font-mono text-xs" : ""}`}>
        {value}
      </div>
    </div>
  )
}

function formatDate(value: string | null) {
  if (!value) return "not recorded"

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value))
}

function formatStatus(value: string | null | undefined) {
  if (!value) return "not reported"

  return value.replace(/[_-]+/g, " ")
}
