import { useI18n } from "@/app/i18n/i18n-context"
import { installationStatusTranslationKey } from "@/features/setup/setup-status-i18n"
import { cn } from "@/lib/utils"
import {
  getInstallationStatusTone,
  type InstallationStatus,
} from "../api/install.types"

export function InstallStatusPill({ status }: { status: InstallationStatus | string }) {
  const { t } = useI18n()
  const typedStatus = status as InstallationStatus
  const tone = getInstallationStatusTone(typedStatus)

  const className =
    tone === "success"
      ? "border-primary/30 bg-primary/10 text-primary"
      : tone === "danger"
        ? "border-destructive/30 bg-destructive/10 text-destructive"
        : tone === "warning"
          ? "border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300"
          : tone === "info"
            ? "border-sky-500/30 bg-sky-500/10 text-sky-700 dark:text-sky-300"
            : "border-border bg-background text-muted-foreground"

  return (
    <span
      role="status"
      aria-live="polite"
      className={cn(
        "inline-flex rounded-full border px-3 py-1 text-xs font-medium",
        className,
      )}
    >
      {t(installationStatusTranslationKey(typedStatus))}
    </span>
  )
}
