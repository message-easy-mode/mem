import { ArrowLeft, ArrowUp, BookOpenText, ChevronDown, FileText, Info, TriangleAlert } from "lucide-react"
import { useLayoutEffect, useRef, useState } from "react"
import { Link, Navigate, useLocation, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { DocumentationDocumentMeta } from "@/features/operator/docs/components/documentation-document-meta"
import { DocumentationPageHeader } from "@/features/operator/docs/components/documentation-page-header"
import { DocumentationReadingNavigation } from "@/features/operator/docs/components/documentation-reading-navigation"
import { DocumentationPackStatusStrip } from "@/features/operator/docs/components/documentation-pack-status-strip"
import { DocumentationSearch } from "@/features/operator/docs/components/documentation-search"
import { DocumentationMarkdown } from "@/features/operator/docs/components/documentation-markdown"
import { DocumentationNavigationTree } from "@/features/operator/docs/components/documentation-navigation-tree"
import { DocumentationOnThisPage } from "@/features/operator/docs/components/documentation-on-this-page"
import {
  getDocumentationReleasePack,
  resolveDocumentationAssetSource,
  resolveDocumentationHref,
  resolveDocumentationRoute,
} from "@/features/operator/docs/documentation-release-pack"
import { useDocumentationRouteScope } from "@/features/operator/docs/documentation-route-scope"
import { cn } from "@/lib/utils"

function DocumentationBackToTopAction() {
  const { t } = useI18n()

  return (
    <button
      type="button"
      className="fixed bottom-6 right-24 z-30 inline-flex h-10 w-10 items-center justify-center rounded-full border border-border bg-card/95 text-muted-foreground shadow-lg backdrop-blur transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
      onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
    >
      <ArrowUp className="h-4 w-4" aria-hidden="true" />
      <span className="sr-only">{t("documentation.backToTop")}</span>
    </button>
  )
}

export function DocumentationDocumentPage() {
  const { "*": documentPath } = useParams()
  const location = useLocation()
  const { language, t } = useI18n()
  const documentationRoute = useDocumentationRouteScope()
  const [mobileContentsOpen, setMobileContentsOpen] = useState(false)
  const mobileContentsDetailsRef = useRef<HTMLDetailsElement | null>(null)
  const routeResolution = resolveDocumentationRoute(language, documentPath)
  const resolvedDocument = routeResolution.kind === "document" ? routeResolution.document : undefined
  const translationUnavailable =
    location.state &&
    typeof location.state === "object" &&
    "documentationNotice" in location.state &&
    location.state.documentationNotice === "translation-unavailable" &&
    "language" in location.state &&
    location.state.language === language

  const previousDocumentId = useRef(resolvedDocument?.id)

  const closeMobileContents = () => {
    setMobileContentsOpen(false)

    if (mobileContentsDetailsRef.current) {
      mobileContentsDetailsRef.current.open = false
    }
  }

  useLayoutEffect(() => {
    if (previousDocumentId.current === resolvedDocument?.id) {
      return
    }

    previousDocumentId.current = resolvedDocument?.id

    const anchor = decodeURIComponent(location.hash.replace(/^#/, ""))
    const translatedHeadingExists =
      anchor.length > 0 && resolvedDocument?.headings.some((heading) => heading.id === anchor)

    if (translatedHeadingExists) {
      window.requestAnimationFrame(() => {
        window.document.getElementById(anchor)?.scrollIntoView({ block: "start" })
      })
      return
    }

    window.scrollTo({ top: 0, left: 0, behavior: "auto" })
  }, [resolvedDocument?.headings, resolvedDocument?.id, location.hash])

  if (routeResolution.kind === "redirect") {
    const targetHref = routeResolution.documentKey
      ? documentationRoute.documentHref(routeResolution.documentKey)
      : documentationRoute.rootHref()

    return (
      <Navigate
        to={
          routeResolution.reason === "legacy-alias"
            ? `${targetHref}${
                documentationRoute.setupMode ? "" : location.search
              }${location.hash}`
            : targetHref
        }
        replace
        state={
          routeResolution.reason === "translation-unavailable"
            ? { documentationNotice: "translation-unavailable", language }
            : undefined
        }
      />
    )
  }

  const document = routeResolution.document
  const documentationReleasePack = getDocumentationReleasePack(language)

  return (
    <div>
      {documentationRoute.setupMode ? (
        <div className="mb-5">
          <Link
            to={documentationRoute.returnTo ?? "/setup/start"}
            className="inline-flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm font-medium text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <ArrowLeft className="h-4 w-4" aria-hidden="true" />
            {t("documentation.setup.backToSetup")}
          </Link>
        </div>
      ) : null}

      <div
        className={cn(
          "sticky top-16 z-20 -mx-4 mb-7 border-b border-border bg-background/95 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 2xl:-mx-10 2xl:px-10",
          documentationRoute.setupMode ? "mt-0 sm:mt-0" : "-mt-6 sm:-mt-8",
        )}
      >
        <div className="mx-auto w-full max-w-[1760px]">
          <div className="hidden sm:block">
            <PageBreadcrumbs
              items={[
                { label: t("documentation.breadcrumb"), to: documentationRoute.rootHref() },
                { label: document.title },
              ]}
            />
          </div>

          <DocumentationPageHeader />
          <div className="mt-3 border-t border-border pt-3">
            <DocumentationSearch className="mb-0" />
          </div>

          <div className="mt-3 xl:hidden">
            <details
              ref={mobileContentsDetailsRef}
              className="rounded-xl border border-border bg-card px-3 py-2"
              open={mobileContentsOpen}
              onToggle={(event) => setMobileContentsOpen(event.currentTarget.open)}
            >
              <summary className="flex cursor-pointer list-none items-center justify-between gap-3 font-medium text-foreground">
                <span className="flex items-center gap-2">
                  <BookOpenText className="h-4 w-4" aria-hidden="true" />
                  {t("documentation.contents")}
                </span>
                <ChevronDown className="h-4 w-4" aria-hidden="true" />
              </summary>
              <div className="mt-3 max-h-[40vh] overflow-y-auto border-t border-border pt-3">
                <DocumentationNavigationTree
                  currentDocumentId={document.id}
                  activeScrollKey={mobileContentsOpen}
                  onNavigate={closeMobileContents}
                />
              </div>
            </details>
          </div>
        </div>
      </div>

      <div className="grid gap-8 xl:grid-cols-[15.5rem_minmax(0,1fr)_15rem] xl:items-start">
        <aside className="sticky top-[14rem] hidden max-h-[calc(100vh-15rem)] overflow-y-auto border-r border-border pr-5 xl:block">
          <DocumentationNavigationTree currentDocumentId={document.id} />
        </aside>

        <main className="min-w-0">
          <header className="border-b border-border pb-7">
            <div className="flex items-start gap-3">
              <div className="rounded-xl bg-primary/10 p-2.5 text-primary">
                <FileText className="h-5 w-5" aria-hidden="true" />
              </div>
              <div>
                <h1 className="text-3xl font-semibold tracking-tight text-foreground">{document.title}</h1>
                <p className="mt-2 max-w-3xl text-base leading-7 text-muted-foreground">{document.summary}</p>
              </div>
            </div>
          </header>

          {translationUnavailable ? (
            <aside
              className="mt-6 flex gap-3 rounded-xl border border-primary/30 bg-primary/5 p-4 text-sm leading-6 text-foreground"
              role="status"
            >
              <Info className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
              <div>
                <p className="font-medium">{t("documentation.translationUnavailableTitle")}</p>
                <p className="mt-1 text-muted-foreground">
                  {t("documentation.translationUnavailableDescription")}
                </p>
              </div>
            </aside>
          ) : null}

          {documentationReleasePack.reviewRequired ? (
            <aside
              className="mt-6 flex gap-3 rounded-xl border border-amber-500/35 bg-amber-500/10 p-4 text-sm leading-6 text-foreground"
              role="note"
            >
              <TriangleAlert className="mt-0.5 h-5 w-5 shrink-0 text-amber-700 dark:text-amber-200" aria-hidden="true" />
              <div>
                <p className="font-medium">{t("documentation.reviewRequiredTitle")}</p>
                <p className="mt-1 text-muted-foreground">{t("documentation.reviewRequiredDescription")}</p>
              </div>
            </aside>
          ) : null}

          <DocumentationMarkdown
            markdown={document.markdown}
            headings={document.headings}
            resolveLocalLink={(href) => {
              const resolvedHref = resolveDocumentationHref(document, href)
              return resolvedHref
                ? documentationRoute.rebaseHref(resolvedHref)
                : undefined
            }}
            resolveLocalImage={(source) =>
              resolveDocumentationAssetSource(document, source)
            }
          />
          <DocumentationReadingNavigation document={document} />
        </main>

        <aside
          aria-label={t("documentation.onThisPageAria")}
          className="sticky top-[14rem] hidden max-h-[calc(100vh-15rem)] overflow-y-auto xl:block"
        >
          <DocumentationOnThisPage headings={document.headings} />
          <DocumentationDocumentMeta document={document} />
        </aside>
      </div>

      <details className="mt-3 rounded-xl border border-border bg-card p-3 xl:hidden">
        <summary className="flex cursor-pointer list-none items-center justify-between gap-3 font-medium text-foreground">
          <span>{t("documentation.onThisPage")}</span>
          <ChevronDown className="h-4 w-4" aria-hidden="true" />
        </summary>
        <div className="mt-4 border-t border-border pt-4">
          <DocumentationOnThisPage headings={document.headings} />
          <DocumentationDocumentMeta document={document} />
        </div>
      </details>

      <DocumentationPackStatusStrip />
      <DocumentationBackToTopAction />
    </div>
  )
}
