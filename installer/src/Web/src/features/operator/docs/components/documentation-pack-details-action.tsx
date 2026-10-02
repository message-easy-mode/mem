import { Info } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { getDocumentationReleasePack } from "@/features/operator/docs/documentation-release-pack"

export function DocumentationPackDetailsAction() {
  const { language, t } = useI18n()
  const documentationReleasePack = getDocumentationReleasePack(language)

  return (
    <details className="relative">
      <summary className="flex h-7 cursor-pointer list-none items-center gap-1 rounded-[min(var(--radius-md),12px)] border border-border bg-background px-2.5 text-[0.8rem] font-medium text-foreground transition hover:bg-muted focus-visible:outline-none focus-visible:ring-3 focus-visible:ring-ring/50 [&::-webkit-details-marker]:hidden dark:bg-input/30 dark:hover:bg-input/50">
        <Info className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{t("documentation.packDetails")}</span>
      </summary>

      <div className="absolute right-0 z-20 mt-2 w-80 max-w-[calc(100vw-2rem)] rounded-xl border border-border bg-popover p-4 shadow-lg">
        <p className="text-sm font-semibold text-foreground">{t("documentation.packDetails")}</p>
        <p className="mt-1 text-sm leading-6 text-muted-foreground">
          {t("documentation.packDetailsDescription", {
            count: documentationReleasePack.documentCount,
          })}
        </p>

        <dl className="mt-4 grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-2 border-t border-border pt-3 text-sm">
          <dt className="text-muted-foreground">{t("documentation.packVersion")}</dt>
          <dd className="font-medium text-foreground">v{documentationReleasePack.version}</dd>
          <dt className="text-muted-foreground">{t("documentation.packDocuments")}</dt>
          <dd className="font-medium text-foreground">{documentationReleasePack.documentCount}</dd>
          <dt className="text-muted-foreground">{t("documentation.packDownloadFiles")}</dt>
          <dd className="font-medium text-foreground">{documentationReleasePack.downloadFileCount}</dd>
          <dt className="text-muted-foreground">{t("documentation.packReviewStatus")}</dt>
          <dd className="font-medium text-amber-800 dark:text-amber-200">
            {documentationReleasePack.reviewRequired
              ? t("documentation.packReviewRequired")
              : t("documentation.localReader")}
          </dd>
        </dl>
      </div>
    </details>
  )
}
