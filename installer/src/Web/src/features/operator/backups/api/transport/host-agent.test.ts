import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import {
  controlPlaneGet,
  HostAgentProblemError,
  isStepUpRequiredHostAgentProblem,
} from "./host-agent"

describe("controlPlaneGet", () => {
  it("SEC-AUTH-01A: does not attach infrastructure authority", async () => {
    server.use(
      http.get("/internal/host-agent/backups/catalog", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("content-type")).toBe("application/json")
        return HttpResponse.json({ entries: [{ id: "catalog-1" }] })
      }),
    )

    await expect(
      controlPlaneGet<{ entries: Array<{ id: string }> }>(
        "/internal/host-agent/backups/catalog",
      ),
    ).resolves.toEqual({ entries: [{ id: "catalog-1" }] })
  })

  it("includes a useful response body when the HostAgent rejects a request", async () => {
    server.use(
      http.get(
        "/internal/host-agent/backups/catalog",
        () => new HttpResponse("A restore workspace is active.", { status: 409 }),
      ),
    )

    await expect(
      controlPlaneGet("/internal/host-agent/backups/catalog"),
    ).rejects.toThrow(
      "GET /internal/host-agent/backups/catalog failed with status 409: A restore workspace is active.",
    )
  })

  it("preserves a valid structured HostAgent problem alongside the existing error text", async () => {
    const problem = {
      error: "restore_attempt_not_found",
      detail: "Restore attempt 'restore-1' was not found.",
      message: {
        code: "restore.attempt.not-found",
        arguments: {
          restoreSessionId: "restore-1",
        },
      },
    }

    server.use(
      http.get(
        "/internal/host-agent/backups/catalog",
        () => HttpResponse.json(problem, { status: 404 }),
      ),
    )

    const thrown = await controlPlaneGet(
      "/internal/host-agent/backups/catalog",
    ).catch((error: unknown) => error)

    expect(thrown).toBeInstanceOf(HostAgentProblemError)

    if (!(thrown instanceof HostAgentProblemError)) {
      throw new Error("Expected a HostAgentProblemError")
    }

    expect(thrown.message).toBe(
      `GET /internal/host-agent/backups/catalog failed with status 404: ${JSON.stringify(problem)}`,
    )
    expect(thrown.problem).toEqual(problem)
  })

  it("recognises the stable recent-step-up problem code without parsing raw error text", async () => {
    server.use(
      http.get(
        "/internal/host-agent/backups/catalog",
        () => HttpResponse.json(
          {
            error: "step_up_required",
            detail: "Fresh identity verification is required before this action.",
          },
          { status: 403 },
        ),
      ),
    )

    const thrown = await controlPlaneGet(
      "/internal/host-agent/backups/catalog",
    ).catch((error: unknown) => error)

    expect(isStepUpRequiredHostAgentProblem(thrown)).toBe(true)
  })

  it("keeps malformed JSON as a safe fallback transport error", async () => {
    server.use(
      http.get(
        "/internal/host-agent/backups/catalog",
        () => new HttpResponse("{not-json", { status: 500 }),
      ),
    )

    const thrown = await controlPlaneGet(
      "/internal/host-agent/backups/catalog",
    ).catch((error: unknown) => error)

    expect(thrown).toBeInstanceOf(HostAgentProblemError)

    if (!(thrown instanceof HostAgentProblemError)) {
      throw new Error("Expected a HostAgentProblemError")
    }

    expect(thrown.problem).toBeUndefined()
    expect(thrown.message).toContain("{not-json")
  })

  it("drops a malformed structured descriptor while retaining safe raw problem fields", async () => {
    server.use(
      http.get(
        "/internal/host-agent/backups/catalog",
        () =>
          HttpResponse.json(
            {
              error: "restore_attempt_not_found",
              detail: "Restore attempt 'restore-1' was not found.",
              message: {
                code: "restore.attempt.not-found",
                arguments: {
                  restoreSessionId: { unsafe: "object" },
                },
              },
            },
            { status: 404 },
          ),
      ),
    )

    const thrown = await controlPlaneGet(
      "/internal/host-agent/backups/catalog",
    ).catch((error: unknown) => error)

    expect(thrown).toBeInstanceOf(HostAgentProblemError)

    if (!(thrown instanceof HostAgentProblemError)) {
      throw new Error("Expected a HostAgentProblemError")
    }

    expect(thrown.problem).toEqual({
      error: "restore_attempt_not_found",
      detail: "Restore attempt 'restore-1' was not found.",
    })
  })
})
