import "../components/migration-target.css"

import { useCallback, useEffect, useMemo, useState } from "react"
import { AlertTriangle, Plus, RefreshCw } from "lucide-react"
import { Link, useLocation, useSearchParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { MigrationSessionsSummaryCards } from "@/features/operator/migrations/components/migration-sessions-summary-cards"
import { MigrationSessionsTable } from "@/features/operator/migrations/components/migration-sessions-table"
import {
  DEFAULT_PAGE_SIZE,
  readMigrationSessionsQuery,
  toMigrationSessionInventoryRequest,
  type MigrationSessionActionFilter,
  type MigrationSessionLifecycle,
  type MigrationSessionsUrlUpdater,
} from "@/features/operator/migrations/components/migration-sessions-query"
import { useMigrationSessions } from "@/features/operator/migrations/hooks/use-migration-sessions"

export function MigrationsPage() {
  const { t } = useI18n()
  const location = useLocation()
  const cancellationNotice = location.state as { cancelledMigrationName?: unknown } | null
  const [searchParams, setSearchParams] = useSearchParams()
  const query = readMigrationSessionsQuery(searchParams)
  const [searchInput, setSearchInput] = useState(() => query.search)

  const updateUrl = useCallback<MigrationSessionsUrlUpdater>(
    (changes, resetPage = true) => {
      setSearchParams((current) => {
        const next = new URLSearchParams(current)

        for (const [key, value] of Object.entries(changes)) {
          if (value === undefined) continue
          if (value === null || value === "") next.delete(key)
          else next.set(key, value)
        }

        if (resetPage && !Object.hasOwn(changes, "page")) next.delete("page")

        if (next.get("page") === "1") next.delete("page")
        if (next.get("pageSize") === String(DEFAULT_PAGE_SIZE)) next.delete("pageSize")
        if (next.get("lifecycle") === "all") next.delete("lifecycle")
        if (next.get("action") === "all") next.delete("action")
        if (next.get("sortBy") === "updated") next.delete("sortBy")
        if (next.get("sortDirection") === "desc") next.delete("sortDirection")
        if (next.get("includeArchived") === "false") next.delete("includeArchived")

        if (next.get("lifecycle") === "archived") next.set("includeArchived", "true")

        return next
      })
    },
    [setSearchParams],
  )

  useEffect(() => {
    setSearchInput(query.search)
  }, [query.search])

  useEffect(() => {
    const timer = window.setTimeout(() => {
      const nextSearch = searchInput.trim()
      if (nextSearch !== query.search) updateUrl({ search: nextSearch || null })
    }, 300)

    return () => window.clearTimeout(timer)
  }, [query.search, searchInput, updateUrl])

  const request = useMemo(() => toMigrationSessionInventoryRequest(query), [query])
  const sessionsQuery = useMigrationSessions(request)
  const response = sessionsQuery.data

  useEffect(() => {
    if (response && !sessionsQuery.isPlaceholderData && response.page !== query.page) {
      updateUrl({ page: String(response.page) }, false)
    }
  }, [query.page, response, sessionsQuery.isPlaceholderData, updateUrl])

  const clearFilters = useCallback(() => {
    setSearchInput("")
    updateUrl({
      search: null,
      lifecycle: null,
      action: null,
      stage: null,
      targetStack: null,
      includeArchived: null,
    })
  }, [updateUrl])

  const setOperationalFilter = useCallback((
    lifecycle: MigrationSessionLifecycle,
    action: MigrationSessionActionFilter,
    includeArchived: boolean,
  ) => {
    updateUrl({
      lifecycle: lifecycle === "all" ? null : lifecycle,
      action: action === "all" ? null : action,
      includeArchived: includeArchived ? "true" : null,
    })
  }, [updateUrl])

  return (
    <div className="migration-target-inventory space-y-4">
      {typeof cancellationNotice?.cancelledMigrationName === "string" ? (
        <Alert role="status" aria-live="polite">
          <AlertTitle>{t("migrationWorkspace.cancel.successTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.cancel.successDescription", {
            name: cancellationNotice.cancelledMigrationName,
          })}</AlertDescription>
        </Alert>
      ) : null}
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0 flex-1">
          <h1 className="text-2xl font-semibold tracking-tight">{t("migrationWorkspace.title")}</h1>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            {t("migrationWorkspace.description")}
          </p>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2">
          <Button
            type="button"
            variant="outline"
            onClick={() => void sessionsQuery.refetch()}
            disabled={sessionsQuery.isFetching}
          >
            <RefreshCw className={sessionsQuery.isFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"} aria-hidden="true" />
            {t("migrationWorkspace.refresh")}
          </Button>
          <Button asChild>
            <Link to="/migrations/new">
              <Plus className="mr-2 h-4 w-4" aria-hidden="true" />
              {t("migrationWorkspace.start")}
            </Link>
          </Button>
        </div>
      </div>

      {!sessionsQuery.isError ? (
        <MigrationSessionsSummaryCards
          summary={response?.summary}
          lifecycle={query.lifecycle}
          action={query.action}
          includeArchived={query.includeArchived}
          onSelect={({ lifecycle, action }) => setOperationalFilter(lifecycle, action, false)}
        />
      ) : null}

      <MigrationSessionsTable
        response={response}
        isLoading={sessionsQuery.isLoading}
        isFetching={sessionsQuery.isFetching}
        isError={sessionsQuery.isError}
        query={query}
        searchInput={searchInput}
        onSearchInputChange={setSearchInput}
        updateUrl={updateUrl}
        onClearFilters={clearFilters}
        onQuickFilterChange={setOperationalFilter}
      />

      {response?.warnings.length ? (
        <Alert>
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.inventory.warning.title")}</AlertTitle>
          <AlertDescription>
            {response.warnings.map((warning) => <div key={warning.code}>{warning.message}</div>)}
          </AlertDescription>
        </Alert>
      ) : null}
    </div>
  )
}
