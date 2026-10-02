import {
  ArrowLeft,
  ArrowRightLeft,
  BookOpenText,
  Boxes,
  ChevronDown,
  LifeBuoy,
  ListTree,
  LogIn,
  PackageCheck,
  RotateCcw,
  ServerCog,
  ShieldCheck,
  SquareTerminal,
  TriangleAlert,
  Wrench,
} from "lucide-react"
import { Link, useLocation } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey, UiLanguage } from "@/app/i18n/messages"
import { DocumentationNavigationTree } from "@/features/operator/docs/components/documentation-navigation-tree"
import { DocumentationPackStatusStrip } from "@/features/operator/docs/components/documentation-pack-status-strip"
import { DocumentationPageHeader } from "@/features/operator/docs/components/documentation-page-header"
import { DocumentationSearch } from "@/features/operator/docs/components/documentation-search"
import { getDocumentationReleasePack, getDocumentationView } from "@/features/operator/docs/documentation-release-pack"
import { useDocumentationRouteScope } from "@/features/operator/docs/documentation-route-scope"
import { cn } from "@/lib/utils"

type DocumentationTaskIcon =
  | "architecture"
  | "backup"
  | "cli"
  | "host"
  | "install"
  | "login"
  | "migrate"
  | "operate"
  | "restore"
  | "security"
  | "troubleshoot"

type DocumentationTask = Readonly<{
  id: string
  documentKey: string
  icon: DocumentationTaskIcon
  titleKey: TranslationKey
  descriptionKey: TranslationKey
}>

const documentationTasksByLanguage: Readonly<Record<UiLanguage, readonly DocumentationTask[]>> = {
  en: [
    { id: "what-is-mem", documentKey: "start/what-is-mem", icon: "architecture", titleKey: "documentation.home.task.whatIsMem.title", descriptionKey: "documentation.home.task.whatIsMem.description" },
    { id: "fit", documentKey: "start/is-mem-right-for-you", icon: "operate", titleKey: "documentation.home.task.fit.title", descriptionKey: "documentation.home.task.fit.description" },
    { id: "install-or-migrate", documentKey: "start/install-or-migrate", icon: "install", titleKey: "documentation.home.task.installOrMigrate.title", descriptionKey: "documentation.home.task.installOrMigrate.description" },
    { id: "migrate", documentKey: "migrate", icon: "migrate", titleKey: "documentation.home.task.migrate.title", descriptionKey: "documentation.home.task.migrate.description" },
    { id: "create-chat-server", documentKey: "chat-servers/create", icon: "operate", titleKey: "documentation.home.task.createChatServer.title", descriptionKey: "documentation.home.task.createChatServer.description" },
    { id: "backup-restore", documentKey: "backups-restores", icon: "backup", titleKey: "documentation.home.task.backupRestore.title", descriptionKey: "documentation.home.task.backupRestore.description" },
    { id: "requirements", documentKey: "start/requirements", icon: "host", titleKey: "documentation.home.task.requirements.title", descriptionKey: "documentation.home.task.requirements.description" },
    { id: "packages", documentKey: "start/packages", icon: "cli", titleKey: "documentation.home.task.packages.title", descriptionKey: "documentation.home.task.packages.description" },
    { id: "cli", documentKey: "cli", icon: "cli", titleKey: "documentation.home.task.cli.title", descriptionKey: "documentation.home.task.cli.description" },
    { id: "limitations", documentKey: "start/known-limitations", icon: "troubleshoot", titleKey: "documentation.home.task.limitations.title", descriptionKey: "documentation.home.task.limitations.description" },
  ],
  de: [
    { id: "what-is-mem", documentKey: "start/what-is-mem", icon: "architecture", titleKey: "documentation.home.task.whatIsMem.title", descriptionKey: "documentation.home.task.whatIsMem.description" },
    { id: "fit", documentKey: "start/is-mem-right-for-you", icon: "operate", titleKey: "documentation.home.task.fit.title", descriptionKey: "documentation.home.task.fit.description" },
    { id: "install-or-migrate", documentKey: "start/install-or-migrate", icon: "install", titleKey: "documentation.home.task.installOrMigrate.title", descriptionKey: "documentation.home.task.installOrMigrate.description" },
    { id: "migrate", documentKey: "migrate", icon: "migrate", titleKey: "documentation.home.task.migrate.title", descriptionKey: "documentation.home.task.migrate.description" },
    { id: "create-chat-server", documentKey: "chat-servers/create", icon: "operate", titleKey: "documentation.home.task.createChatServer.title", descriptionKey: "documentation.home.task.createChatServer.description" },
    { id: "backup-restore", documentKey: "backups-restores", icon: "backup", titleKey: "documentation.home.task.backupRestore.title", descriptionKey: "documentation.home.task.backupRestore.description" },
    { id: "requirements", documentKey: "start/requirements", icon: "host", titleKey: "documentation.home.task.requirements.title", descriptionKey: "documentation.home.task.requirements.description" },
    { id: "packages", documentKey: "start/packages", icon: "cli", titleKey: "documentation.home.task.packages.title", descriptionKey: "documentation.home.task.packages.description" },
    { id: "cli", documentKey: "cli", icon: "cli", titleKey: "documentation.home.task.cli.title", descriptionKey: "documentation.home.task.cli.description" },
    { id: "limitations", documentKey: "start/known-limitations", icon: "troubleshoot", titleKey: "documentation.home.task.limitations.title", descriptionKey: "documentation.home.task.limitations.description" },
  ],
}

