import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"

type Props = {
  status: string | null | undefined
}

const statusTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  unknown: "stacks.status.unknown",
  passed: "stacks.status.passed",
  verified: "stacks.status.verified",
  "public routes verified": "stacks.status.publicRoutesVerified",
  created: "stacks.status.created",
  started: "stacks.status.started",
  failed: "stacks.status.failed",
  error: "stacks.status.error",
  completed: "stacks.status.completed",
  "in progress": "stacks.status.inProgress",
  running: "stacks.status.running",
  ready: "stacks.status.ready",
  healthy: "stacks.status.healthy",
  "needs attention": "stacks.status.needsAttention",
  offline: "stacks.status.offline",
  "setting up": "stacks.status.settingUp",
}

function normaliseStatus(status: string | null | undefined) {
  return (status ?? "unknown")
    .trim()
    .toLowerCase()
    .replaceAll("_", " ")
    .replaceAll("-", " ")
    .replace(/\s+/g, " ")
}

export function stackStatusLabel(status: string | null | undefined, t: (key: TranslationKey) => string) {
  const normalised = normaliseStatus(status)
  const translationKey = statusTranslationKeys[normalised]

  return translationKey ? t(translationKey) : (status ?? "unknown").replaceAll("_", " ")
}

export function StackStatusPill({ status }: Props) {
  const { language, t } = useI18n()
  const normalized = normaliseStatus(status)
  const good =
    normalized.includes("verified") ||
    normalized === "passed" ||
    normalized === "completed" ||
    normalized === "ready" ||
    normalized === "healthy"
  const warning =
    normalized.includes("created") ||
    normalized.includes("started") ||
    normalized === "in progress" ||
    normalized === "running" ||
    normalized === "setting up" ||
    normalized === "needs attention"
  const failed =
    normalized.includes("failed") ||
    normalized.includes("error") ||
    normalized === "offline"

  return (
    <Badge
      variant="outline"
      className={cn(
        language === "en" && "capitalize",
        good && "border-emerald-500/30 bg-emerald-500/10 text-emerald-300",
        warning && "border-amber-500/30 bg-amber-500/10 text-amber-300",
        failed && "border-red-500/30 bg-red-500/10 text-red-300",
      )}
    >
      {stackStatusLabel(status, t)}
    </Badge>
  )
}
