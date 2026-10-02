import { useIsFetching, useQueries, useQueryClient } from "@tanstack/react-query"
import { Link } from "react-router-dom"
import { ArrowRight, HardDrive, RefreshCw } from "lucide-react"

import { formatBytes, formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import type { UiLanguage } from "@/app/i18n/messages"
import { useDashboardOverview } from "@/features/operator/dashboard/hooks/use-dashboard-overview"
import type { DashboardDiskUsage } from "@/features/operator/dashboard/api/dashboard.types"
import { inspectRuntimeStackStorage } from "@/features/operator/stacks/api/stacks.api"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import type {
  RuntimeStackMatrixStorageResponse,
  RuntimeStackStorageResponse,
  RuntimeStackSummaryResponse,
} from "@/features/operator/stacks/api/stacks.types"
import {
  runtimeStackKeys,
  useRuntimeStacks,
} from "@/features/operator/stacks/hooks/use-runtime-stacks"

const storageQueryRoot = [...runtimeStackKeys.all, "storage"] as const

export function StoragePage() {
  const { language, t } = useI18n()
  const queryClient = useQueryClient()
  const stacksQuery = useRuntimeStacks()
  const dashboardQuery = useDashboardOverview()
  const storageFetchCount = useIsFetching({ queryKey: storageQueryRoot })
  const stacks = stacksQuery.data?.stacks ?? []
  const storageQueries = useQueries({
    queries: stacks.map((stack) => ({
      queryKey: runtimeStackKeys.storage(stack.slug),
      queryFn: () => inspectRuntimeStackStorage(stack.slug),
      refetchInterval: 15000,
    })),
  })
  const storageItems = stacks.map((stack, index) => ({
    stack,
    storage: storageQueries[index]?.data ?? null,
    isLoading: storageQueries[index]?.isLoading ?? true,
    hasError: Boolean(storageQueries[index]?.error),
  }))
  const isRefreshing = stacksQuery.isFetching || dashboardQuery.isFetching || storageFetchCount > 0

  async function refreshStorageInventory() {
    await Promise.all([
      stacksQuery.refetch(),
      dashboardQuery.refetch(),
      queryClient.invalidateQueries({ queryKey: storageQueryRoot }),
    ])
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">{t("operatorStorage.title")}</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("operatorStorage.description")}
          </p>
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={() => void refreshStorageInventory()}
          disabled={isRefreshing}
        >
          <RefreshCw className="mr-2 h-4 w-4" />
          {isRefreshing ? t("operatorStorage.refreshing") : t("operatorStorage.refresh")}
        </Button>
      </div>

      {stacksQuery.error && (
        <Alert variant="destructive">
          <AlertTitle>{t("operatorStorage.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{stacksQuery.error.message}</AlertDescription>
        </Alert>
      )}

      <StorageOverview
        items={storageItems}
        isFleetLoading={stacksQuery.isLoading}
        hasFleetError={Boolean(stacksQuery.error)}
        filesystemDisk={dashboardQuery.data?.host.disk ?? null}
        isFilesystemLoading={dashboardQuery.isLoading}
        filesystemUnavailable={
          Boolean(dashboardQuery.error) ||
          (dashboardQuery.data !== undefined &&
            (dashboardQuery.data.host.state !== "available" || dashboardQuery.data.host.disk === null))
        }
      />

      <Card>
        <CardHeader>
          <CardTitle>{t("operatorStorage.inventory.title")}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("operatorStorage.inventory.description")}
          </p>
        </CardHeader>
        <CardContent className="px-0 pb-0">
          {stacksQuery.isLoading ? (
            <div className="px-6 pb-6 text-sm text-muted-foreground">
              {t("operatorStorage.inventory.loading")}
            </div>
          ) : stacks.length === 0 ? (
            <div className="px-6 pb-6 text-sm text-muted-foreground">
              {t("operatorStorage.inventory.empty")}
            </div>
          ) : (
            <Table className="min-w-[1040px]" data-testid="storage-inventory-table">
              <TableHeader>
                <TableRow>
                  <TableHead>{t("operatorStorage.inventory.chatServer")}</TableHead>
                  <TableHead>{t("operatorStorage.inventory.matrixMedia")}</TableHead>
                  <TableHead>{t("operatorStorage.inventory.localUploads")}</TableHead>
                  <TableHead>{t("operatorStorage.inventory.remoteCache")}</TableHead>
                  <TableHead>{t("operatorStorage.inventory.mediaStore")}</TableHead>
                  <TableHead className="sticky right-0 z-30 min-w-40 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]">
                    {t("operatorStorage.inventory.actions")}
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {storageItems.map((item) => (
                  <StorageInventoryRow key={item.stack.stackId} item={item} language={language} />
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

type StorageInventoryItem = {
  stack: RuntimeStackSummaryResponse
  storage: RuntimeStackStorageResponse | null
  isLoading: boolean
  hasError: boolean
}

type StorageTotals = {
  matrixBytes: number
  matrixFiles: number
  localBytes: number
  localFiles: number
  remoteBytes: number
  remoteFiles: number
  thumbnailBytes: number
  thumbnailFiles: number
  urlCacheBytes: number
  urlCacheFiles: number
  mediaStoresAvailable: number
}

function StorageOverview({
  items,
  isFleetLoading,
  hasFleetError,
  filesystemDisk,
  isFilesystemLoading,
  filesystemUnavailable,
}: {
  items: StorageInventoryItem[]
  isFleetLoading: boolean
  hasFleetError: boolean
  filesystemDisk: DashboardDiskUsage | null
  isFilesystemLoading: boolean
  filesystemUnavailable: boolean
}) {
  const { language, t } = useI18n()
  const hasPendingInspection = isFleetLoading || items.some((item) => item.isLoading)
  const hasFailedInspection = hasFleetError || items.some((item) => item.hasError)
  const totals = summarizeStorage(items)
  const filesystemCapacity = getFilesystemCapacity(filesystemDisk)

  const aggregateValue = (value: string) => {
    if (hasPendingInspection) return t("operatorStorage.inventory.loadingShort")
    if (hasFailedInspection) return t("operatorStorage.overview.unavailable")
    return value
  }
  const chatServerCount = isFleetLoading
    ? t("operatorStorage.inventory.loadingShort")
    : hasFleetError
      ? t("operatorStorage.overview.unavailable")
      : formatNumber(items.length, language)

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <HardDrive className="h-5 w-5" />
          {t("operatorStorage.overview.title")}
        </CardTitle>
        <p className="text-sm text-muted-foreground">
          {t("operatorStorage.overview.description")}
        </p>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
          <Metric
            label={t("operatorStorage.overview.managedMatrixMedia")}
            value={aggregateValue(formatBytes(totals.matrixBytes, language))}
          />
          <Metric
            label={t("operatorStorage.overview.mediaFiles")}
            value={aggregateValue(formatNumber(totals.matrixFiles, language))}
          />
          <FilesystemCapacityMetric
            disk={filesystemCapacity}
            isLoading={isFilesystemLoading}
            isUnavailable={filesystemUnavailable}
            language={language}
          />
          <Metric
            label={t("operatorStorage.overview.chatServers")}
            value={chatServerCount}
          />
        </div>

        <div className="grid gap-3 border-t border-border pt-4 text-sm sm:grid-cols-2 xl:grid-cols-5">
          <Info
            label={t("operatorStorage.overview.localUploads")}
            value={aggregateValue(formatAggregateMedia(totals.localBytes, totals.localFiles, language, t))}
          />
          <Info
            label={t("operatorStorage.overview.mediaStoresAvailable")}
            value={aggregateValue(
              t("operatorStorage.overview.mediaStoresCount", {
                available: formatNumber(totals.mediaStoresAvailable, language),
                total: formatNumber(items.length, language),
              }),
            )}
          />
          <Info
            label={t("operatorStorage.overview.remoteCache")}
            value={aggregateValue(formatAggregateMedia(totals.remoteBytes, totals.remoteFiles, language, t))}
          />
          <Info
            label={t("operatorStorage.overview.generatedThumbnails")}
            value={aggregateValue(
              formatAggregateMedia(totals.thumbnailBytes, totals.thumbnailFiles, language, t),
            )}
          />
          <Info
            label={t("operatorStorage.overview.urlPreviewCache")}
            value={aggregateValue(formatAggregateMedia(totals.urlCacheBytes, totals.urlCacheFiles, language, t))}
          />
        </div>
      </CardContent>
    </Card>
  )
}

function getFilesystemCapacity(disk: DashboardDiskUsage | null) {
  if (
    !disk ||
    disk.scope !== "mem_data" ||
    !Number.isFinite(disk.usedBytes) ||
    !Number.isFinite(disk.totalBytes) ||
    disk.usedBytes < 0 ||
    disk.totalBytes <= 0 ||
    disk.usedBytes > disk.totalBytes
  ) {
    return null
  }

  const availableBytes = disk.totalBytes - disk.usedBytes
  const availablePercent = Math.round((availableBytes / disk.totalBytes) * 100)
  return {
    availableBytes,
    totalBytes: disk.totalBytes,
    availablePercent,
    usedPercent: 100 - availablePercent,
  }
}

function FilesystemCapacityMetric({
  disk,
  isLoading,
  isUnavailable,
  language,
}: {
  disk: ReturnType<typeof getFilesystemCapacity>
  isLoading: boolean
  isUnavailable: boolean
  language: UiLanguage
}) {
  const { t } = useI18n()

  if (isLoading && !disk) {
    return (
      <Metric
        label={t("operatorStorage.overview.memDataAvailable")}
        value={t("operatorStorage.inventory.loadingShort")}
      />
    )
  }

  if (isUnavailable || !disk) {
    return (
      <Metric
        label={t("operatorStorage.overview.memDataAvailable")}
        value={t("operatorStorage.overview.unavailable")}
      />
    )
  }

  const availablePercent = formatNumber(disk.availablePercent / 100, language, {
    style: "percent",
    maximumFractionDigits: 0,
  })
  const usedPercent = formatNumber(disk.usedPercent / 100, language, {
    style: "percent",
    maximumFractionDigits: 0,
  })

  return (
    <div
      className="rounded-lg border border-border bg-muted/20 p-4"
      data-testid="storage-filesystem-capacity"
    >
      <div className="text-xs uppercase tracking-wide text-muted-foreground">
        {t("operatorStorage.overview.memDataAvailable")}
      </div>
      <div className="mt-2 break-words text-2xl font-semibold tracking-tight text-foreground">
        {t("operatorStorage.overview.availablePercent", { percent: availablePercent })}
      </div>
      <div className="mt-1 flex flex-wrap items-center justify-between gap-x-3 gap-y-1 text-xs text-muted-foreground">
        <span>
          {t("operatorStorage.overview.capacityDetail", {
            available: formatBytes(disk.availableBytes, language),
            total: formatBytes(disk.totalBytes, language),
          })}
        </span>
        <span>{t("operatorStorage.overview.usedPercent", { percent: usedPercent })}</span>
      </div>
      <div
        aria-label={t("operatorStorage.overview.usedPercent", { percent: usedPercent })}
        className="mt-3 h-1.5 overflow-hidden rounded-full bg-muted"
        role="progressbar"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={disk.usedPercent}
      >
        <div
          aria-hidden="true"
          className="h-full rounded-full bg-primary/70"
          style={{ width: `${disk.usedPercent}%` }}
        />
      </div>
    </div>
  )
}

function summarizeStorage(items: StorageInventoryItem[]): StorageTotals {
  const totals: StorageTotals = {
    matrixBytes: 0,
    matrixFiles: 0,
    localBytes: 0,
    localFiles: 0,
    remoteBytes: 0,
    remoteFiles: 0,
    thumbnailBytes: 0,
    thumbnailFiles: 0,
    urlCacheBytes: 0,
    urlCacheFiles: 0,
    mediaStoresAvailable: 0,
  }

  for (const item of items) {
    if (!item.storage || item.hasError) continue

    const matrix = item.storage.matrix
    totals.matrixBytes += matrix.totalBytes
    totals.matrixFiles += matrix.totalFiles
    if (matrix.mediaStoreExists) totals.mediaStoresAvailable += 1

    addSection(totals, findSection(item.storage, "local_content"), "localBytes", "localFiles")
    addSection(totals, findSection(item.storage, "remote_content"), "remoteBytes", "remoteFiles")
    addSection(totals, findSection(item.storage, "thumbnails"), "thumbnailBytes", "thumbnailFiles")
    addSection(totals, findSection(item.storage, "url_cache"), "urlCacheBytes", "urlCacheFiles")
  }

  return totals
}

function addSection(
  totals: StorageTotals,
  section: { bytes: number; files: number } | null,
  bytesKey: "localBytes" | "remoteBytes" | "thumbnailBytes" | "urlCacheBytes",
  filesKey: "localFiles" | "remoteFiles" | "thumbnailFiles" | "urlCacheFiles",
) {
  if (!section) return
  totals[bytesKey] += section.bytes
  totals[filesKey] += section.files
}

function StorageInventoryRow({
  item,
  language,
}: {
  item: StorageInventoryItem
  language: UiLanguage
}) {
  const { t } = useI18n()
  const { stack, storage, isLoading, hasError } = item
  const matrix = storage?.matrix ?? null
  const localUploads = findSection(storage, "local_content")
  const remoteMedia = findSection(storage, "remote_content")
  const stackName = stack.displayName?.trim() || stack.slug
  const detailPath = `/stacks/${encodeURIComponent(stack.slug)}/services`

  const matrixMedia = hasError
    ? t("operatorStorage.inventory.inspectionFailed")
    : matrix
      ? formatMediaSummary(matrix, language, t)
      : isLoading
        ? t("operatorStorage.inventory.loadingShort")
        : t("stacks.storage.unavailable")

  const mediaStore = hasError
    ? t("operatorStorage.inventory.inspectionFailed")
    : matrix
      ? matrix.mediaStoreExists
        ? t("stacks.storage.available")
        : t("stacks.storage.notPresent")
      : isLoading
        ? t("operatorStorage.inventory.loadingShort")
        : t("stacks.storage.unavailable")

  return (
    <TableRow data-testid={`storage-row-${stack.slug}`}>
      <TableCell className="align-top">
        <Link to={detailPath} className="font-medium text-primary hover:underline">
          {stackName}
        </Link>
        {stackName !== stack.slug && (
          <div className="mt-1 text-xs text-muted-foreground">{stack.slug}</div>
        )}
      </TableCell>
      <TableCell className="align-top">{matrixMedia}</TableCell>
      <TableCell className="align-top">
        {formatStorageSection(localUploads, matrix, isLoading, hasError, language, t)}
      </TableCell>
      <TableCell className="align-top">
        {formatStorageSection(remoteMedia, matrix, isLoading, hasError, language, t)}
      </TableCell>
      <TableCell className="align-top">{mediaStore}</TableCell>
      <TableCell
        className="sticky right-0 z-20 min-w-40 border-l border-border bg-card align-top text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]"
        data-testid={`storage-actions-${stack.slug}`}
      >
        <Button variant="outline" size="sm" asChild>
          <Link to={detailPath}>
            {t("operatorStorage.inventory.openDetails")}
            <ArrowRight className="ml-2 h-4 w-4" />
          </Link>
        </Button>
      </TableCell>
    </TableRow>
  )
}

function findSection(storage: RuntimeStackStorageResponse | null, key: string) {
  return storage?.matrix.sections.find((section) => section.key === key) ?? null
}

function formatMediaSummary(
  matrix: RuntimeStackMatrixStorageResponse,
  language: UiLanguage,
  t: ReturnType<typeof useI18n>["t"],
) {
  return t("stacks.storage.sizeAndFiles", {
    size: formatBytes(matrix.totalBytes, language),
    files: t("stacks.storage.files", { count: matrix.totalFiles }),
  })
}

function formatStorageSection(
  section: { bytes: number; files: number; exists: boolean } | null,
  matrix: RuntimeStackMatrixStorageResponse | null,
  isLoading: boolean,
  hasError: boolean,
  language: UiLanguage,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (hasError) return t("operatorStorage.inventory.inspectionFailed")
  if (!matrix) return isLoading ? t("operatorStorage.inventory.loadingShort") : t("stacks.storage.unavailable")
  if (!section) return t("stacks.storage.notReported")
  if (!section.exists) return t("stacks.storage.notCreatedYet")

  return t("stacks.storage.sizeAndFiles", {
    size: formatBytes(section.bytes, language),
    files: t("stacks.storage.files", { count: section.files }),
  })
}

function formatAggregateMedia(
  bytes: number,
  files: number,
  language: UiLanguage,
  t: ReturnType<typeof useI18n>["t"],
) {
  return t("stacks.storage.sizeAndFiles", {
    size: formatBytes(bytes, language),
    files: t("stacks.storage.files", { count: files }),
  })
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border bg-muted/20 p-4">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-2 break-words text-2xl font-semibold tracking-tight text-foreground">{value}</div>
    </div>
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
