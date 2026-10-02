import { useMemo, useState } from "react"
import { useMutation, useQueryClient } from "@tanstack/react-query"
import { CheckCircle2, ChevronDown, Clock3, RotateCcw } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { ApiProblemAlert } from "@/components/operator/api-problem-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select } from "@/components/ui/select"
import {
  acknowledgeDiagnosticIncident,
  reopenDiagnosticIncident,
  resolveDiagnosticIncident,
  snoozeDiagnosticIncident,
} from "../api/diagnostics.api"
import type {
  DiagnosticsIncidentDetailResponse,
  DiagnosticsIncidentLifecycle,
  DiagnosticsIncidentLifecycleFilter,
} from "../api/diagnostics.types"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "../diagnostics-time"

type LifecycleMutation =
  | Readonly<{ kind: "acknowledge" }>
  | Readonly<{ kind: "snooze"; snoozedUntilUtc: string }>
  | Readonly<{ kind: "resolve"; resolutionCode: string }>
  | Readonly<{ kind: "reopen" }>

export const diagnosticsIncidentResolutionCodes = [
  "fixed",
  "self_recovered",
  "no_longer_relevant",
  "accepted_risk",
  "duplicate",
  "false_positive",
] as const

export function DiagnosticIncidentLifecycleBadge({
  lifecycle,
}: {
  lifecycle: DiagnosticsIncidentLifecycle | null | undefined
}) {
  const { t } = useI18n()
  const state = lifecycle?.state ?? "open"
  const reopened = state === "open" && lifecycle?.reopened === true

  return (
    <Badge
      variant={state === "open" ? "destructive" : state === "resolved" ? "outline" : "secondary"}
      data-incident-lifecycle={state}
    >
      {reopened
        ? t("diagnostics.lifecycle.reopened")
        : t(`diagnostics.lifecycle.state.${normalizeLifecycleState(state)}`)}
    </Badge>
  )
}

