import { formatDateTime, type DateTimeInput } from "@/app/formatters"
import type { UiLanguage } from "@/app/i18n/messages"

export function formatDiagnosticsLocalDateTime(
  value: DateTimeInput,
  language: UiLanguage,
  includeSeconds = false,
): string {
  return formatDateTime(value, language, {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    ...(includeSeconds ? { second: "2-digit" as const } : {}),
    timeZoneName: "short",
  })
}

export function diagnosticsUtcIso(value: DateTimeInput): string {
  const date = value instanceof Date ? value : new Date(value)
  if (Number.isNaN(date.getTime())) return String(value)
  return date.toISOString()
}
