import { ChevronDown, FileText, House } from "lucide-react"
import { useEffect, useRef, useState, type RefObject } from "react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { getDocumentationView } from "@/features/operator/docs/documentation-release-pack"
import { useDocumentationRouteScope } from "@/features/operator/docs/documentation-route-scope"
import type {
  DocumentationDocument,
  DocumentationNavigationGroup,
} from "@/features/operator/docs/docs.types"
import { cn } from "@/lib/utils"

type DocumentationNavigationTreeProps = Readonly<{
  currentDocumentId?: string
  activeScrollKey?: string | number | boolean
  onNavigate?: () => void
}>

type DocumentationNavigationGroupProps = Readonly<{
  group: DocumentationNavigationGroup
  documents: readonly DocumentationDocument[]
  currentDocumentId?: string
  initiallyOpen: boolean
  activeLinkRef: RefObject<HTMLAnchorElement | null>
  documentHref: (documentKey: string) => string
  onNavigate?: () => void
}>

function DocumentationNavigationGroupSection({
  group,
  documents,
  currentDocumentId,
  initiallyOpen,
  activeLinkRef,
  documentHref,
  onNavigate,
}: DocumentationNavigationGroupProps) {
  const [open, setOpen] = useState(initiallyOpen)

  return (
    <details
      className="group rounded-lg border border-transparent open:border-border open:bg-card/60"
      open={open}
      onToggle={(event) => setOpen(event.currentTarget.open)}
    >
      <summary className="flex cursor-pointer list-none items-center justify-between gap-2 rounded-lg px-2 py-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground transition hover:bg-muted hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring [&::-webkit-details-marker]:hidden">
        <span className="min-w-0">{group.label}</span>
        <span className="flex shrink-0 items-center gap-1.5">
          <span className="rounded-full bg-muted px-1.5 py-0.5 text-[0.65rem] font-medium text-muted-foreground">
            {documents.length}
          </span>
          <ChevronDown
            className="h-3.5 w-3.5 transition group-open:rotate-180"
            aria-hidden="true"
          />
        </span>
      </summary>

      <div className="space-y-1 px-1 pb-1">
        {documents.map((document) => {
          const active = document.id === currentDocumentId

          return (
            <Link
              key={document.id}
              ref={active ? activeLinkRef : undefined}
              to={documentHref(document.key)}
              aria-current={active ? "page" : undefined}
              onClick={onNavigate}
              className={cn(
                "flex items-start gap-2 rounded-lg px-2 py-2 text-sm transition",
                active
                  ? "bg-primary/10 font-medium text-foreground"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground",
              )}
            >
              <FileText className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              <span className="min-w-0 leading-5">{document.title}</span>
            </Link>
          )
        })}
      </div>
    </details>
  )
}

export function DocumentationNavigationTree({
  currentDocumentId,
  activeScrollKey,
  onNavigate,
}: DocumentationNavigationTreeProps) {
  const { language, t } = useI18n()
  const documentationRoute = useDocumentationRouteScope()
  const activeLinkRef = useRef<HTMLAnchorElement | null>(null)
  const documentationView = getDocumentationView(language)
  const activeDocument = currentDocumentId
    ? documentationView.documents.find((document) => document.id === currentDocumentId)
    : undefined

  useEffect(() => {
    if (!activeScrollKey) {
      return
    }

    activeLinkRef.current?.scrollIntoView({
      block: "center",
      inline: "nearest",
    })
  }, [activeScrollKey, currentDocumentId])

  return (
    <nav aria-label={t("documentation.contentsAria")} className="space-y-4">
      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          {t("documentation.contents")}
        </div>
        <p className="mt-1 text-xs leading-5 text-muted-foreground">
          {t("documentation.importedPack")}
        </p>
      </div>

      <Link
        ref={!currentDocumentId ? activeLinkRef : undefined}
        to={documentationRoute.rootHref()}
        aria-current={!currentDocumentId ? "page" : undefined}
        onClick={onNavigate}
        className={cn(
          "flex items-center gap-2 rounded-lg px-2 py-2 text-sm transition",
          !currentDocumentId
            ? "bg-primary/10 font-medium text-foreground"
            : "text-muted-foreground hover:bg-muted hover:text-foreground",
        )}
      >
        <House className="h-4 w-4 shrink-0" aria-hidden="true" />
        <span>{t("documentation.home.link")}</span>
      </Link>

      <div className="space-y-2">
        {documentationView.navigationGroups.map((group) => {
          const documents = documentationView.documents.filter(
            (document) => document.groupId === group.id,
          )
          const groupActive = activeDocument?.groupId === group.id

          return (
            <DocumentationNavigationGroupSection
              key={`${group.id}-${groupActive ? "active" : "inactive"}`}
              group={group}
              documents={documents}
              currentDocumentId={currentDocumentId}
              initiallyOpen={groupActive}
              activeLinkRef={activeLinkRef}
              documentHref={documentationRoute.documentHref}
              onNavigate={onNavigate}
            />
          )
        })}
      </div>
    </nav>
  )
}
