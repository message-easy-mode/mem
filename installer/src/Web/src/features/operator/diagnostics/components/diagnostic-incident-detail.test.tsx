import { screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"
import type { DiagnosticsEvent } from "../api/diagnostics.types"
import { DiagnosticEventDisclosure } from "./diagnostic-incident-detail"

const longEvidence = "sha256:" + "abcdef0123456789".repeat(12)
const longKey = "upstream.proxy.host.with.a.deliberately.long.unbroken.evidence.key"
const longMessage = "Technical evidence " + "unbroken".repeat(24)

const event: DiagnosticsEvent = {
  schemaVersion: 1,
  eventId: "evt_responsive_wrap",
  timestampUtc: "2026-09-12T04:00:00Z",
  severity: "error",
  eventCode: "diagnostics_responsive_wrap_probe",
  source: "DiagnosticsResponsiveWrapTests",
  feature: "diagnostics",
  stage: "incident-detail",
  message: longMessage,
  incidentId: "inc_responsive_wrap",
  traceId: null,
  spanId: null,
  requestId: null,
  correlationId: null,
  operationId: null,
  resource: null,
  expected: { [longKey]: longEvidence },
  observed: { currentState: longEvidence },
  details: { boundedEvidence: longEvidence },
  exception: {
    type: "DiagnosticsResponsiveWrapException" + "Type".repeat(10),
    message: "Exception evidence " + "unbroken".repeat(20),
    stackTrace: "at Very.Long.Namespace." + "Frame".repeat(40),
    innerExceptions: [],
  },
  suggestedAction: null,
  retryable: false,
  redactionsApplied: true,
  truncated: false,
}

describe("DiagnosticEventDisclosure responsive evidence", () => {
  it("allows long technical evidence to shrink and wrap without overlapping adjacent columns", () => {
    const { container } = renderWithProviders(<DiagnosticEventDisclosure event={event} />)

    expect(screen.getByText(longMessage)).toHaveClass("[overflow-wrap:anywhere]")

    const evidenceValues = screen.getAllByText(longEvidence)
    expect(evidenceValues).toHaveLength(3)
    for (const value of evidenceValues) {
      expect(value).toHaveClass("min-w-0", "whitespace-pre-wrap", "[overflow-wrap:anywhere]")
      expect(value.parentElement).toHaveClass("min-w-0")
    }

    expect(screen.getByText(longKey)).toHaveClass("[overflow-wrap:anywhere]")
    expect(container.querySelector("details")).toHaveClass("min-w-0")
    expect(container.querySelector("pre")).toHaveClass("max-w-full", "overflow-auto")
  })
})