export function DiagnosticIncidentLifecyclePanel({
  detail,
}: {
  detail: DiagnosticsIncidentDetailResponse
}) {
  const { language, t } = useI18n()
  const queryClient = useQueryClient()
  const incident = detail.incident
  const lifecycle = incident.lifecycle ?? openLifecycle()
  const state = normalizeLifecycleState(lifecycle.state)
  const canManage = detail.capabilities.canManageIncidentLifecycle
  const [resolveOpen, setResolveOpen] = useState(false)
  const [resolutionCode, setResolutionCode] = useState("")
  const [customSnoozeOpen, setCustomSnoozeOpen] = useState(false)
  const [customSnooze, setCustomSnooze] = useState("")

  const mutation = useMutation({
    mutationFn: async (request: LifecycleMutation) => {
      switch (request.kind) {
        case "acknowledge":
          return acknowledgeDiagnosticIncident(incident.incidentId)
        case "snooze":
          return snoozeDiagnosticIncident(incident.incidentId, request.snoozedUntilUtc)
        case "resolve":
          return resolveDiagnosticIncident(incident.incidentId, request.resolutionCode)
        case "reopen":
          return reopenDiagnosticIncident(incident.incidentId)
      }
    },
    onSuccess: async (updated) => {
      queryClient.setQueryData(
        ["diagnostics", "incident", incident.incidentId],
        updated,
      )
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "incidents"] }),
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "overview"] }),
        queryClient.invalidateQueries({ queryKey: ["diagnostics", "attention"] }),
      ])
      setResolveOpen(false)
      setResolutionCode("")
      setCustomSnoozeOpen(false)
      setCustomSnooze("")
    },
  })

  const customSnoozeUntil = useMemo(() => {
    if (!customSnooze) return null
    const value = new Date(customSnooze)
    if (Number.isNaN(value.getTime())) return null
    const now = Date.now()
    const latest = now + 30 * 24 * 60 * 60 * 1000
    return value.getTime() > now && value.getTime() <= latest ? value.toISOString() : null
  }, [customSnooze])

  const run = (request: LifecycleMutation) => {
    mutation.reset()
    mutation.mutate(request)
  }

  const openCustomSnooze = () => {
    const initial = new Date(Date.now() + 24 * 60 * 60 * 1000)
    setCustomSnooze(toLocalDateTimeInput(initial))
    setCustomSnoozeOpen(true)
  }

  return (
    <section className="rounded-lg border bg-muted/15 p-4" aria-labelledby="diagnostics-incident-lifecycle-title">
      <div className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <h3 id="diagnostics-incident-lifecycle-title" className="font-medium">
              {t("diagnostics.lifecycle.title")}
            </h3>
            <DiagnosticIncidentLifecycleBadge lifecycle={lifecycle} />
          </div>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {lifecycleDescription(lifecycle, t)}
          </p>
        </div>

        {canManage ? (
          <div className="flex flex-wrap gap-2">
            {state === "open" ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={mutation.isPending}
                onClick={() => run({ kind: "acknowledge" })}
              >
                <CheckCircle2 className="mr-1 h-4 w-4" />
                {t("diagnostics.lifecycle.acknowledge")}
              </Button>
            ) : null}

            {state === "open" || state === "acknowledged" ? (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button type="button" variant="outline" size="sm" disabled={mutation.isPending}>
                    <Clock3 className="mr-1 h-4 w-4" />
                    {t("diagnostics.lifecycle.snooze")}
                    <ChevronDown className="ml-1 h-3.5 w-3.5" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuLabel>{t("diagnostics.lifecycle.snoozeFor")}</DropdownMenuLabel>
                  {([
                    [1, "diagnostics.lifecycle.snooze.1h"],
                    [4, "diagnostics.lifecycle.snooze.4h"],
                    [24, "diagnostics.lifecycle.snooze.24h"],
                  ] as const).map(([hours, labelKey]) => (
                    <DropdownMenuItem
                      key={hours}
                      onSelect={() => run({
                        kind: "snooze",
                        snoozedUntilUtc: new Date(Date.now() + hours * 60 * 60 * 1000).toISOString(),
                      })}
                    >
                      {t(labelKey)}
                    </DropdownMenuItem>
                  ))}
                  <DropdownMenuItem
                    onSelect={() => run({
                      kind: "snooze",
                      snoozedUntilUtc: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
                    })}
                  >
                    {t("diagnostics.lifecycle.snooze.7d")}
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem onSelect={openCustomSnooze}>
                    {t("diagnostics.lifecycle.snooze.custom")}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            ) : null}

            {state !== "resolved" ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={mutation.isPending}
                onClick={() => {
                  mutation.reset()
                  setResolveOpen(true)
                }}
              >
                {t("diagnostics.lifecycle.resolve")}
              </Button>
            ) : null}

            {state !== "open" ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={mutation.isPending}
                onClick={() => run({ kind: "reopen" })}
              >
                <RotateCcw className="mr-1 h-4 w-4" />
                {t("diagnostics.lifecycle.reopen")}
              </Button>
            ) : null}
          </div>
        ) : (
          <Badge variant="outline">{t("diagnostics.lifecycle.readOnly")}</Badge>
        )}
      </div>

      {lifecycle.updatedAtUtc || lifecycle.snoozedUntilUtc || lifecycle.resolutionCode ? (
        <dl className="mt-4 grid gap-3 border-t pt-3 text-sm sm:grid-cols-2 xl:grid-cols-4">
          {lifecycle.updatedAtUtc ? (
            <LifecycleFact
              label={t("diagnostics.lifecycle.actedAt")}
              value={formatDiagnosticsLocalDateTime(lifecycle.updatedAtUtc, language, true)}
              title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(lifecycle.updatedAtUtc)}`}
            />
          ) : null}
          {lifecycle.updatedByOperatorId ? (
            <LifecycleFact
              label={t("diagnostics.lifecycle.actedBy")}
              value={t("diagnostics.lifecycle.authenticatedOperator")}
              title={lifecycle.updatedByOperatorId}
            />
          ) : null}
          {lifecycle.snoozedUntilUtc && state === "snoozed" ? (
            <LifecycleFact
              label={t("diagnostics.lifecycle.snoozedUntil")}
              value={formatDiagnosticsLocalDateTime(lifecycle.snoozedUntilUtc, language, true)}
              title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(lifecycle.snoozedUntilUtc)}`}
            />
          ) : null}
          {lifecycle.resolutionCode ? (
            <LifecycleFact
              label={t("diagnostics.lifecycle.resolutionReason")}
              value={diagnosticsIncidentResolutionLabel(lifecycle.resolutionCode, t)}
            />
          ) : null}
        </dl>
      ) : null}

      {mutation.error ? (
        <div className="mt-4">
          <ApiProblemAlert
            error={mutation.error}
            title={t("diagnostics.lifecycle.actionErrorTitle")}
            fallbackDescription={t("diagnostics.lifecycle.actionErrorDescription")}
            showDiagnosticsLink={false}
          />
        </div>
      ) : null}

      <ConfirmationDialog
        open={resolveOpen}
        onOpenChange={setResolveOpen}
        title={t("diagnostics.lifecycle.resolveTitle")}
        description={t("diagnostics.lifecycle.resolveDescription")}
        confirmLabel={t("diagnostics.lifecycle.resolve")}
        confirmingLabel={t("diagnostics.lifecycle.working")}
        cancelLabel={t("common.cancel")}
        onConfirm={() => {
          if (resolutionCode) run({ kind: "resolve", resolutionCode })
        }}
        isConfirming={mutation.isPending}
        confirmDisabled={!resolutionCode}
      >
        <div className="space-y-2">
          <Label htmlFor="diagnostics-resolution-code">
            {t("diagnostics.lifecycle.resolutionReason")}
          </Label>
          <Select
            id="diagnostics-resolution-code"
            value={resolutionCode}
            onChange={(event) => setResolutionCode(event.target.value)}
          >
            <option value="">{t("diagnostics.lifecycle.selectReason")}</option>
            {diagnosticsIncidentResolutionCodes.map((code) => (
              <option key={code} value={code}>{diagnosticsIncidentResolutionLabel(code, t)}</option>
            ))}
          </Select>
        </div>
      </ConfirmationDialog>

      <ConfirmationDialog
        open={customSnoozeOpen}
        onOpenChange={setCustomSnoozeOpen}
        title={t("diagnostics.lifecycle.customSnoozeTitle")}
        description={t("diagnostics.lifecycle.customSnoozeDescription")}
        confirmLabel={t("diagnostics.lifecycle.snooze")}
        confirmingLabel={t("diagnostics.lifecycle.working")}
        cancelLabel={t("common.cancel")}
        onConfirm={() => {
          if (customSnoozeUntil) run({ kind: "snooze", snoozedUntilUtc: customSnoozeUntil })
        }}
        isConfirming={mutation.isPending}
        confirmDisabled={!customSnoozeUntil}
      >
        <div className="space-y-2">
          <Label htmlFor="diagnostics-snooze-until">
            {t("diagnostics.lifecycle.snoozedUntil")}
          </Label>
          <Input
            id="diagnostics-snooze-until"
            type="datetime-local"
            value={customSnooze}
            onChange={(event) => setCustomSnooze(event.target.value)}
          />
          <p className="text-xs text-muted-foreground">
            {t("diagnostics.lifecycle.customSnoozeLimit")}
          </p>
        </div>
      </ConfirmationDialog>
    </section>
  )
}

