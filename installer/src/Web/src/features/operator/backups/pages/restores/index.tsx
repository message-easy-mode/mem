import { useCallback, useEffect, useMemo, useState } from "react"
import { useSearchParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { WarningList } from "@/features/operator/backups/shared/components/backup-common"
import type { RestoreAttemptListRequest } from "@/features/operator/backups/api"
import { useRestoreAttempts } from "@/features/operator/backups/hooks/use-backups"

import {
  DEFAULT_PAGE_SIZE,
  readRestoreSessionsQuery,
  toRestoreAttemptListRequest,
  type RestoreSessionsUrlUpdater,
} from "./components/restore-sessions-query"
import { RestoreSessionsPageHeader } from "./components/restore-sessions-page-header"
import { RestoreSessionsSummaryCards } from "./components/restore-sessions-summary-cards"
import { RestoreSessionsTable } from "./components/restore-sessions-table"

export function BackupRestoreSessionsPage() {
  const { t } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const query = readRestoreSessionsQuery(searchParams)
  const [searchInput, setSearchInput] = useState(() => query.search)

  const updateUrl = useCallback<RestoreSessionsUrlUpdater>(
    (changes, resetPage = true) => {
      setSearchParams((current) => {
        const next = new URLSearchParams(current)

        for (const [key, value] of Object.entries(changes)) {
          if (!value) {
            next.delete(key)
          } else {
            next.set(key, value)
          }
        }

        if (resetPage && !Object.hasOwn(changes, "page")) {
          next.delete("page")
        }

        if (next.get("page") === "1") {
          next.delete("page")
        }

        if (next.get("pageSize") === DEFAULT_PAGE_SIZE.toString()) {
          next.delete("pageSize")
        }

        if (next.get("status") === "all") {
          next.delete("status")
        }

        if (next.get("sort") === "updated") {
          next.delete("sort")
        }

        if (next.get("direction") === "desc") {
          next.delete("direction")
        }

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

      if (nextSearch !== query.search) {
        updateUrl({ q: nextSearch || null })
      }
    }, 300)

    return () => window.clearTimeout(timer)
  }, [query.search, searchInput, updateUrl])

  const request = useMemo<RestoreAttemptListRequest>(
    () => toRestoreAttemptListRequest(query),
    [query],
  )

  const restoreAttemptsQuery = useRestoreAttempts(request)
  const response = restoreAttemptsQuery.data

  // The API clamps pages after a removal or filter change. Keep browser history
  // and refreshes aligned with the page that HostAgent actually returned.
  useEffect(() => {
    if (
      response &&
      !restoreAttemptsQuery.isPlaceholderData &&
      response.page !== query.page
    ) {
      updateUrl({ page: response.page.toString() }, false)
    }
  }, [query.page, response, restoreAttemptsQuery.isPlaceholderData, updateUrl])

  const hasFilters = Boolean(
    query.search || query.targetStack || query.status !== "all",
  )

  return (
    <div className="space-y-6">
      <RestoreSessionsPageHeader
        isRefreshing={restoreAttemptsQuery.isFetching}
        onRefresh={() => void restoreAttemptsQuery.refetch()}
      />

      {restoreAttemptsQuery.error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("restoreSessions.loadError")}</AlertTitle>
          <AlertDescription>{restoreAttemptsQuery.error.message}</AlertDescription>
        </Alert>
      ) : null}

      <RestoreSessionsSummaryCards
        summary={response?.summary}
        hasFilters={hasFilters}
      />

      <RestoreSessionsTable
        response={response}
        isLoading={restoreAttemptsQuery.isLoading}
        isFetching={restoreAttemptsQuery.isFetching}
        query={query}
        searchInput={searchInput}
        onSearchInputChange={setSearchInput}
        updateUrl={updateUrl}
      />

      {response?.warnings.length ? <WarningList warnings={response.warnings} /> : null}
    </div>
  )
}
