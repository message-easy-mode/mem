import { CalendarClock, Clock3, FileText, Tags } from "lucide-react"

import { formatDate } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { documentationReleasePack } from "@/features/operator/docs/documentation-release-pack"
import type { DocumentationDocument } from "@/features/operator/docs/docs.types"

export function DocumentationDocumentMeta({ document }: { document: DocumentationDocument }) {
  const { language, t } = useI18n()

  return (
    <section className="mt-8 border-t border-border pt-5" aria-labelledby="documentation-meta-heading">
      <h2 id="documentation-meta-heading" className="text-sm font-semibold text-foreground">
        {t("documentation.documentDetails")}
      </h2>
      <dl className="mt-3 space-y-3 text-sm">
        <div className="flex items-start gap-2 text-muted-foreground">
          <Clock3 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div>
            <dt className="sr-only">{t("documentation.estimatedReadTime")}</dt>
            <dd>{t("documentation.estimatedReadTime", { count: document.estimatedReadMinutes })}</dd>
          </div>
        </div>
        <div className="flex items-start gap-2 text-muted-foreground">
          <FileText className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div>
            <dt className="sr-only">{t("documentation.sourcePath")}</dt>
            <dd className="break-all font-mono text-xs leading-5 text-foreground/85">{document.sourcePath}</dd>
          </div>
        </div>
        <div className="flex items-start gap-2 text-muted-foreground">
          <Tags className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div>
            <dt className="sr-only">{t("documentation.tags")}</dt>
            <dd className="flex flex-wrap gap-1.5">
              {document.tags.length > 0 ? (
                document.tags.map((tag) => (
                  <Badge key={tag} variant="outline">
                    {tag}
                  </Badge>
                ))
              ) : (
                <span>{t("documentation.noTags")}</span>
              )}
            </dd>
          </div>
        </div>
        <div className="flex items-start gap-2 text-muted-foreground">
          <CalendarClock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <div>
            <dt className="sr-only">{t("documentation.packGenerated")}</dt>
            <dd>{formatDate(documentationReleasePack.generatedAtUtc, language)}</dd>
          </div>
        </div>
      </dl>
    </section>
  )
}