const setupDocumentationKeysByStage = {
  start: [
    "installation/private-access-and-setup-code",
    "installation/first-platform-owner",
    "installation/check-server",
  ],
  "check-server": [
    "installation/check-server",
    "installation/prepare-host",
    "installation/troubleshooting",
  ],
  domain: [
    "installation/domain-and-certificate",
    "installation/troubleshooting",
  ],
  review: [
    "installation/review-and-install",
    "installation/domain-and-certificate",
  ],
  install: [
    "installation/review-and-install",
    "installation/troubleshooting",
  ],
  verify: [
    "installation/verify-and-handoff",
    "installation/troubleshooting",
  ],
  finish: [
    "installation/verify-and-handoff",
    "installation/troubleshooting",
  ],
} as const

type SetupDocumentationStage = keyof typeof setupDocumentationKeysByStage

function getSetupDocumentationStage(
  returnTo: string | undefined,
): SetupDocumentationStage {
  if (!returnTo) {
    return "start"
  }

  const pathname = returnTo.split(/[?#]/, 1)[0]

  if (pathname.startsWith("/setup/check-server")) return "check-server"
  if (pathname.startsWith("/setup/domain")) return "domain"
  if (pathname.startsWith("/setup/review")) return "review"
  if (pathname.startsWith("/setup/install")) return "install"
  if (pathname.startsWith("/setup/verify")) return "verify"
  if (pathname.startsWith("/setup/handoff")) return "finish"

  return "start"
}

function DocumentationTaskIcon({ icon }: { icon: DocumentationTaskIcon }) {
  const className = "h-5 w-5"

  switch (icon) {
    case "architecture":
      return <ListTree className={className} aria-hidden="true" />
    case "backup":
      return <PackageCheck className={className} aria-hidden="true" />
    case "cli":
      return <SquareTerminal className={className} aria-hidden="true" />
    case "host":
      return <ServerCog className={className} aria-hidden="true" />
    case "install":
      return <Wrench className={className} aria-hidden="true" />
    case "login":
      return <LogIn className={className} aria-hidden="true" />
    case "migrate":
      return <ArrowRightLeft className={className} aria-hidden="true" />
    case "operate":
      return <Boxes className={className} aria-hidden="true" />
    case "restore":
      return <RotateCcw className={className} aria-hidden="true" />
    case "security":
      return <ShieldCheck className={className} aria-hidden="true" />
    case "troubleshoot":
      return <LifeBuoy className={className} aria-hidden="true" />
    default:
      return <LifeBuoy className={className} aria-hidden="true" />
  }
}

export function DocumentationHomePage() {
  const { language, t } = useI18n()
  const location = useLocation()
  const documentationRoute = useDocumentationRouteScope()
  const documentationView = getDocumentationView(language)
  const documentationReleasePack = getDocumentationReleasePack(language)
  const translationUnavailable =
    location.state &&
    typeof location.state === "object" &&
    "documentationNotice" in location.state &&
    location.state.documentationNotice === "translation-unavailable" &&
    "language" in location.state &&
    location.state.language === language
  const tasks = documentationTasksByLanguage[language].flatMap((task) => {
    const document = documentationView.documentByKey.get(task.documentKey)
    return document ? [{ ...task, document }] : []
  })
  const setupStage = getSetupDocumentationStage(documentationRoute.returnTo)
  const setupRecommendedDocuments = documentationRoute.setupMode
    ? setupDocumentationKeysByStage[setupStage].flatMap((documentKey) => {
        const document = documentationView.documentByKey.get(documentKey)
        return document ? [document] : []
      })
    : []

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
          <div className="mt-3 rounded-xl border border-primary/25 bg-primary/5 px-4 py-3 text-sm">
            <p className="font-medium text-foreground">
              {t("documentation.setup.contextTitle")}
            </p>
            <p className="mt-1 text-muted-foreground">
              {t("documentation.setup.contextDescription")}
            </p>
          </div>
        </div>
      ) : null}

      <div
        className={cn(
          "sticky top-16 z-20 -mx-4 mb-7 border-b border-border bg-background/95 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6 lg:-mx-8 lg:px-8 2xl:-mx-10 2xl:px-10",
          documentationRoute.setupMode ? "mt-0 sm:mt-0" : "-mt-6 sm:-mt-8",
        )}
      >
        <div className="mx-auto w-full max-w-[1760px]">
          <DocumentationPageHeader />
          <div className="mt-3 border-t border-border pt-3">
            <DocumentationSearch className="mb-0" />
          </div>
          <details className="mt-3 rounded-xl border border-border bg-card px-3 py-2 xl:hidden">
            <summary className="flex cursor-pointer list-none items-center justify-between gap-3 font-medium text-foreground [&::-webkit-details-marker]:hidden">
              <span className="flex items-center gap-2">
                <BookOpenText className="h-4 w-4" aria-hidden="true" />
                {t("documentation.contents")}
              </span>
              <ChevronDown className="h-4 w-4" aria-hidden="true" />
            </summary>
            <div className="mt-3 max-h-[40vh] overflow-y-auto border-t border-border pt-3">
              <DocumentationNavigationTree />
            </div>
          </details>
        </div>
      </div>

      <div className="grid gap-8 xl:grid-cols-[15.5rem_minmax(0,1fr)] xl:items-start">
        <aside className="sticky top-[14rem] hidden max-h-[calc(100vh-15rem)] overflow-y-auto border-r border-border pr-5 xl:block">
          <DocumentationNavigationTree />
        </aside>

        <main className="min-w-0">
          <section className="rounded-2xl border border-border bg-gradient-to-br from-card to-muted/40 p-6 sm:p-8">
            <div className="flex items-start gap-4">
              <div className="rounded-2xl bg-primary/10 p-3 text-primary">
                <BookOpenText className="h-6 w-6" aria-hidden="true" />
              </div>
              <div className="min-w-0">
                <p className="text-sm font-medium text-primary">{t("documentation.home.eyebrow")}</p>
                <h1 className="mt-1 text-3xl font-semibold tracking-tight text-foreground">
                  {t("documentation.home.title")}
                </h1>
                <p className="mt-3 max-w-3xl text-base leading-7 text-muted-foreground">
                  {t("documentation.home.description")}
                </p>
              </div>
            </div>
          </section>

          {translationUnavailable ? (
            <aside
              className="mt-6 flex gap-3 rounded-xl border border-primary/30 bg-primary/5 p-4 text-sm leading-6 text-foreground"
              role="status"
            >
              <BookOpenText className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
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
                <p className="mt-1 text-muted-foreground">
                  {t("documentation.reviewRequiredDescription")}
                </p>
              </div>
            </aside>
          ) : null}

          {documentationRoute.setupMode && setupRecommendedDocuments.length > 0 ? (
            <section
              className="mt-8"
              aria-labelledby="documentation-setup-recommended"
            >
              <div>
                <h2
                  id="documentation-setup-recommended"
                  className="text-xl font-semibold text-foreground"
                >
                  {t("documentation.setup.recommendedTitle")}
                </h2>
                <p className="mt-1 text-sm leading-6 text-muted-foreground">
                  {t("documentation.setup.recommendedDescription")}
                </p>
              </div>

              <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                {setupRecommendedDocuments.map((document) => (
                  <Link
                    key={document.id}
                    to={documentationRoute.documentHref(document.key)}
                    className="rounded-xl border border-primary/25 bg-primary/5 p-4 transition hover:border-primary/45 hover:bg-primary/10 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
                  >
                    <h3 className="font-semibold text-foreground">
                      {document.title}
                    </h3>
                    <p className="mt-1 line-clamp-3 text-sm leading-6 text-muted-foreground">
                      {document.summary}
                    </p>
                  </Link>
                ))}
              </div>
            </section>
          ) : null}

          <section className="mt-8" aria-labelledby="documentation-home-tasks">
            <div>
              <h2 id="documentation-home-tasks" className="text-xl font-semibold text-foreground">
                {t("documentation.home.tasksTitle")}
              </h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {t("documentation.home.tasksDescription")}
              </p>
            </div>

            <div className="mt-4 grid gap-3 md:grid-cols-2 2xl:grid-cols-3">
              {tasks.map((task) => (
                <Link
                  key={task.id}
                  to={documentationRoute.documentHref(task.document.key)}
                  className="group rounded-xl border border-border bg-card p-4 transition hover:border-primary/35 hover:bg-muted/45 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
                >
                  <div className="flex items-start gap-3">
                    <div className="rounded-xl bg-primary/10 p-2.5 text-primary transition group-hover:bg-primary/15">
                      <DocumentationTaskIcon icon={task.icon} />
                    </div>
                    <div className="min-w-0">
                      <h3 className="font-semibold text-foreground">{t(task.titleKey)}</h3>
                      <p className="mt-1 text-sm leading-6 text-muted-foreground">
                        {t(task.descriptionKey)}
                      </p>
                    </div>
                  </div>
                </Link>
              ))}
            </div>
          </section>

          <section className="mt-10" aria-labelledby="documentation-home-sections">
            <div>
              <h2 id="documentation-home-sections" className="text-xl font-semibold text-foreground">
                {t("documentation.home.sectionsTitle")}
              </h2>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {t("documentation.home.sectionsDescription")}
              </p>
            </div>

            <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {documentationView.navigationGroups.map((group) => {
                const documents = documentationView.documents.filter(
                  (document) => document.groupId === group.id,
                )
                const firstDocument = documents[0]

                if (!firstDocument) {
                  return null
                }

                return (
                  <Link
                    key={group.id}
                    to={documentationRoute.documentHref(firstDocument.key)}
                    className={cn(
                      "rounded-xl border border-border bg-card px-4 py-3 transition",
                      "hover:border-primary/35 hover:bg-muted/45 focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
                    )}
                  >
                    <div className="flex items-center justify-between gap-3">
                      <span className="font-medium text-foreground">{group.label}</span>
                      <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
                        {t("documentation.home.sectionCount", { count: documents.length })}
                      </span>
                    </div>
                    <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted-foreground">
                      {firstDocument.summary}
                    </p>
                  </Link>
                )
              })}
            </div>
          </section>
        </main>
      </div>

      <DocumentationPackStatusStrip />
    </div>
  )
}
