import { Archive, ChevronLeft, ChevronRight, ClipboardList, Clock3, Database, HardDriveDownload, RefreshCw, Search, ShieldCheck, UploadCloud } from "lucide-react"
import { useMemo, useState } from "react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Select } from "@/components/ui/select"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import type { BackupCatalogListItem } from "@/features/operator/backups/api"
import { useBackupCatalog } from "@/features/operator/backups/hooks/use-backup-catalog"
import { formatBytes, formatDate } from "@/features/operator/backups/shared/components/backup-formatting"

import { BackupCatalogEmptyState, BackupCatalogTable } from "./components/backup-catalog-table"

type OriginFilter = "all" | "local-captured" | "imported-zip"
type SortOrder = "newest" | "oldest" | "backup-id" | "stack"
type RowsPerPage = 10 | 25 | 50

/**
 * The primary Backup / Restore entry point is the catalog. Local captures and
 * materialised imported ZIPs share one recovery journey; origin remains a
 * provenance field on each catalog item rather than a separate destination.
 */
export function BackupRestorePage() {
  const { language, t } = useI18n()
  const catalogQuery = useBackupCatalog()
  const entries = catalogQuery.data?.entries ?? []
  const [search, setSearch] = useState("")
  const [originFilter, setOriginFilter] = useState<OriginFilter>("all")
  const [stackFilter, setStackFilter] = useState("all")
  const [sortOrder, setSortOrder] = useState<SortOrder>("newest")
  const [rowsPerPage, setRowsPerPage] = useState<RowsPerPage>(10)
  const [currentPage, setCurrentPage] = useState(1)

  const validCount = entries.filter((entry) => entry.integrityStatus === "valid").length
  const warningCount = entries.filter((entry) => entry.integrityStatus === "warning").length
  const advisoryCount = entries.reduce((total, entry) => total + (entry.advisoryCount ?? 0), 0)
  const totalBytes = entries.reduce((total, entry) => total + (entry.payloadBytes ?? 0), 0)
  const latestEntry = latestCatalogEntry(entries)
  const stacks = useMemo(() => [...new Set(entries.map((entry) => entry.sourceStackSlug).filter((stack): stack is string => Boolean(stack)))].sort(), [entries])
  const visibleEntries = useMemo(() => filterAndSortCatalogEntries(entries, { search, originFilter, stackFilter, sortOrder }), [entries, originFilter, search, sortOrder, stackFilter])
  const hasFilters = search.trim().length > 0 || originFilter !== "all" || stackFilter !== "all" || sortOrder !== "newest"
  const pageCount = Math.max(1, Math.ceil(visibleEntries.length / rowsPerPage))
  const safeCurrentPage = Math.min(currentPage, pageCount)
  const pageStart = (safeCurrentPage - 1) * rowsPerPage
  const pagedEntries = visibleEntries.slice(pageStart, pageStart + rowsPerPage)
  const showingStart = visibleEntries.length === 0 ? 0 : pageStart + 1
  const showingEnd = Math.min(pageStart + rowsPerPage, visibleEntries.length)

  function resetFilters() {
    setSearch("")
    setOriginFilter("all")
    setStackFilter("all")
    setSortOrder("newest")
    setCurrentPage(1)
  }

  function updateSearch(value: string) {
    setSearch(value)
    setCurrentPage(1)
  }

  function updateOriginFilter(value: OriginFilter) {
    setOriginFilter(value)
    setCurrentPage(1)
  }

  function updateStackFilter(value: string) {
    setStackFilter(value)
    setCurrentPage(1)
  }

  function updateSortOrder(value: SortOrder) {
    setSortOrder(value)
    setCurrentPage(1)
  }

  function updateRowsPerPage(value: RowsPerPage) {
    setRowsPerPage(value)
    setCurrentPage(1)
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <PageBreadcrumbs items={[{ label: t("backupCatalog.breadcrumb.backupRestore") }, { label: t("backupCatalog.breadcrumb.catalog") }]} />
          <h1 className="text-2xl font-semibold tracking-tight">{t("backupCatalog.title")}</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("backupCatalog.description")}
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" asChild><Link to="/restores"><ClipboardList className="mr-2 h-4 w-4" />{t("backupCatalog.restoreSessions")}</Link></Button>
          <Button variant="outline" size="sm" asChild><Link to="/backups/import"><UploadCloud className="mr-2 h-4 w-4" />{t("backupCatalog.importZip")}</Link></Button>
          <Button variant="outline" size="sm" onClick={() => void catalogQuery.refetch()} disabled={catalogQuery.isFetching}>
            <RefreshCw className="mr-2 h-4 w-4" />{t("backupCatalog.refresh")}
          </Button>
        </div>
      </div>

      {catalogQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("backupCatalog.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{catalogQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <CatalogSummaryCard icon={Database} tone="emerald" label={t("backupCatalog.summary.catalogItems")} value={catalogQuery.data?.totalCount ?? 0} detail={t("backupCatalog.summary.backupsAvailable")} />
        <CatalogSummaryCard icon={ShieldCheck} tone="sky" label={t("backupCatalog.summary.integrityStatus")} value={t("backupCatalog.summary.validCount", { count: validCount })} detail={warningCount > 0 ? t("backupCatalog.summary.integrityWarnings", { count: warningCount }) : advisoryCount > 0 ? t("backupCatalog.summary.importAdvisories", { count: advisoryCount }) : t("backupCatalog.summary.noIntegrityWarnings")} />
        <CatalogSummaryCard icon={Clock3} tone="violet" label={t("backupCatalog.summary.latestBackup")} value={latestEntry ? formatDate(latestCatalogTime(latestEntry), language) : t("backupCatalog.summary.noneYet")} detail={latestEntry?.sourceStackSlug ?? t("backupCatalog.summary.noBackupCaptured")} compactValue />
        <CatalogSummaryCard icon={HardDriveDownload} tone="cyan" label={t("backupCatalog.summary.totalSize")} value={formatBytes(totalBytes, language)} detail={t("backupCatalog.summary.catalogPayloads")} />
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2"><Archive className="h-5 w-5" />{t("backupCatalog.entries.title")}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("backupCatalog.entries.description")}
          </p>
        </CardHeader>
        <CardContent className="space-y-4">
          {!catalogQuery.isLoading && !catalogQuery.error && entries.length > 0 ? (
            <div className="grid gap-3 lg:grid-cols-[minmax(0,1fr)_auto_auto_auto_auto] lg:items-center">
              <div className="relative">
                <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
                <Input aria-label={t("backupCatalog.filters.searchAria")} value={search} onChange={(event) => updateSearch(event.target.value)} placeholder={t("backupCatalog.filters.searchPlaceholder")} className="pl-9" />
              </div>
              <Select aria-label={t("backupCatalog.filters.originAria")} value={originFilter} onChange={(event) => updateOriginFilter(event.target.value as OriginFilter)}>
                <option value="all">{t("backupCatalog.filters.allOrigins")}</option>
                <option value="local-captured">{t("backupCatalog.filters.localBackups")}</option>
                <option value="imported-zip">{t("backupCatalog.filters.importedZips")}</option>
              </Select>
              <Select aria-label={t("backupCatalog.filters.stackAria")} value={stackFilter} onChange={(event) => updateStackFilter(event.target.value)}>
                <option value="all">{t("backupCatalog.filters.allStacks")}</option>
                {stacks.map((stack) => <option key={stack} value={stack}>{stack}</option>)}
              </Select>
              <Select aria-label={t("backupCatalog.filters.sortAria")} value={sortOrder} onChange={(event) => updateSortOrder(event.target.value as SortOrder)}>
                <option value="newest">{t("backupCatalog.filters.newestFirst")}</option>
                <option value="oldest">{t("backupCatalog.filters.oldestFirst")}</option>
                <option value="backup-id">{t("backupCatalog.filters.backupId")}</option>
                <option value="stack">{t("backupCatalog.filters.stack")}</option>
              </Select>
              {hasFilters ? <Button variant="outline" size="sm" onClick={resetFilters}>{t("backupCatalog.filters.clear")}</Button> : <span className="hidden lg:block" aria-hidden="true" />}
            </div>
          ) : null}
          {catalogQuery.isLoading ? <CatalogLoadingState /> : null}
          {!catalogQuery.isLoading && !catalogQuery.error && entries.length === 0 ? <BackupCatalogEmptyState /> : null}
          {!catalogQuery.isLoading && !catalogQuery.error && entries.length > 0 && visibleEntries.length === 0 ? <BackupCatalogFilteredEmptyState onClear={resetFilters} /> : null}
          {!catalogQuery.isLoading && !catalogQuery.error && visibleEntries.length > 0 ? <BackupCatalogTable entries={pagedEntries} /> : null}
          {!catalogQuery.isLoading && !catalogQuery.error && entries.length > 0 ? (
            <div className="flex flex-col gap-3 border-t pt-4 text-sm text-muted-foreground sm:flex-row sm:items-center sm:justify-between">
              <p>{t("backupCatalog.results.matchingBackups", { start: showingStart, end: showingEnd, count: visibleEntries.length })}</p>
              <div className="flex flex-wrap items-center gap-2">
                <span>{t("backupCatalog.results.rows")}</span>
                <Select aria-label={t("backupCatalog.results.rowsPerPageAria")} className="h-8 w-20" value={String(rowsPerPage)} onChange={(event) => updateRowsPerPage(Number(event.target.value) as RowsPerPage)}>
                  <option value="10">10</option><option value="25">25</option><option value="50">50</option>
                </Select>
                <Button aria-label={t("backupCatalog.results.previousPageAria")} size="sm" variant="outline" onClick={() => setCurrentPage((page) => Math.max(1, page - 1))} disabled={safeCurrentPage === 1}><ChevronLeft className="h-4 w-4" />{t("backupCatalog.results.previousPage")}</Button>
                <span className="min-w-8 text-center text-foreground">{safeCurrentPage}</span>
                <Button aria-label={t("backupCatalog.results.nextPageAria")} size="sm" variant="outline" onClick={() => setCurrentPage((page) => Math.min(pageCount, page + 1))} disabled={safeCurrentPage === pageCount}>{t("backupCatalog.results.nextPage")}<ChevronRight className="ml-1 h-4 w-4" /></Button>
              </div>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  )
}

function filterAndSortCatalogEntries(entries: BackupCatalogListItem[], filters: { search: string; originFilter: OriginFilter; stackFilter: string; sortOrder: SortOrder }) {
  const normalizedSearch = filters.search.trim().toLowerCase()
  return entries
    .filter((entry) => {
      const matchesSearch = normalizedSearch.length === 0 || [entry.sourceBackupId, entry.displayName, entry.sourceStackSlug].filter(Boolean).some((value) => value!.toLowerCase().includes(normalizedSearch))
      const matchesOrigin = filters.originFilter === "all" || entry.originKind === filters.originFilter
      const matchesStack = filters.stackFilter === "all" || entry.sourceStackSlug === filters.stackFilter
      return matchesSearch && matchesOrigin && matchesStack
    })
    .sort((left, right) => {
      if (filters.sortOrder === "backup-id") return (left.sourceBackupId ?? left.displayName).localeCompare(right.sourceBackupId ?? right.displayName)
      if (filters.sortOrder === "stack") return (left.sourceStackSlug ?? "").localeCompare(right.sourceStackSlug ?? "")
      const difference = Date.parse(latestCatalogTime(left)) - Date.parse(latestCatalogTime(right))
      return filters.sortOrder === "oldest" ? difference : -difference
    })
}

function latestCatalogEntry(entries: BackupCatalogListItem[]) {
  return [...entries].sort((left, right) => Date.parse(latestCatalogTime(right)) - Date.parse(latestCatalogTime(left)))[0]
}

function latestCatalogTime(entry: BackupCatalogListItem) {
  return entry.capturedAtUtc ?? entry.importedAtUtc ?? entry.createdAtUtc
}

function CatalogSummaryCard({ icon: Icon, tone, label, value, detail, compactValue = false }: { icon: typeof Database; tone: "emerald" | "sky" | "violet" | "cyan" | "amber"; label: string; value: string | number; detail: string; compactValue?: boolean }) {
  const toneClasses = {
    emerald: "bg-emerald-500/10 text-emerald-400 ring-1 ring-emerald-400/20",
    sky: "bg-sky-500/10 text-sky-400 ring-1 ring-sky-400/20",
    violet: "bg-violet-500/10 text-violet-400 ring-1 ring-violet-400/20",
    cyan: "bg-cyan-500/10 text-cyan-400 ring-1 ring-cyan-400/20",
    amber: "bg-amber-500/10 text-amber-400 ring-1 ring-amber-400/20",
  }[tone]
  return (
    <Card className="py-0">
      <CardContent className="flex min-h-[88px] flex-col justify-center gap-2 px-3 py-3">
        <div className="flex min-w-0 items-center gap-2 text-xs font-medium text-muted-foreground">
          <span className={`inline-flex size-7 shrink-0 items-center justify-center rounded-md ${toneClasses}`}><Icon className="size-3.5" aria-hidden="true" /></span>
          <span className="min-w-0 truncate">{label}</span>
        </div>
        <div className="flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-0.5">
          <div className={compactValue ? "text-base font-semibold leading-tight tracking-tight" : "text-2xl font-semibold leading-none tracking-tight"}>{value}</div>
          <div className="text-xs leading-4 text-muted-foreground">{detail}</div>
        </div>
      </CardContent>
    </Card>
  )
}

function CatalogLoadingState() {
  const { t } = useI18n()

  return <div className="space-y-3" aria-label={t("backupCatalog.loadingAria")}><div className="h-10 animate-pulse rounded bg-muted" /><div className="h-16 animate-pulse rounded bg-muted" /><div className="h-16 animate-pulse rounded bg-muted" /></div>
}

function BackupCatalogFilteredEmptyState({ onClear }: { onClear: () => void }) {
  const { t } = useI18n()

  return (
    <div className="flex min-h-48 flex-col items-center justify-center rounded-lg border border-dashed p-6 text-center">
      <Search className="mb-3 h-8 w-8 text-muted-foreground" />
      <h3 className="font-medium">{t("backupCatalog.filteredEmpty.title")}</h3>
      <p className="mt-1 max-w-md text-sm text-muted-foreground">{t("backupCatalog.filteredEmpty.description")}</p>
      <Button className="mt-4" variant="outline" onClick={onClear}>{t("backupCatalog.filteredEmpty.clear")}</Button>
    </div>
  )
}
