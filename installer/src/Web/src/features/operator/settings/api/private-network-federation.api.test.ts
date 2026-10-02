import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import {
  applyPrivateNetworkFederationException,
  reviewPrivateNetworkFederationException,
} from "./private-network-federation.api"

describe("private network federation settings API", () => {
  it("sends an exact server-owned review request", async () => {
    server.use(
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/review",
        async ({ request }) => {
          expect(await request.json()).toEqual({ address: "10.0.0.238", action: "add" })
          return HttpResponse.json({
            source: "control-plane",
            status: "ready",
            runtimeStackId: "stack-1",
            slug: "demo-stack",
            matrixServerName: "matrix-demo-stack.deltabox.dev",
            action: "add",
            canonicalAddress: "10.0.0.238",
            canonicalCidr: "10.0.0.238/32",
            currentExceptions: [],
            proposedExceptions: ["10.0.0.238/32"],
            restartRequired: true,
            noChange: false,
            reviewHash: "sha256:review",
            confirmationText: "Apply exact exception.",
          })
        },
      ),
    )

    const result = await reviewPrivateNetworkFederationException("demo-stack", {
      address: "10.0.0.238",
      action: "add",
    })

    expect(result.canonicalCidr).toBe("10.0.0.238/32")
  })

  it("returns truthful rolled-back operation bodies from non-2xx responses", async () => {
    server.use(
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        () => HttpResponse.json({
          source: "control-plane",
          status: "rolled_back",
          operationId: "operation-1",
          runtimeStackId: "stack-1",
          slug: "demo-stack",
          action: "add",
          canonicalCidr: "10.0.0.238/32",
          observedExceptions: [],
          rollbackAttempted: true,
          rollbackSucceeded: true,
          errorCode: "private_network_restart_failed",
          detail: "Previous state restored.",
        }, { status: 500 }),
      ),
    )

    const result = await applyPrivateNetworkFederationException("demo-stack", {
      address: "10.0.0.238",
      action: "add",
      reviewHash: "sha256:review",
      idempotencyKey: "key-1",
    })

    expect(result.status).toBe("rolled_back")
    expect(result.rollbackSucceeded).toBe(true)
  })

  it("preserves structured step-up errors", async () => {
    server.use(
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        () => HttpResponse.json({ error: "step_up_required", detail: "Verify identity." }, { status: 403 }),
      ),
    )

    await expect(applyPrivateNetworkFederationException("demo-stack", {
      address: "10.0.0.238",
      action: "add",
      reviewHash: "sha256:review",
      idempotencyKey: "key-1",
    })).rejects.toMatchObject({
      name: "PrivateNetworkFederationProblemError",
      problem: { error: "step_up_required" },
    })
  })
})
