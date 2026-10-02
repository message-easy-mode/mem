import { Server } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { MigrationConversionSourceStack } from "@/features/operator/migrations/api/migration-conversions"

export function MigrationBoundSourceStack({
  stack,
}: {
  stack: MigrationConversionSourceStack
}) {
  const { t } = useI18n()

  return (
    <div className="rounded-xl border bg-muted/15 p-4 sm:p-5" aria-label={t("migrationWorkspace.prepare.boundSourceStackTitle")}>
      <div className="flex items-start gap-3">
        <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
          <Server className="h-5 w-5" aria-hidden="true" />
        </span>
        <div className="min-w-0 flex-1">
          <h3 className="font-semibold">{t("migrationWorkspace.prepare.boundSourceStackTitle")}</h3>
          <p className="mt-1 text-sm leading-6 text-muted-foreground">
            {t("migrationWorkspace.prepare.boundSourceStackDescription")}
          </p>
          <dl className="mt-3 grid gap-4 text-sm xl:grid-cols-[minmax(0,0.7fr)_minmax(0,1.3fr)_minmax(0,1.4fr)]">
            <div className="min-w-0">
              <dt className="text-muted-foreground">{t("migrationWorkspace.prepare.boundSourceStackSlug")}</dt>
              <dd className="mt-1 font-medium">{stack.slug}</dd>
            </div>
            <div className="min-w-0">
              <dt className="text-muted-foreground">{t("migrationWorkspace.prepare.boundMatrixServer")}</dt>
              <dd className="mt-1 break-words font-medium [overflow-wrap:anywhere]">{stack.matrixServerName}</dd>
            </div>
            <div className="min-w-0">
              <dt className="text-muted-foreground">{t("migrationWorkspace.prepare.sourceStackId")}</dt>
              <dd className="mt-1 break-words font-mono text-xs [overflow-wrap:anywhere]">{stack.sourceStackId}</dd>
            </div>
          </dl>
        </div>
      </div>
    </div>
  )
}
