// src/components/layout/page-breadcrumbs.tsx

import { Link } from "react-router-dom"
import { ChevronRight } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"

type BreadcrumbItem = {
  label: string
  to?: string
}

export function PageBreadcrumbs({ items }: { items: BreadcrumbItem[] }) {
  const { t } = useI18n()

  if (items.length === 0) {
    return null
  }

  return (
    <nav
      aria-label={t("navigation.breadcrumbsAria")}
      className="mb-2 flex flex-wrap items-center gap-1 text-sm text-muted-foreground"
    >
      {items.map((item, index) => {
        const isLast = index === items.length - 1

        return (
          <span key={`${item.label}-${index}`} className="flex items-center gap-1">
            {item.to && !isLast ? (
              <Link
                to={item.to}
                className="hover:text-foreground hover:underline"
              >
                {item.label}
              </Link>
            ) : (
              <span className={isLast ? "text-foreground" : undefined}>
                {item.label}
              </span>
            )}

            {!isLast ? <ChevronRight className="h-3.5 w-3.5" /> : null}
          </span>
        )
      })}
    </nav>
  )
}