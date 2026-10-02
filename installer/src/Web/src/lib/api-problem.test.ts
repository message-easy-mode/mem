import { describe, expect, it } from "vitest"

import {
  MemApiProblemError,
  buildDiagnosticsIncidentHref,
  getMemApiProblem,
  getMemApiProblemCode,
  getMemApiProblemDetail,
  parseMemApiProblemText,
} from "./api-problem"

describe("MEM API problem parsing", () => {
  it("preserves correlated RFC 7807 fields used by Diagnostics", () => {
    const problem = parseMemApiProblemText(JSON.stringify({
      type: "https://mem.invalid/problems/docker-operation-failed",
      title: "The container could not be started",
      status: 500,
      detail: "MEM could not start the private Synapse staging container.",
      code: "docker_container_start_failed",
      traceId: "trace-1",
      spanId: "span-1",
      requestId: "request-1",
      correlationId: "correlation-1",
      incidentId: "inc_20260802_0001",
      operationId: "f094ed15-e2fd-4ef1-a5e0-d50cf03df7e9",
      retryable: false,
      suggestedAction: "Open Diagnostics and review the container exit evidence.",
    }))

    expect(problem).toMatchObject({
      code: "docker_container_start_failed",
      incidentId: "inc_20260802_0001",
      traceId: "trace-1",
      retryable: false,
    })
  })

  it("does not treat unrelated JSON as a problem", () => {
    expect(parseMemApiProblemText('{"status":"ready"}')).toBeUndefined()
  })

  it("builds an encoded Diagnostics deep link only when an incident exists", () => {
    const error = new MemApiProblemError({
      method: "POST",
      path: "/api/test",
      status: 500,
      problem: {
        detail: "The operation failed.",
        incidentId: "inc/with spaces",
      },
    })

    expect(buildDiagnosticsIncidentHref(error)).toBe(
      "/diagnostics/logs?incident=inc%2Fwith+spaces",
    )
  })

  it("can read compatible problem metadata from feature-specific error objects", () => {
    const error = {
      problem: {
        error: "step_up_required",
        detail: "Fresh verification is required.",
        incidentId: "inc_1",
      },
    }

    expect(getMemApiProblem(error)?.incidentId).toBe("inc_1")
    expect(getMemApiProblemCode(error)).toBe("step_up_required")
    expect(getMemApiProblemDetail(error, "Fallback")).toBe(
      "Fresh verification is required.",
    )
  })
})
