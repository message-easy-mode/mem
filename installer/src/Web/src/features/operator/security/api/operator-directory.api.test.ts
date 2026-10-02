import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import {
  createPendingManagedOperator,
  getManagedOperators,
  issueManagedOperatorEnrollmentGrant,
  isStepUpRequiredOperatorDirectoryProblem,
  OperatorDirectoryProblemError,
  revokeManagedOperatorSessions,
  setManagedOperatorEnabled,
  setManagedOperatorRoles,
} from "./operator-directory.api"

const pendingOperator = {
  operatorId: "b0abf244-0119-4527-afd5-cba6abb3dced",
  username: "audit.reader",
  email: "audit.reader@example.test",
  isEnabled: false,
  isBootstrapProvisioning: false,
  hasPassword: false,
  hasTotp: false,
  roles: ["auditor"],
  createdAtUtc: "2026-07-04T12:00:00+00:00",
  lastLoginAtUtc: null,
  enrollmentGrantExpiresAtUtc: null,
  isCurrentOperator: false,
}

describe("managed operator API", () => {
  it("uses the named cookie session and returns only the server directory projection", async () => {
    server.use(
      http.get("/api/security/operators", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("authorization")).toBeNull()

        return HttpResponse.json([
          {
            ...pendingOperator,
            username: "admin",
            email: null,
            isEnabled: true,
            hasPassword: true,
            hasTotp: true,
            roles: ["platform_owner"],
            isCurrentOperator: true,
          },
        ])
      }),
    )

    await expect(getManagedOperators()).resolves.toMatchObject([
      {
        username: "admin",
        roles: ["platform_owner"],
        isCurrentOperator: true,
      },
    ])
  })

  it("creates a disabled pending operator without browser infrastructure authority", async () => {
    server.use(
      http.post("/api/security/operators", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("authorization")).toBeNull()
        expect(await request.json()).toEqual({
          username: "audit.reader",
          email: "audit.reader@example.test",
          roles: ["auditor"],
        })

        return HttpResponse.json(pendingOperator, { status: 201 })
      }),
    )

    await expect(createPendingManagedOperator({
      username: "audit.reader",
      email: "audit.reader@example.test",
      roles: ["auditor"],
    })).resolves.toMatchObject({
      username: "audit.reader",
      isEnabled: false,
      hasPassword: false,
      hasTotp: false,
    })
  })

  it("uses explicit lifecycle, role, and session endpoints with structured error codes", async () => {
    server.use(
      http.put("/api/security/operators/:operatorId/enabled", async ({ request, params }) => {
        expect(params.operatorId).toBe(pendingOperator.operatorId)
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(await request.json()).toEqual({ isEnabled: false })
        return HttpResponse.json({ ...pendingOperator, isEnabled: false })
      }),
      http.put("/api/security/operators/:operatorId/roles", async ({ request, params }) => {
        expect(params.operatorId).toBe(pendingOperator.operatorId)
        expect(await request.json()).toEqual({ roles: ["operator"] })
        return HttpResponse.json({ ...pendingOperator, roles: ["operator"] })
      }),
      http.post("/api/security/operators/:operatorId/revoke-sessions", ({ params }) => {
        expect(params.operatorId).toBe(pendingOperator.operatorId)
        return HttpResponse.json(pendingOperator)
      }),
    )

    await expect(setManagedOperatorEnabled(pendingOperator.operatorId, false))
      .resolves.toMatchObject({ isEnabled: false })

    await expect(setManagedOperatorRoles(pendingOperator.operatorId, ["operator"]))
      .resolves.toMatchObject({ roles: ["operator"] })

    await expect(revokeManagedOperatorSessions(pendingOperator.operatorId))
      .resolves.toMatchObject({ username: "audit.reader" })
  })

  it("issues a one-time enrolment code without browser infrastructure authority", async () => {
    server.use(
      http.post("/api/security/operators/:operatorId/enrollment-grants", ({ request, params }) => {
        expect(params.operatorId).toBe(pendingOperator.operatorId)
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("authorization")).toBeNull()

        return HttpResponse.json({
          operatorId: pendingOperator.operatorId,
          username: pendingOperator.username,
          enrollmentCode: "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
          expiresAtUtc: "2026-07-04T13:00:00+00:00",
        }, {
          headers: {
            "Cache-Control": "no-store",
          },
        })
      }),
    )

    await expect(issueManagedOperatorEnrollmentGrant(pendingOperator.operatorId))
      .resolves.toMatchObject({
        username: "audit.reader",
        enrollmentCode: "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
      })
  })

  it("preserves a step-up-required refusal so an already-confirmed governance action can be retried", async () => {
    server.use(
      http.put("/api/security/operators/:operatorId/roles", () =>
        HttpResponse.json(
          { status: "step_up_required" },
          { status: 403, headers: { "Cache-Control": "no-store" } },
        ),
      ),
    )

    const error = await setManagedOperatorRoles(
      pendingOperator.operatorId,
      ["operator"],
    ).catch((caught: unknown) => caught)

    expect(isStepUpRequiredOperatorDirectoryProblem(error)).toBe(true)
  })

  it("preserves a structured lifecycle rejection without fabricating operator data", async () => {
    server.use(
      http.put("/api/security/operators/:operatorId/enabled", () =>
        HttpResponse.json(
          { status: "last_active_platform_owner" },
          { status: 409 },
        ),
      ),
    )

    const error = await setManagedOperatorEnabled(
      pendingOperator.operatorId,
      false,
    ).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(OperatorDirectoryProblemError)
    expect((error as OperatorDirectoryProblemError).status).toBe(409)
    expect((error as OperatorDirectoryProblemError).code).toBe(
      "last_active_platform_owner",
    )
  })
})
