import { formatDateTime, formatNumber } from "@/app/formatters"
import type { UiLanguage } from "@/app/i18n/messages"

const UTC_DATE_TIME_WITHOUT_OFFSET = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d{1,7})?)?$/

function normalizeUtcDateTime(value: string) {
  // SQLite returns persisted DateTime values with Kind=Unspecified. System.Text.Json
  // consequently emits those UTC-named fields without a trailing Z. Browsers then
  // interpret them as local wall-clock values, shifting neither the date nor time.
  // Treat only an ISO date-time with no offset as UTC; preserve explicit Z/offset input.
  return UTC_DATE_TIME_WITHOUT_OFFSET.test(value) ? `${value}Z` : value
}

export function formatDate(value: string | null | undefined, language?: UiLanguage) {
  if (!value) return "unknown"

  const normalizedValue = normalizeUtcDateTime(value)

  if (!language) {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(normalizedValue))
  }

  try {
    return formatDateTime(normalizedValue, language)
  } catch {
    return value
  }
}

export function formatBytes(value: number | null | undefined, language?: UiLanguage) {
  const bytes = value ?? 0

  if (bytes < 1024) {
    return `${bytes} B`
  }

  const units = ["KB", "MB", "GB", "TB"]
  let size = bytes / 1024
  let unitIndex = 0

  while (size >= 1024 && unitIndex < units.length - 1) {
    size /= 1024
    unitIndex++
  }

  const fractionDigits = size >= 10 ? 1 : 2
  const formattedSize = language
    ? formatNumber(size, language, {
        minimumFractionDigits: fractionDigits,
        maximumFractionDigits: fractionDigits,
      })
    : size.toFixed(fractionDigits)

  return `${formattedSize} ${units[unitIndex]}`
}

export function saveBlob(blob: Blob, downloadName: string) {
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement("a")

  anchor.href = objectUrl
  anchor.download = downloadName
  anchor.style.display = "none"

  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()

  window.setTimeout(() => {
    URL.revokeObjectURL(objectUrl)
  }, 1000)
}
