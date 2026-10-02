import { Info } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { getDocumentationReleasePack } from "@/features/operator/docs/documentation-release-pack"

export function DocumentationPackStatusStrip() {
  const { language, t } = useI18n()
  const documentationReleasePack = getDocumentationReleasePack(language)

  return (
    <section
      className="mt-8 flex flex-col gap-3 rounded-xl border border-primary/25 bg-primary/5 px-4 py-3 text-sm sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-5"
      aria-label={t("documentation.packStatus")}
    >
      <div className="flex items-center gap-2 font-medium text-foreground">
        <Info className="h-4 w-4 text-primary" aria-hidden="true" />
        <span>{t("documentation.packFooterLocal")}</span>
      </div>
      <span className="text-muted-foreground">
        {t("documentation.packFooterRelease", { version: documentationReleasePack.version })}
      </span>
      <span className="text-muted-foreground">
        {t("documentation.packFooterDocuments", { count: documentationReleasePack.documentCount })}
      </span>
      <span className="text-muted-foreground">
        {t("documentation.packFooterDownloadFiles", {
          count: documentationReleasePack.downloadFileCount,
        })}
      </span>
      {documentationReleasePack.reviewRequired ? (
        <span className="font-medium text-amber-800 dark:text-amber-200">
          {t("documentation.packReviewRequired")}
        </span>
      ) : null}
    </section>
  )
}
