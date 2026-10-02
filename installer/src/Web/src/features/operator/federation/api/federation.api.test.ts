import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import {
  FederationHostAgentProblemError,
  applyRuntimeStackFederation,
  getFederationProblemCode,
  getFederationProblemDetail,
  inspectRuntimeStackFederation,
  isStepUpRequiredFederationProblem,
  reviewRuntimeStackFederation,
} from "./federation.api"

const endpoint = "/internal/host-agent/runtime-stacks/demo-stack/federation"

describe("federation API", () => {
  it("preserves the structured HostAgent problem contract", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          {
            error: "federation_config_custom_unsupported",
            detail: "The active federation configuration is custom.",
          },
          { status: 409 },
        ),
      ),
    )

    let error: unknown
    try {
      await inspectRuntimeStackFederation("demo-stack")
    } catch (caught) {
      error = caught
    }

    expect(error).toBeInstanceOf(FederationHostAgentProblemError)
    expect(getFederationProblemCode(error)).toBe("federation_config_custom_unsupported")
    expect(getFederationProblemDetail(error)).toBe("The active federation configuration is custom.")
  })

  it("encodes the stack identity in the read-only endpoint", async () => {
    let requestedUrl = ""
    server.use(
      http.get("*", ({ request }) => {
        requestedUrl = request.url
        return HttpResponse.json({ source: "control-plane", status: "ok" })
      }),
    )

    await inspectRuntimeStackFederation("stack name")

    expect(requestedUrl).toContain("/runtime-stacks/stack%20name/federation")
  })

  it("posts the canonical policy to the stateless review endpoint", async () => {
    let received: unknown
    server.use(
      http.post(`${endpoint}/review`, async ({ request }) => {
        received = await request.json()
        return HttpResponse.json({
          source: "control-plane",
          status: "ready",
          reviewHash: `sha256:${"a".repeat(64)}`,
        })
      }),
    )

    await reviewRuntimeStackFederation("demo-stack", {
      mode: "restricted",
      allowlist: ["matrix.example.org", "partner.example"],
    })

    expect(received).toEqual({
      mode: "restricted",
      allowlist: ["matrix.example.org", "partner.example"],
    })
  })

  it("returns a candidate-rejected operation outcome from a 422 response", async () => {
    server.use(
      http.post(`${endpoint}/apply`, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "candidate_rejected",
          operationId: "11111111-1111-1111-1111-111111111111",
          previousMode: "public",
          requestedMode: "restricted",
          observedMode: "public",
          rollbackAttempted: false,
          rollbackSucceeded: null,
          checks: [],
          errorCode: "federation_config_validation_failed",
          detail: "Candidate rejected.",
        }, { status: 422 }),
      ),
    )

    const result = await applyRuntimeStackFederation("demo-stack", {
      mode: "restricted",
      allowlist: ["partner.example"],
      reviewHash: `sha256:${"a".repeat(64)}`,
      idempotencyKey: "federation-demo",
    })

    expect(result.status).toBe("candidate_rejected")
    expect(result.rollbackAttempted).toBe(false)
  })

  it("preserves step-up-required so the exact confirmed request can continue", async () => {
    server.use(
      http.post(`${endpoint}/apply`, () =>
        HttpResponse.json(
          {
            error: "step_up_required",
            detail: "Recent identity verification is required.",
          },
          { status: 403 },
        ),
      ),
    )

    let error: unknown
    try {
      await applyRuntimeStackFederation("demo-stack", {
        mode: "public",
        allowlist: [],
        reviewHash: `sha256:${"a".repeat(64)}`,
        idempotencyKey: "federation-demo",
      })
    } catch (caught) {
      error = caught
    }

    expect(isStepUpRequiredFederationProblem(error)).toBe(true)
    expect(getFederationProblemDetail(error)).toBe("Recent identity verification is required.")
  })
})
