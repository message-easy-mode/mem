import { describe, expect, it } from "vitest"

import { formatMigrationDateTime, migrationDisplayTimeZone, migrationUtcIso } from "./migration-time"

const instant = "2026-09-21T04:44:49Z"
const options: Intl.DateTimeFormatOptions = {
  year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit",
  hourCycle: "h23", timeZoneName: "shortOffset",
}

describe("Migration UTC presentation", () => {
  it.each([
    ["2026-09-21T04:44:49Z", "2026-09-21T04:44:49.000Z"],
    ["2026-09-21T04:44:49", "2026-09-21T04:44:49.000Z"],
    ["2026-09-21T16:44:49+12:00", "2026-09-21T04:44:49.000Z"],
    ["2026-09-20T21:44:49-07:00", "2026-09-21T04:44:49.000Z"],
    ["2026-09-21T04:44:49.1234567", "2026-09-21T04:44:49.1234567Z"],
    ["2024-02-29T12:34:56+00:00", "2024-02-29T12:34:56.000Z"],
  ])("reads %s as the recorded instant, without applying a local offset twice", (value, expected) => {
    expect(migrationUtcIso(value)).toBe(expected)
  })

  it.each([null, undefined, "", "not-a-date", "2026-02-30T00:00:00Z", "2026-09-31T00:00:00Z",
    "2026-00-12T00:00:00Z", "2026-13-12T00:00:00Z", "2026-09-21T24:01:00Z", "2026-09-21T00:60:00Z",
    "2026-09-21", "21/09/2026 04:44", "2026-09-21T04:44:49+99:00", "2026-09-21T04:44:49+12:99",
  ])("keeps missing or invalid UTC evidence neutral: %s", (value) => {
    expect(migrationUtcIso(value)).toBeNull()
    expect(formatMigrationDateTime(value, "en-NZ")).toBe("—")
    expect(formatMigrationDateTime(value, "de-DE", "Nicht erfasst")).toBe("Nicht erfasst")
  })

  describe.each(["en-NZ", "de-DE"])("%s", (locale) => {
    it.each(["UTC", "Pacific/Auckland", "Europe/Berlin", "America/Los_Angeles"])(
      "formats the same UTC instant in %s with an explicit offset", (timeZone) => {
        const expected = new Intl.DateTimeFormat(locale, { ...options, timeZone }).format(new Date(instant))
        expect(formatMigrationDateTime(instant, locale, "—", timeZone)).toBe(expected)
        expect(formatMigrationDateTime("2026-09-21T04:44:49", locale, "—", timeZone)).toBe(expected)
        expect(formatMigrationDateTime("2026-09-21T16:44:49+12:00", locale, "—", timeZone)).toBe(expected)
      },
    )

    it("uses browser-local time by default, not a fixed offset or the UI language's country", () => {
      expect(migrationDisplayTimeZone()).toBe(new Intl.DateTimeFormat().resolvedOptions().timeZone)
      expect(formatMigrationDateTime(instant, locale)).toBe(new Intl.DateTimeFormat(locale, options).format(new Date(instant)))
    })

    it("follows date boundaries and daylight-saving changes", () => {
      for (const value of ["2026-09-21T23:30:00Z", "2026-09-26T13:30:00Z", "2026-09-26T14:30:00Z"]) {
        expect(formatMigrationDateTime(value, locale, "—", "Pacific/Auckland")).toBe(
          new Intl.DateTimeFormat(locale, { ...options, timeZone: "Pacific/Auckland" }).format(new Date(value)),
        )
      }
      expect(migrationUtcIso("2026-09-26T13:30:00Z")).toBe("2026-09-26T13:30:00.000Z")
    })
  })
})
