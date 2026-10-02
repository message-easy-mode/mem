import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { getJson, postJson } from "./api"
import { MemApiProblemError } from "./api-problem"

describe("shared JSON transport", () => {
  it("preserves safe correlated Problem Details without embedding the raw body in Error.message", async () => {
    server.use(
      http.post("/api/test/fail", () => HttpResponse.json(
        {
          type: "https://mem.invalid/problems/docker-operation-failed",
          title: "The operation failed",
          status: 500,
          detail: "MEM could not start the requested container.",
          code: "docker_operation_failed",
          traceId: "trace-1",
          incidentId: "inc_1",
          retryable: false,
        },
        {
          status: 500,
          headers: { "Content-Type": "application/problem+json" },
        },
      )),
    )

    const thrown = await postJson<void, unknown>("/api/test/fail").catch(
      (error: unknown) => error,
    )

    expect(thrown).toBeInstanceOf(MemApiProblemError)

    if (!(thrown instanceof MemApiProblemError)) {
      throw new Error("Expected MemApiProblemError")
    }

    expect(thrown.message).toBe("MEM could not start the requested container.")
    expect(thrown.message).not.toContain("trace-1")
    expect(thrown.problem).toMatchObject({
      code: "docker_operation_failed",
      traceId: "trace-1",
      incidentId: "inc_1",
    })
  })

  it("uses a bounded generic transport message for a non-problem failure body", async () => {
    server.use(
      http.get(
        "/api/test/text-failure",
        () => new HttpResponse("raw internal exception details", { status: 500 }),
      ),
    )

    const thrown = await getJson("/api/test/text-failure").catch(
      (error: unknown) => error,
    )

    expect(thrown).toBeInstanceOf(MemApiProblemError)
    expect((thrown as Error).message).toBe(
      "GET /api/test/text-failure failed with status 500",
    )
    expect((thrown as Error).message).not.toContain("raw internal")
  })

  it("still returns ordinary JSON responses", async () => {
    server.use(
      http.get("/api/test/ok", () => HttpResponse.json({ state: "ready" })),
    )

    await expect(getJson<{ state: string }>("/api/test/ok")).resolves.toEqual({
      state: "ready",
    })
  })
})
