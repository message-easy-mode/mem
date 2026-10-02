// src/features/operator/services/pages/services-page.tsx

import { useCallback, useEffect, useMemo, useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { MigrationStagingRetirementDialog } from "@/features/operator/migrations/components/migration-staging-retirement-dialog"
import type { StagingRetirementTarget } from "@/features/operator/migrations/api/migration-staging-retirement"

import type { I18nContextValue } from "@/app/i18n/i18n-context"
import { useI18n } from "@/app/i18n/i18n-context"

import {
  getDiagnosticsPortainerOverview,
  getDiagnosticsSeqOverview,
} from "@/features/operator/diagnostics/api/diagnostics.api"

import { ManagedServicesBoard } from "@/features/operator/services/components/managed-services-board"
import { useNpm } from "@/features/operator/services/hooks/use-npm"
import { usePostgres } from "@/features/operator/services/hooks/use-postgres"
import { useSeq } from "@/features/operator/services/hooks/use-seq"
import { useCoturn, useCoturnLatestCheck } from "@/features/operator/services/hooks/use-coturn"
import { useTemporaryStaging } from "@/features/operator/services/hooks/use-temporary-staging"
import { makeTemporaryStagingItem } from "@/features/operator/services/lib/temporary-staging-item"
import { useManagedContainers } from "@/features/operator/services/hooks/use-managed-containers"
import type { CoturnLatestCheckResponse } from "@/features/operator/services/api/coturn.api"
import type { DockerContainerSummary } from "@/features/operator/services/api/runtime.types"
import { formatServicePorts } from "@/features/operator/services/lib/format-service-ports"
import { localizeServiceState } from "@/features/operator/services/lib/service-state"
import type { ManagedServiceListItem } from "../api/runtime-ui.types"
import type { ServicesFilter } from "../components/services-filter-tabs"

type Translate = I18nContextValue["t"]

type StackContainerGroup = {
  stackName: string
  stackSlug: string
  runtimeKind: "managed-stack" | "restore-staging"
  stagingId: string | null
  matrix: DockerContainerSummary | null
  element: DockerContainerSummary | null
  other: DockerContainerSummary[]
}

export function ServicesPage() {
  const { t } = useI18n()
  const postgres = usePostgres()
  const npm = useNpm()
  const seq = useSeq()
  const seqDiagnostics = useQuery({
    queryKey: ["diagnostics", "seq"],
    queryFn: getDiagnosticsSeqOverview,
    staleTime: 10_000,
    retry: 0,
  })
  const portainerDiagnostics = useQuery({
    queryKey: ["diagnostics", "portainer"],
    queryFn: getDiagnosticsPortainerOverview,
    staleTime: 10_000,
    retry: 0,
  })
  const coturn = useCoturn()
  const coturnLatestCheck = useCoturnLatestCheck()
  const containers = useManagedContainers()
  const temporaryStaging = useTemporaryStaging()
  const [retirementTarget, setRetirementTarget] = useState<StagingRetirementTarget | null>(null)
  const [activeFilter, setActiveFilter] = useState<ServicesFilter>("all")

  const refreshInventory = useCallback(async () => {
    await Promise.all([
      postgres.refetch(),
      npm.refetch(),
      seq.refetch(),
      seqDiagnostics.refetch(),
      portainerDiagnostics.refetch(),
      coturn.refetch(),
      coturnLatestCheck.refetch(),
      containers.refetch(),
      temporaryStaging.refetch(),
    ])
  }, [
    postgres.refetch,
    npm.refetch,
    seq.refetch,
    seqDiagnostics.refetch,
    portainerDiagnostics.refetch,
    coturn.refetch,
    coturnLatestCheck.refetch,
    containers.refetch,
    temporaryStaging.refetch,
  ])

  useEffect(() => {
    const refreshOnFocus = () => {
      void refreshInventory()
    }

    window.addEventListener("focus", refreshOnFocus)
    return () => window.removeEventListener("focus", refreshOnFocus)
  }, [refreshInventory])

  const refreshing =
    postgres.isFetching ||
    npm.isFetching ||
    seq.isFetching ||
    seqDiagnostics.isFetching ||
    portainerDiagnostics.isFetching ||
    coturn.isFetching ||
    coturnLatestCheck.isFetching ||
    containers.isFetching ||
    temporaryStaging.isFetching

  const allContainers = containers.data?.containers ?? []

  const portainerContainer = findContainer(allContainers, "portainer")
  const coturnContainer = findContainer(allContainers, "mem-coturn")
  const stagingItems = temporaryStaging.data?.items ?? []
  const stagingContainerIds = new Set(stagingItems.flatMap((item) => item.containerIds))
  const stagingIds = new Set(stagingItems.map((item) => item.stagingId).filter(Boolean))
  // Suppress exact already-projected containers, including renamed resources.
  // Name-only leftovers stay visible but never receive a workspace/action link.
  const stackGroups = findStackGroups(allContainers.filter((container) => !stagingContainerIds.has(container.id)))
    .filter((group) => !group.stagingId || !stagingIds.has(group.stagingId))

  const items: ManagedServiceListItem[] = useMemo(
    () => [
      {
        serviceName: "postgres",
        displayName: "Postgres",
        category: "platform",
        supported: true,
        exists: postgres.data?.exists ?? false,
        running: postgres.data?.running ?? false,
        state: localizeServiceState(t, postgres.data?.state ?? "Not deployed"),
        image: postgres.data?.container?.image ?? null,
        containerName: postgres.data?.container?.name ?? null,
        desiredPorts: [],
        actualPorts: postgres.data?.selectedHostPort
          ? [`5432→${postgres.data.selectedHostPort}`]
          : [],
        hostPaths: postgres.data?.hostDataPath ? [postgres.data.hostDataPath] : [],
        warnings: postgres.data?.warnings ?? [],
        serviceHref: "/services/postgres",
        openUiHref: null,
        urls: [],
        notes: [t("services.item.postgres.note")],
      },
      {
        serviceName: "npm",
        displayName: "Nginx Proxy Manager",
        category: "platform",
        supported: true,
        exists: npm.data?.exists ?? false,
        running: npm.data?.running ?? false,
        state: localizeServiceState(t, npm.data?.state ?? "Not deployed"),
        image: npm.data?.container?.image ?? null,
        containerName: npm.data?.container?.name ?? null,
        desiredPorts: [],
        actualPorts: [
          npm.data?.httpHostPort ? `80→${npm.data.httpHostPort}` : "",
          npm.data?.adminHostPort ? `81→${npm.data.adminHostPort}` : "",
          npm.data?.httpsHostPort ? `443→${npm.data.httpsHostPort}` : "",
        ].filter(Boolean),
        hostPaths: [
          npm.data?.hostDataPath ?? "",
          npm.data?.hostLetsEncryptPath ?? "",
        ].filter(Boolean),
        warnings: npm.data?.warnings ?? [],
        serviceHref: "/services/nginx-proxy-manager",
        openUiHref: npm.data?.running && npm.data?.adminHostPort
          ? `http://localhost:${npm.data.adminHostPort}`
          : null,
        urls: npm.data?.adminHostPort
          ? [
              {
                label: t("services.item.npm.adminUi"),
                href: `http://localhost:${npm.data.adminHostPort}`,
              },
            ]
          : [],
        actions: [
          {
            label: t("services.item.npm.manageDomains"),
            href: "/domains/certificates",
            description: t("services.item.npm.manageDomainsDescription"),
          },
        ],
        notes: [
          t("services.item.npm.note.ingress"),
          t("services.item.npm.note.certificates"),
        ],
      },
      {
        serviceName: "coturn",
        displayName: t("services.item.coturn.name"),
        category: "platform",
        supported: true,
        exists: coturn.data?.containerExists ?? coturnContainer !== null,
        running: coturn.data?.running ?? isContainerRunning(coturnContainer),
        state: localizeCoturnServiceStatus(
          t,
          coturn.data?.operatorStatus ?? (coturnContainer ? "unknown" : "not-deployed"),
          coturnLatestCheck.data,
          coturnLatestCheck.isError,
        ),
        statusLabel: localizeCoturnServiceStatus(
          t,
          coturn.data?.operatorStatus ?? (coturnContainer ? "unknown" : "not-deployed"),
          coturnLatestCheck.data,
          coturnLatestCheck.isError,
        ),
        statusTone: coturnServiceStatusTone(
          coturn.data?.operatorStatus ?? (coturnContainer ? "unknown" : "not-deployed"),
          coturnLatestCheck.data,
          coturnLatestCheck.isError,
        ),
        image: coturn.data?.image ?? coturnContainer?.image ?? null,
        containerName: coturn.data?.containerName ?? coturnContainer?.name ?? null,
        desiredPorts: coturn.data?.requiredProductionFirewallPorts ?? [
          "3478/tcp",
          "3478/udp",
          t("services.item.coturn.productionRelay", { range: "49160-49200" }),
        ],
        actualPorts: coturn.data?.publishedPorts ?? formatServicePorts(coturnContainer?.ports),
        hostPaths: [],
        warnings: coturn.error ? [coturn.error.message] : coturn.data?.warnings ?? [],
        serviceHref: "/services/coturn",
        openUiHref: null,
        urls:
          coturn.data?.turnUris.map((uri) => ({
            label: uri.includes("transport=udp")
              ? t("services.item.coturn.turnUdpUri")
              : t("services.item.coturn.turnTcpUri"),
            href: uri,
          })) ?? [],
        notes: [
          coturn.data?.runtimeExact
            ? t("services.item.coturn.note.runtimeExact")
            : t("services.item.coturn.note.runtimeNotExact"),
          ...(coturn.data?.protectedEvidenceAccess === "restricted"
            ? [t("services.item.coturn.note.protectedEvidenceRestricted")]
            : []),
          functionalCheckNote(
            t,
            coturnLatestCheck.data,
            coturnLatestCheck.isError,
          ),
          t("services.item.coturn.note.shared"),
          t("services.item.coturn.note.routing"),
          coturn.data
            ? t("services.item.coturn.publicHost", { host: coturn.data.publicHost })
            : t("services.item.coturn.publicHostFallback"),
          coturn.data
            ? t("services.item.coturn.realm", { realm: coturn.data.realm })
            : t("services.item.coturn.realmFallback"),
          coturn.data?.relayPortsPublished
            ? t("services.item.coturn.relayPublished", {
                min: coturn.data.relayMinPort,
                max: coturn.data.relayMaxPort,
              })
            : t("services.item.coturn.relayDefault"),
          coturn.data?.domainDriftDetected
            ? t("services.item.coturn.drift")
            : t("services.item.coturn.noDrift"),
        ],
        serviceKind: "platform",
      },
      {
        serviceName: "seq",
        displayName: "Seq",
        category: "support",
        supported: true,
        exists: seq.data?.exists ?? false,
        running: seq.data?.running ?? false,
        state: localizeServiceState(t, seq.data?.state ?? "Not deployed"),
        image: seq.data?.container?.image ?? null,
        containerName: seq.data?.container?.name ?? null,
        desiredPorts: [],
        actualPorts: seq.data?.uiHostPort ? [`80→${seq.data.uiHostPort}`] : [],
        hostPaths: seq.data?.hostDataPath ? [seq.data.hostDataPath] : [],
        warnings: seq.data?.warnings ?? [],
        serviceHref: "/diagnostics/seq",
        openUiHref:
          seq.data?.running &&
          seqDiagnostics.data?.capabilities.canOpenUi &&
          seqDiagnostics.data.ui.available
            ? safeExternalHttpUrl(seqDiagnostics.data.ui.url)
            : null,
        urls: [],
        notes: [t("services.item.seq.note")],
      },
      makeContainerBackedService({
        serviceName: "portainer",
        displayName: "Portainer",
        category: "support",
        container: portainerContainer,
        serviceHref: "/diagnostics/portainer",
        openUiHref:
          isContainerRunning(portainerContainer) &&
          portainerDiagnostics.data?.available &&
          portainerDiagnostics.data.capabilities.canOpenHome
            ? safeExternalHttpUrl(portainerDiagnostics.data.links.home)
            : null,
        urls: [],
        notes: [
          t("services.item.portainer.note.visibility"),
          t("services.item.portainer.note.reuse"),
        ],
        state: localizeServiceState(t, portainerContainer?.state ?? "Not deployed"),
      }),
      ...stackGroups.map((group) => makeStackGroupServiceItem(group, t)),
      ...stagingItems.map((item) => makeTemporaryStagingItem(item, t, temporaryStaging.isError)),
    ],
    [
      postgres.data,
      npm.data,
      seq.data,
      seqDiagnostics.data,
      portainerDiagnostics.data,
      coturn.data,
      coturn.error,
      coturnLatestCheck.data,
      coturnLatestCheck.isError,
      portainerContainer,
      coturnContainer,
      stackGroups,
      stagingItems,
      temporaryStaging.isError,
      t,
    ],
  )

  return (
    <>
    <ManagedServicesBoard
      onReviewRetirement={setRetirementTarget}
      items={items}
      inventoryNotice={temporaryStaging.isError
        ? t("services.staging.inventoryUnavailable")
        : temporaryStaging.data?.status === "partial"
          ? t("services.staging.inventoryPartial")
          : temporaryStaging.isPending ? t("services.staging.inventoryLoading") : undefined}
      activeFilter={activeFilter}
      onFilterChange={setActiveFilter}
      refreshing={refreshing}
      onRefresh={() => void refreshInventory()}
    />
    {retirementTarget && <MigrationStagingRetirementDialog
      key={`${retirementTarget.migrationId}:${retirementTarget.stagingRunId}`}
      target={retirementTarget} onClose={() => setRetirementTarget(null)} />}
    </>
  )
}

function safeExternalHttpUrl(value: string | null | undefined): string | null {
  if (!value) return null

  try {
    const url = new URL(value)
    if (
      (url.protocol !== "http:" && url.protocol !== "https:") ||
      url.username ||
      url.password ||
      url.search ||
      url.hash ||
      url.hostname === "0.0.0.0" ||
      url.hostname === "[::]" ||
      url.hostname === "::"
    ) {
      return null
    }

    return url.toString()
  } catch {
    return null
  }
}

function localizeCoturnOperatorStatus(t: Translate, status: string) {
  switch (status) {
    case "runtime-ready":
      return t("services.coturn.operatorStatus.runtimeReady")
    case "verification-limited":
      return t("services.coturn.operatorStatus.verificationLimited")
    case "needs-attention":
      return t("services.coturn.operatorStatus.needsAttention")
    case "repair-required":
      return t("services.coturn.operatorStatus.repairRequired")
    case "stopped":
      return t("services.coturn.operatorStatus.stopped")
    case "not-deployed":
      return t("services.coturn.operatorStatus.notDeployed")
    case "conflict":
      return t("services.coturn.operatorStatus.conflict")
    default:
      return t("services.coturn.operatorStatus.unknown")
  }
}

function localizeCoturnFunctionalHealthStatus(
  t: Translate,
  latest: CoturnLatestCheckResponse | undefined,
  latestUnavailable: boolean,
) {
  if (latestUnavailable) {
    return t("services.coturn.functionalHealth.unavailable")
  }

  if (latest?.freshness === "runtime-changed") {
    return t("services.coturn.functionalHealth.checkRequired")
  }

  if (latest?.result?.status === "failed") {
    return t("services.coturn.functionalHealth.needsAttention")
  }

  if (latest?.result?.status === "warning") {
    return t("services.coturn.functionalHealth.warning")
  }

  if (latest?.freshness === "stale") {
    return t("services.coturn.functionalHealth.checkOverdue")
  }

  if (latest?.freshness === "fresh" && latest.result?.status === "passed") {
    return t("services.coturn.functionalHealth.healthy")
  }

  if (latest?.freshness === "unavailable") {
    return t("services.coturn.functionalHealth.unavailable")
  }

  return t("services.coturn.functionalHealth.checkRecommended")
}

function localizeCoturnServiceStatus(
  t: Translate,
  runtimeStatus: string,
  latest: CoturnLatestCheckResponse | undefined,
  latestUnavailable: boolean,
) {
  if (runtimeStatus !== "runtime-ready") {
    return localizeCoturnOperatorStatus(t, runtimeStatus)
  }

  if (latestUnavailable) {
    return localizeCoturnFunctionalHealthStatus(t, latest, latestUnavailable)
  }

  if (latest?.freshness === "fresh" && latest.result?.status === "passed") {
    return t("services.status.running")
  }

  return localizeCoturnFunctionalHealthStatus(t, latest, latestUnavailable)
}

function coturnServiceStatusTone(
  runtimeStatus: string,
  latest: CoturnLatestCheckResponse | undefined,
  latestUnavailable: boolean,
): ManagedServiceListItem["statusTone"] {
  if (runtimeStatus !== "runtime-ready") {
    return coturnStatusTone(runtimeStatus)
  }

  if (latestUnavailable) {
    return "neutral"
  }

  if (latest?.freshness === "runtime-changed") {
    return "warning"
  }

  if (latest?.result?.status === "failed") {
    return "danger"
  }

  if (
    latest?.result?.status === "warning" ||
    latest?.freshness === "stale"
  ) {
    return "warning"
  }

  if (latest?.freshness === "fresh" && latest.result?.status === "passed") {
    return "positive"
  }

  return "neutral"
}

function functionalCheckNote(
  t: Translate,
  latest: CoturnLatestCheckResponse | undefined,
  latestUnavailable: boolean,
) {
  if (latestUnavailable) {
    return t("services.item.coturn.note.functionalUnavailable")
  }

  if (!latest?.result) {
    return latest?.freshness === "unavailable"
      ? t("services.item.coturn.note.functionalUnavailable")
      : t("services.item.coturn.note.functionalNotChecked")
  }

  if (latest.freshness === "runtime-changed") {
    return t("services.item.coturn.note.functionalRuntimeChanged")
  }

  if (latest.result.status === "failed") {
    return t("services.item.coturn.note.functionalFailed")
  }

  if (latest.result.status === "warning") {
    return t("services.item.coturn.note.functionalWarning")
  }

  if (latest.freshness === "stale") {
    return t("services.item.coturn.note.functionalStale")
  }

  return t("services.item.coturn.note.functionalFresh")
}

function coturnStatusTone(status: string): ManagedServiceListItem["statusTone"] {
  switch (status) {
    case "runtime-ready":
      return "positive"
    case "verification-limited":
      return "neutral"
    case "needs-attention":
    case "repair-required":
    case "stopped":
      return "warning"
    case "conflict":
      return "danger"
    default:
      return "neutral"
  }
}

function makeContainerBackedService({
  serviceName,
  displayName,
  category,
  container,
  serviceHref,
  openUiHref,
  urls,
  notes,
  state,
}: {
  serviceName: ManagedServiceListItem["serviceName"]
  displayName: string
  category: ManagedServiceListItem["category"]
  container: DockerContainerSummary | null
  serviceHref: string
  openUiHref: string | null
  urls: ManagedServiceListItem["urls"]
  notes: string[]
  state: string
}): ManagedServiceListItem {
  return {
    serviceName,
    displayName,
    category,
    supported: true,
    exists: container !== null,
    running: isContainerRunning(container),
    state,
    image: container?.image ?? null,
    containerName: container?.name ?? null,
    desiredPorts: [],
    actualPorts: formatServicePorts(container?.ports),
    hostPaths: [],
    warnings: [],
    serviceHref,
    openUiHref,
    urls,
    notes,
  }
}

function findStackGroups(containers: DockerContainerSummary[]) {
  const groups = new Map<string, StackContainerGroup>()

  for (const container of containers) {
    const name = normalizeName(container.name).toLowerCase()

    if (
      [
        "mem-api",
        "mem-web",
        "mem-postgres",
        "mem-npm",
        "mem-seq",
        "mem-coturn",
        "portainer",
      ].includes(name)
    ) {
      continue
    }

    const restoreStaging = inferRestoreStaging(container)
    const serviceKind = restoreStaging?.serviceKind ?? inferStackServiceKind(container)

    if (!restoreStaging && serviceKind !== "matrix" && serviceKind !== "element") {
      continue
    }

    const stackName = restoreStaging?.stagingId ?? inferStackName(container)

    if (!stackName) {
      continue
    }

    const stackSlug = restoreStaging
      ? `restore-staging-${toSlug(restoreStaging.stagingId)}`
      : toSlug(stackName)
    const groupKey = restoreStaging ? `restore-staging:${restoreStaging.stagingId}` : stackSlug

    const existing =
      groups.get(groupKey) ??
      {
        stackName,
        stackSlug,
        runtimeKind: restoreStaging ? "restore-staging" : "managed-stack",
        stagingId: restoreStaging?.stagingId ?? null,
        matrix: null,
        element: null,
        other: [],
      }

    if (serviceKind === "matrix") {
      existing.matrix = container
    } else if (serviceKind === "element") {
      existing.element = container
    } else {
      existing.other.push(container)
    }

    groups.set(groupKey, existing)
  }

  return Array.from(groups.values()).sort((a, b) =>
    a.stackSlug.localeCompare(b.stackSlug),
  )
}

function makeStackGroupServiceItem(
  group: StackContainerGroup,
  t: Translate,
): ManagedServiceListItem {
  const containers = [
    group.matrix,
    group.element,
    ...group.other,
  ].filter(Boolean) as DockerContainerSummary[]

  const runningCount = containers.filter(isContainerRunning).length
  const totalCount = containers.length

  const matrixPorts = formatServicePorts(group.matrix?.ports)
  const elementPorts = formatServicePorts(group.element?.ports)

  const actualPorts = [
    ...matrixPorts.map((port) => t("services.item.stack.matrixPort", { port })),
    ...elementPorts.map((port) => t("services.item.stack.elementPort", { port })),
  ]

  const elementUrl = getFirstHttpUrl(group.element)
  const matrixUrl = getFirstHttpUrl(group.matrix)

  const state =
    totalCount === 0
      ? t("services.status.notDeployed")
      : runningCount === totalCount
        ? t("services.status.running")
        : runningCount > 0
          ? t("services.status.partiallyRunning")
          : t("services.status.stopped")

  const isRestoreStaging = group.runtimeKind === "restore-staging"

  return {
    serviceName: isRestoreStaging
      ? `restore-staging-${group.stagingId ?? group.stackSlug}`
      : `stack-${group.stackSlug}`,
    displayName: isRestoreStaging
      ? t("services.item.restoreStaging.name")
      : group.stackName,
    category: "stack",
    supported: true,
    exists: totalCount > 0,
    running: runningCount > 0,
    state,
    image: summarizeStackImages(group),
    containerName: summarizeStackContainers(group),
    desiredPorts: [],
    actualPorts,
    hostPaths: [],
    warnings: buildStackWarnings(group, t),
    serviceHref: isRestoreStaging ? null : `/stacks/${group.stackSlug}`,
    description: isRestoreStaging ? t("services.staging.owner.unresolved") : null,
    inventoryNote: isRestoreStaging ? t("services.staging.unresolvedNote") : undefined,
    openUiHref: null,
    urls: [
      ...(elementUrl
        ? [
            {
              label: t("services.item.stack.elementEndpoint"),
              href: elementUrl,
            },
          ]
        : []),
      ...(matrixUrl
        ? [
            {
              label: t("services.item.stack.matrixEndpoint"),
              href: matrixUrl,
            },
          ]
        : []),
    ],
    notes: [
      group.matrix
        ? t("services.item.stack.matrixContainer", {
            container: normalizeName(group.matrix.name),
          })
        : t("services.item.stack.matrixMissing"),
      group.element
        ? t("services.item.stack.elementContainer", {
            container: normalizeName(group.element.name),
          })
        : t("services.item.stack.elementMissing"),
      t("services.item.stack.relationship"),
      t("services.item.stack.deeper"),
    ],
    stackName: isRestoreStaging ? null : group.stackName,
    stackSlug: isRestoreStaging ? null : group.stackSlug,
    serviceKind: "unknown",
  }
}

function buildStackWarnings(group: StackContainerGroup, t: Translate) {
  const warnings: string[] = []

  if (!group.matrix) {
    warnings.push(t("services.item.stack.warning.matrixMissing"))
  }

  if (!group.element) {
    warnings.push(t("services.item.stack.warning.elementMissing"))
  }

  if (group.matrix && !isContainerRunning(group.matrix)) {
    warnings.push(t("services.item.stack.warning.matrixStopped"))
  }

  if (group.element && !isContainerRunning(group.element)) {
    warnings.push(t("services.item.stack.warning.elementStopped"))
  }

  return warnings
}

function summarizeStackImages(group: StackContainerGroup) {
  const parts = [
    group.matrix ? `Matrix: ${group.matrix.image}` : null,
    group.element ? `Element: ${group.element.image}` : null,
  ].filter(Boolean)

  return parts.length > 0 ? parts.join(" · ") : null
}

function summarizeStackContainers(group: StackContainerGroup) {
  const parts = [
    group.matrix ? normalizeName(group.matrix.name) : null,
    group.element ? normalizeName(group.element.name) : null,
  ].filter(Boolean)

  return parts.length > 0 ? parts.join(" · ") : null
}

function inferRestoreStaging(container: DockerContainerSummary): {
  stagingId: string
  serviceKind: "matrix" | "element" | "postgres"
} | null {
  const name = normalizeName(container.name)
  const match = /^mem-restore-staging-(synapse|matrix|element-web|element|postgres)-(.+)$/i.exec(name)

  if (!match?.[2]?.trim()) {
    return null
  }

  const service = match[1].toLowerCase()

  return {
    stagingId: match[2],
    serviceKind: service === "postgres" ? "postgres" : service === "synapse" || service === "matrix" ? "matrix" : "element",
  }
}

function inferStackServiceKind(
  container: DockerContainerSummary,
): ManagedServiceListItem["serviceKind"] {
  const name = normalizeName(container.name).toLowerCase()
  const image = container.image.toLowerCase()

  if (
    name.includes("element") ||
    image.includes("element-web") ||
    image.includes("vectorim/element")
  ) {
    return "element"
  }

  if (
    name.includes("synapse") ||
    image.includes("synapse") ||
    name.includes("matrix")
  ) {
    return "matrix"
  }

  return "unknown"
}

function inferStackName(container: DockerContainerSummary) {
  const name = normalizeName(container.name)

  const cleaned = name
    .replace(/^stack[-_]mem[-_]/i, "")
    .replace(/^mem[-_]/i, "")
    .replace(/^matrix[-_]/i, "")
    .replace(/^element[-_]/i, "")
    .replace(/^synapse[-_]/i, "")
    .replace(/^element-web[-_]/i, "")
    .replace(/[-_](synapse|matrix|element|element-web)$/i, "")
    .replace(/^(synapse|matrix|element|element-web)[-_]/i, "")

  return cleaned && cleaned !== name ? cleaned : null
}

function getFirstHttpUrl(container: DockerContainerSummary | null) {
  if (!container) {
    return null
  }

  const port = container.ports.find((x) => x.publicPort > 0)
  if (!port) {
    return null
  }

  return `http://localhost:${port.publicPort}`
}

function isContainerRunning(container: DockerContainerSummary | null) {
  if (!container) {
    return false
  }

  return container.state.toLowerCase() === "running"
}

function findContainer(
  containers: DockerContainerSummary[],
  name: string,
): DockerContainerSummary | null {
  return (
    containers.find((container) => normalizeName(container.name) === name) ?? null
  )
}

function normalizeName(name: string) {
  return name.replace(/^\/+/, "")
}

function toSlug(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, "-")
    .replace(/-{2,}/g, "-")
    .replace(/^-+|-+$/g, "")
}
