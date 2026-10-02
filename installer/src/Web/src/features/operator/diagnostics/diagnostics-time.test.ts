import { describe, expect, it } from "vitest"

import { formatDateTime } from "@/app/formatters"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "./diagnostics-time"

describe("diagnostics time presentation", () => {
  it("uses the browser timezone for normal operator presentation", () => {
    const value = "2026-08-16T00:21:59.686Z"

    expect(formatDiagnosticsLocalDateTime(value, "en", true)).toBe(
      formatDateTime(value, "en", {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "numeric",
        minute: "2-digit",
        second: "2-digit",
        timeZoneName: "short",
      }),
    )
  })

  it("retains an exact UTC ISO value for technical evidence", () => {
    expect(diagnosticsUtcIso("2026-08-16T00:21:59.686Z"))
      .toBe("2026-08-16T00:21:59.686Z")
  })
})
