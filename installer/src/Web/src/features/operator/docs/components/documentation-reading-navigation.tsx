import { ArrowLeft, ArrowRight, BookOpenText } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { getDocumentationReadingContext } from "@/features/operator/docs/documentation-release-pack"
import type { DocumentationDocument } from "@/features/operator/docs/docs.types"
import { useDocumentationRouteScope } from "@/features/operator/docs/documentation-route-scope"
import { cn } from "@/lib/utils"

type DocumentationReadingNavigationProps = Readonly<{
  document: DocumentationDocument
}>

type ReadingDirectionLinkProps = Readonly<{
  direction: "previous" | "next"
  document: DocumentationDocument
}>

function ReadingDirectionLink({ direction, document }: ReadingDirectionLinkProps) {
  const { t } = useI18n()
  const documentationRoute = useDocumentationRouteScope()
  const previous = direction === "previous"
  const label = previous
    ? t("documentation.previousDocument")
    : t("documentation.nextDocument")

  return (
    <Link
      to={documentationRoute.documentHref(document.key)}
      aria-label={`${label}: ${document.title}`}
      className={cn(
        "group flex min-h-28 items-start gap-3 rounded-xl border border-border bg-card p-4 transition",
        "hover:border-primary/35 hover:bg-muted/45 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
        !previous && "sm:text-right",
      )}
    >
      {previous ? (
        <ArrowLeft className="mt-1 h-4 w-4 shrink-0 text-primary transition group-hover:-translate-x-0.5" aria-hidden="true" />
      ) : null}
      <span className="min-w-0 flex-1">
        <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          {label}
        </span>
        <span className="mt-1 block font-semibold text-foreground">{document.title}</span>
        <span className="mt-1 line-clamp-2 block text-sm leading-6 text-muted-foreground">
          {document.summary}
        </span>
      </span>
      {!previous ? (
        <ArrowRight className="mt-1 h-4 w-4 shrink-0 text-primary transition group-hover:translate-x-0.5" aria-hidden="true" />
      ) : null}
    </Link>
  )
}

export function DocumentationReadingNavigation({
  document,
}: DocumentationReadingNavigationProps) {
  const { language, t } = useI18n()
  const documentationRoute = useDocumentationRouteScope()
  const readingContext = getDocumentationReadingContext(language, document.key)
  const hasSequenceLinks = Boolean(
    readingContext.previousDocument || readingContext.nextDocument,
  )

  if (!hasSequenceLinks && readingContext.relatedDocuments.length === 0) {
    return null
  }

  return (
    <div className="mt-12 border-t border-border pt-8">
      {hasSequenceLinks ? (
        <nav aria-label={t("documentation.readingNavigationAria")}>
          <h2 className="text-lg font-semibold text-foreground">
            {t("documentation.continueReading")}
          </h2>
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            {readingContext.previousDocument ? (
              <ReadingDirectionLink
                direction="previous"
                document={readingContext.previousDocument}
              />
            ) : (
              <div className="hidden sm:block" aria-hidden="true" />
            )}
            {readingContext.nextDocument ? (
              <ReadingDirectionLink direction="next" document={readingContext.nextDocument} />
            ) : null}
          </div>
        </nav>
      ) : null}

      {readingContext.relatedDocuments.length > 0 ? (
        <section className={hasSequenceLinks ? "mt-10" : undefined} aria-labelledby="documentation-related-documents">
          <div className="flex items-start gap-3">
            <div className="rounded-xl bg-primary/10 p-2 text-primary">
              <BookOpenText className="h-4 w-4" aria-hidden="true" />
            </div>
            <div>
              <h2 id="documentation-related-documents" className="text-lg font-semibold text-foreground">
                {t("documentation.relatedDocuments")}
              </h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {t("documentation.relatedDocumentsDescription")}
              </p>
            </div>
          </div>

          <div className="mt-4 grid gap-3 lg:grid-cols-3">
            {readingContext.relatedDocuments.map((relatedDocument) => (
              <Link
                key={relatedDocument.key}
                to={documentationRoute.documentHref(relatedDocument.key)}
                className="rounded-xl border border-border bg-card p-4 transition hover:border-primary/35 hover:bg-muted/45 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
              >
                <h3 className="font-semibold text-foreground">{relatedDocument.title}</h3>
                <p className="mt-1 line-clamp-3 text-sm leading-6 text-muted-foreground">
                  {relatedDocument.summary}
                </p>
              </Link>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  )
}