export const diagnosticsIncidentLifecycleFilters: readonly DiagnosticsIncidentLifecycleFilter[] = [
  "open",
  "acknowledged",
  "snoozed",
  "resolved",
  "all",
]

export function lifecycleFilterLabel(
  filter: DiagnosticsIncidentLifecycleFilter,
  t: ReturnType<typeof useI18n>["t"],
): string {
  return t(`diagnostics.lifecycle.filter.${filter}`)
}

function LifecycleFact({
  label,
  value,
  title,
}: {
  label: string
  value: string
  title?: string
}) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-medium" title={title}>{value}</dd>
    </div>
  )
}

function normalizeLifecycleState(value: string): "open" | "acknowledged" | "snoozed" | "resolved" {
  switch (value) {
    case "acknowledged":
    case "snoozed":
    case "resolved":
      return value
    default:
      return "open"
  }
}

function openLifecycle(): DiagnosticsIncidentLifecycle {
  return {
    state: "open",
    reopened: false,
    storedDisposition: null,
    updatedAtUtc: null,
    updatedByOperatorId: null,
    observedThroughAtUtc: null,
    observedThroughEventId: null,
    snoozedUntilUtc: null,
    resolutionCode: null,
    revision: null,
  }
}

function lifecycleDescription(
  lifecycle: DiagnosticsIncidentLifecycle,
  t: ReturnType<typeof useI18n>["t"],
): string {
  if (lifecycle.state === "open" && lifecycle.reopened) {
    return t("diagnostics.lifecycle.description.reopened")
  }
  return t(`diagnostics.lifecycle.description.${normalizeLifecycleState(lifecycle.state)}`)
}

export function diagnosticsIncidentResolutionLabel(code: string, t: ReturnType<typeof useI18n>["t"]): string {
  switch (code) {
    case "fixed": return t("diagnostics.lifecycle.reason.fixed")
    case "self_recovered": return t("diagnostics.lifecycle.reason.selfRecovered")
    case "no_longer_relevant": return t("diagnostics.lifecycle.reason.noLongerRelevant")
    case "accepted_risk": return t("diagnostics.lifecycle.reason.acceptedRisk")
    case "duplicate": return t("diagnostics.lifecycle.reason.duplicate")
    case "false_positive": return t("diagnostics.lifecycle.reason.falsePositive")
    default: return code
  }
}

function toLocalDateTimeInput(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, "0")
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}
