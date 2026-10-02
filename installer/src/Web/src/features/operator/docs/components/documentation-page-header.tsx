import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { DocumentationPackDetailsAction } from "@/features/operator/docs/components/documentation-pack-details-action"
import { DocumentationPackDownloadAction } from "@/features/operator/docs/components/documentation-pack-download-action"
import { getDocumentationReleasePack } from "@/features/operator/docs/documentation-release-pack"

export function DocumentationPageHeader() {
  const { language, t } = useI18n()
  const documentationReleasePack = getDocumentationReleasePack(language)

  return (
    <section className="flex flex-col gap-3 lg:flex-row lg:items-start lg:justify-between">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-xl font-semibold tracking-tight text-foreground sm:text-2xl">
            {t("documentation.title")}
          </p>
          <Badge variant="secondary">{t("documentation.localReader")}</Badge>
          {documentationReleasePack.reviewRequired ? (
            <Badge
              variant="outline"
              className="border-amber-500/45 bg-amber-500/10 text-amber-800 dark:text-amber-200"
            >
              {t("documentation.reviewRequired")}
            </Badge>
          ) : null}
        </div>
        <p className="mt-1 hidden max-w-3xl text-sm leading-6 text-muted-foreground sm:block">
          {t("documentation.description", { version: documentationReleasePack.version })}
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <DocumentationPackDetailsAction />
        <DocumentationPackDownloadAction compact />
      </div>
    </section>
  )
}
