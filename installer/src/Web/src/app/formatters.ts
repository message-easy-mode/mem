import { getIntlLocale } from "@/app/i18n/i18n-core"
import type { UiLanguage } from "@/app/i18n/messages"

export type DateTimeInput = Date | string | number

function asDate(value: DateTimeInput): Date {
  const date = value instanceof Date ? value : new Date(value)

  if (Number.isNaN(date.getTime())) {
    throw new Error("Expected a valid date value.")
  }

  return date
}

export function formatNumber(
  value: number,
  language: UiLanguage,
  options: Intl.NumberFormatOptions = {},
): string {
  return new Intl.NumberFormat(getIntlLocale(language), options).format(value)
}

export function formatDateTime(
  value: DateTimeInput,
  language: UiLanguage,
  options: Intl.DateTimeFormatOptions = {
    dateStyle: "medium",
    timeStyle: "short",
  },
): string {
  return new Intl.DateTimeFormat(getIntlLocale(language), options).format(asDate(value))
}

export function formatDate(
  value: DateTimeInput,
  language: UiLanguage,
  options: Intl.DateTimeFormatOptions = { dateStyle: "medium" },
): string {
  return new Intl.DateTimeFormat(getIntlLocale(language), options).format(asDate(value))
}

export function formatBytes(value: number, language: UiLanguage): string {
  if (!Number.isFinite(value) || value < 0) {
    throw new Error("Expected a finite, non-negative byte value.")
  }

  const units = ["B", "KiB", "MiB", "GiB", "TiB"]

  if (value === 0) {
    return `0 ${units[0]}`
  }

  const unitIndex = Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1)
  const scaledValue = value / 1024 ** unitIndex
  const maximumFractionDigits = scaledValue >= 10 || unitIndex === 0 ? 0 : 1

  return `${formatNumber(scaledValue, language, { maximumFractionDigits })} ${units[unitIndex]}`
}
