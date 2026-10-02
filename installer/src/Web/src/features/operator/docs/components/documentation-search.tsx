import { Filter, Search, X } from "lucide-react"
import { useEffect, useRef, useState } from "react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select } from "@/components/ui/select"
import {
  getDocumentationSearchSections,
  getDocumentationSearchTags,
  searchDocumentation,
  type DocumentationSearchResult,
} from "@/features/operator/docs/documentation-search"
import { useDocumentationRouteScope } from "@/features/operator/docs/documentation-route-scope"
import { cn } from "@/lib/utils"

export function DocumentationSearch({ className }: { className?: string } = {}) {
  const { language, t } = useI18n()
  const documentationRoute = useDocumentationRouteScope()
  const searchInputRef = useRef<HTMLInputElement>(null)
  const [query, setQuery] = useState("")
  const [groupId, setGroupId] = useState("")
  const [tag, setTag] = useState("")
  const [advancedOpen, setAdvancedOpen] = useState(false)
  const [results, setResults] = useState<readonly DocumentationSearchResult[]>([])
  const [completedSearchKey, setCompletedSearchKey] = useState("")
  const documentationSearchSections = getDocumentationSearchSections(language)
  const documentationSearchTags = getDocumentationSearchTags(language)
  const activeGroupId = documentationSearchSections.some((section) => section.id === groupId)
    ? groupId
    : ""
  const activeTag = documentationSearchTags.includes(tag) ? tag : ""
  const hasSearch = query.trim().length > 0
  const hasFilters = Boolean(activeGroupId || activeTag)
  const searchKey = `${language}\u0000${query}\u0000${activeGroupId}\u0000${activeTag}`
  const searching = hasSearch && completedSearchKey !== searchKey

  useEffect(() => {
    let cancelled = false

    if (!hasSearch) {
      return () => {
        cancelled = true
      }
    }

    void searchDocumentation(language, query, {
      groupId: activeGroupId || undefined,
      tag: activeTag || undefined,
    })
      .then((nextResults) => {
        if (!cancelled) {
          setResults(nextResults)
          setCompletedSearchKey(searchKey)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setResults([])
          setCompletedSearchKey(searchKey)
        }
      })

    return () => {
      cancelled = true
    }
  }, [activeGroupId, activeTag, hasSearch, language, query, searchKey])

  useEffect(() => {
    function handleShortcut(event: KeyboardEvent) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLocaleLowerCase("en") === "k") {
        event.preventDefault()
        searchInputRef.current?.focus()
      }
    }

    window.addEventListener("keydown", handleShortcut)
    return () => window.removeEventListener("keydown", handleShortcut)
  }, [])

  function clearSearch() {
    setQuery("")
    setGroupId("")
    setTag("")
  }

  return (
    <section className={cn("mb-7", className)} aria-labelledby="documentation-search-heading">
      <h2 id="documentation-search-heading" className="sr-only">
        {t("documentation.search")}
      </h2>

      <div className="flex flex-col gap-2 border-b border-border pb-3 lg:flex-row lg:items-center">
        <div className="relative min-w-0 flex-1">
          <Search
            className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <Input
            ref={searchInputRef}
            type="search"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            placeholder={t("documentation.searchPlaceholder")}
            aria-label={t("documentation.search")}
            className="h-9 border-border bg-card pl-9 pr-20 shadow-sm sm:h-10 sm:pr-24"
          />
          {hasSearch ? (
            <button
              type="button"
              onClick={() => setQuery("")}
              aria-label={t("documentation.clearQuery")}
              className="absolute top-1/2 right-12 -translate-y-1/2 rounded p-1 text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          ) : null}
          <kbd className="pointer-events-none absolute top-1/2 right-3 hidden -translate-y-1/2 rounded border border-border bg-muted px-1.5 py-0.5 text-[0.7rem] font-medium text-muted-foreground sm:block">
            {t("documentation.searchShortcut")}
          </kbd>
        </div>

        <div className="flex flex-wrap items-center gap-1">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => setAdvancedOpen((current) => !current)}
            aria-expanded={advancedOpen}
            aria-controls="documentation-search-filters"
          >
            <Filter data-icon="inline-start" aria-hidden="true" />
            {advancedOpen ? t("documentation.hideAdvancedSearch") : t("documentation.advancedSearch")}
          </Button>
          {hasSearch || hasFilters ? (
            <Button type="button" variant="ghost" size="sm" onClick={clearSearch}>
              <X data-icon="inline-start" aria-hidden="true" />
              {t("documentation.clearSearch")}
            </Button>
          ) : null}
        </div>
      </div>

      {advancedOpen ? (
        <div
          id="documentation-search-filters"
          className="mt-3 grid gap-3 rounded-xl border border-border bg-card p-3 sm:grid-cols-2"
        >
          <label className="grid gap-1.5 text-sm font-medium text-foreground" htmlFor="documentation-search-section">
            {t("documentation.searchSection")}
            <Select
              id="documentation-search-section"
              value={activeGroupId}
              onChange={(event) => setGroupId(event.target.value)}
              className="w-full"
              wrapperClassName="w-full"
            >
              <option value="">{t("documentation.searchAllSections")}</option>
              {documentationSearchSections.map((section) => (
                <option key={section.id} value={section.id}>
                  {section.label}
                </option>
              ))}
            </Select>
          </label>

          <label className="grid gap-1.5 text-sm font-medium text-foreground" htmlFor="documentation-search-topic">
            {t("documentation.searchTopic")}
            <Select
              id="documentation-search-topic"
              value={activeTag}
              onChange={(event) => setTag(event.target.value)}
              className="w-full"
              wrapperClassName="w-full"
            >
              <option value="">{t("documentation.searchAllTopics")}</option>
              {documentationSearchTags.map((candidate) => (
                <option key={candidate} value={candidate}>
                  {candidate}
                </option>
              ))}
            </Select>
          </label>
        </div>
      ) : null}

      {hasSearch ? (
        <div className="mt-3 rounded-xl border border-border bg-card p-4" aria-live="polite">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h2 className="text-sm font-semibold text-foreground">{t("documentation.searchResults")}</h2>
            <span className="text-sm text-muted-foreground">
              {searching
                ? t("documentation.searching")
                : t("documentation.searchResultsCount", { count: results.length })}
            </span>
          </div>

          {searching ? (
            <p className="mt-4 text-sm text-muted-foreground">{t("documentation.searching")}</p>
          ) : results.length === 0 ? (
            <div className="mt-4 rounded-lg bg-muted/55 p-4">
              <p className="font-medium text-foreground">{t("documentation.noSearchResults", { query })}</p>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {t("documentation.noSearchResultsDescription")}
              </p>
            </div>
          ) : (
            <ol className="mt-3 divide-y divide-border">
              {results.map((result) => (
                <li key={result.href}>
                  <Link
                    to={documentationRoute.rebaseHref(result.href)}
                    onClick={clearSearch}
                    className="block rounded-lg px-2 py-3 transition hover:bg-muted focus-visible:bg-muted focus-visible:outline-none"
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-foreground">{result.document.title}</span>
                      <Badge variant="outline">{result.sectionLabel}</Badge>
                      {result.matchedHeading ? (
                        <span className="text-sm text-muted-foreground">
                          {t("documentation.searchHeadingMatch", { heading: result.matchedHeading.label })}
                        </span>
                      ) : null}
                    </div>
                    <p className="mt-1 line-clamp-2 text-sm leading-6 text-muted-foreground">{result.excerpt}</p>
                  </Link>
                </li>
              ))}
            </ol>
          )}
        </div>
      ) : null}

      {hasFilters && !hasSearch ? (
        <p className={cn("mt-2 text-sm text-muted-foreground")}>
          {t("documentation.searchFiltersWaiting")}
        </p>
      ) : null}
    </section>
  )
}
