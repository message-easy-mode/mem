import { ListTree } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { DocumentationHeading } from "@/features/operator/docs/docs.types"
import { cn } from "@/lib/utils"

export function DocumentationOnThisPage({ headings }: { headings: readonly DocumentationHeading[] }) {
  const { t } = useI18n()

  return (
    <nav aria-label={t("documentation.onThisPageAria")}>
      <div className="flex items-center gap-2 text-sm font-semibold text-foreground">
        <ListTree className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
        {t("documentation.onThisPage")}
      </div>

      {headings.length === 0 ? (
        <p className="mt-3 text-sm text-muted-foreground">{t("documentation.noHeadings")}</p>
      ) : (
        <ol className="mt-3 space-y-2 border-l border-border">
          {headings.map((heading) => (
            <li key={heading.id} className={cn(heading.level === 3 && "pl-3", heading.level === 2 && "pl-2")}>
              <a
                href={`#${heading.id}`}
                className="block text-sm leading-5 text-muted-foreground hover:text-foreground"
              >
                {heading.label}
              </a>
            </li>
          ))}
        </ol>
      )}
    </nav>
  )
}
