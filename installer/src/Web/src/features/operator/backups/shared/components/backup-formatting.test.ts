import { describe, expect, it } from "vitest"

import { formatDate } from "./backup-formatting"

describe("backup date formatting", () => {
  it("treats an offset-free UTC API timestamp the same as an explicit Z timestamp", () => {
    expect(formatDate("2026-07-26T17:58:48", "en")).toBe(
      formatDate("2026-07-26T17:58:48Z", "en"),
    )
  })

  it("preserves timestamps that already carry an explicit offset", () => {
    expect(formatDate("2026-07-27T05:58:48+12:00", "en")).toBe(
      formatDate("2026-07-26T17:58:48Z", "en"),
    )
  })

  it("keeps the original value when parsing fails", () => {
    expect(formatDate("not-a-date", "en")).toBe("not-a-date")
  })
})
