/** Presentation helpers for Migration API fields explicitly named *Utc.
 * Offset-less ISO timestamps in those fields still denote UTC, not browser-local
 * wall time. Explicit offsets are honoured. No recorded value is modified.
 */
export function migrationUtcIso(valueUtc: string | null | undefined): string | null {
  if (!valueUtc) return null
  const value = valueUtc.trim()
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(Z|[+-](?:[01]\d|2[0-3]):[0-5]\d)?$/.exec(value)
  if (!match) return null
  const [, year, month, day, hour, minute, second, fraction, zone] = match
  if (Number(year) < 1 || Number(month) < 1 || Number(month) > 12 ||
      Number(day) < 1 || Number(hour) > 23 || Number(minute) > 59 || Number(second) > 59) return null

  // Reject impossible dates rather than allowing Date's day/month rollover.
  const calendar = new Date(`${year}-${month}-${day}T00:00:00Z`)
  if (!Number.isFinite(calendar.getTime()) || calendar.getUTCDate() !== Number(day)) return null
  const date = new Date(zone ? value : `${value}Z`)
  if (!Number.isFinite(date.getTime()) || date.getUTCFullYear() < 1 || date.getUTCFullYear() > 9999) return null
  // Date is millisecond-precision; keep the recorded sub-millisecond digits in
  // technical UTC evidence rather than silently truncating .NET's precision.
  return `${date.toISOString().slice(0, 19)}.${(fraction ?? "").padEnd(3, "0")}Z`
}

export function formatMigrationDateTime(
  valueUtc: string | null | undefined,
  locale: string,
  fallback = "—",
  timeZone?: string,
): string {
  const utc = migrationUtcIso(valueUtc)
  if (!utc) return fallback
  return new Intl.DateTimeFormat(locale, {
    year: "numeric", month: "short", day: "numeric",
    hour: "2-digit", minute: "2-digit", hourCycle: "h23",
    timeZoneName: "shortOffset",
    ...(timeZone ? { timeZone } : {}),
  }).format(new Date(utc))
}

export function migrationDisplayTimeZone(): string {
  return new Intl.DateTimeFormat().resolvedOptions().timeZone
}
