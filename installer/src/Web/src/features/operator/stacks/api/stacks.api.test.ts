import { http, HttpResponse } from "msw"
import { describe, expect, it, vi } from "vitest"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import {
  connectRuntimeStackTurn,
  disconnectRuntimeStackTurn,
  doctorRuntimeStack,
  getLatestRuntimeStackDoctor,
  getRuntimeStackDoctorReport,
  listRuntimeStackDoctorHistory,
  deactivateRuntimeStackUser,
  inspectRuntimeStack,
  inspectRuntimeStackTurn,
  reviewRuntimeStackTurnConnect,
  reviewRuntimeStackTurnDisconnect,
  listRuntimeStacks,
  reactivateRuntimeStackUser,
  resetRuntimeStackUserPassword,
  setRuntimeStackMatrixAdminAuthority,
  synchronizeRuntimeStackUsers,
  updateRuntimeStackIdentity,
  uploadRuntimeStackLogo,
  removeRuntimeStackLogo,
  RuntimeStackDoctorTransportTimeoutError,
  isRuntimeStackNotFoundProblem,
} from "./stacks.api"

const synchronizedUsers = {
  source: "control-plane",
  status: "ok",
  stackId: "7085b97d-3d30-434e-976a-62df0178be16",
  slug: "demo-stack",
  inventorySource: "synapse-postgres",
  inventoryStatus: "synchronized",
  inventoryLastAttemptedAtUtc: "2026-07-10T00:00:00Z",
  inventoryLastSynchronizedAtUtc: "2026-07-10T00:00:00Z",
  inventoryUserCount: 0,
  activeAdminCount: 0,
  inventoryErrorCode: null,
  synchronizationRequired: false,
  users: [],
  requiresFirstAdmin: true,
  canCreateUsers: true,
  adminAuthority: {
    status: "required",
    canResetPasswords: false,
    source: null,
    adminUserId: null,
    storedAtUtc: null,
    lastValidatedAtUtc: null,
    errorCode: null,
  },
  detail: null,
}

describe("SEC-AUTH-01A runtime stacks transport characterization", () => {
  it("SEC-AUTH-01A: does not send infrastructure authority for runtime-stack requests", async () => {
    server.use(
      http.get("/internal/host-agent/runtime-stacks", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("content-type")).toBe("application/json")
        return HttpResponse.json({ source: "control-plane", status: "ok", stacks: [], detail: null })
      }),
    )
    await expect(listRuntimeStacks()).resolves.toMatchObject({ status: "ok", stacks: [] })
  })
  it("STACKS-UX-01C-A: updates only operator-facing stack identity", async () => {
    server.use(
      http.put("/internal/host-agent/runtime-stacks/demo-stack/identity", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(await request.json()).toEqual({
          displayName: "Dewar Family Chat",
          category: "Family",
        })

        return HttpResponse.json({
          source: "control-plane",
          status: "public_routes_verified",
          stackId: synchronizedUsers.stackId,
          slug: "demo-stack",
          displayName: "Dewar Family Chat",
          category: "Family",
          matrix: null,
          element: null,
          lastVerifiedAtUtc: "2026-08-24T00:00:00Z",
          detail: null,
        })
      }),
    )

    await expect(updateRuntimeStackIdentity("demo-stack", {
      displayName: "Dewar Family Chat",
      category: "Family",
    })).resolves.toMatchObject({
      slug: "demo-stack",
      displayName: "Dewar Family Chat",
      category: "Family",
    })
  })

  it("STACKS-UX-01C-B: uploads and removes a stack logo without JSON-wrapping the file", async () => {
    let removeCalled = false

    server.use(
      http.put("/internal/host-agent/runtime-stacks/demo-stack/logo", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("content-type")).toMatch(/^multipart\/form-data; boundary=/)
        // JSDOM and Node/Undici expose different File implementations. Parsing
        // request.formData() here asks Undici to reconstruct a JSDOM File and
        // fails before the handler can inspect an otherwise valid multipart
        // request. Assert the actual wire payload instead.
        const multipartBody = new TextDecoder().decode(await request.arrayBuffer())
        // Node/Undici normalizes a JSDOM File from another Web API realm to
        // filename="blob" even when the browser helper supplies file.name as
        // the multipart filename. The filename is deliberately not part of
        // MEM's trusted server contract; prove the actual file field and media
        // type instead.
        expect(multipartBody).toContain('name="file"')
        expect(multipartBody).toContain("Content-Type: image/png")

        return HttpResponse.json({
          source: "control-plane",
          status: "public_routes_verified",
          stackId: synchronizedUsers.stackId,
          slug: "demo-stack",
          displayName: "Dewar Family Chat",
          category: "Family",
          logoUrl: "/internal/host-agent/runtime-stacks/demo-stack/logo?v=abc123",
          matrix: null,
          element: null,
          lastVerifiedAtUtc: "2026-08-25T00:00:00Z",
          detail: null,
        })
      }),
      http.delete("/internal/host-agent/runtime-stacks/demo-stack/logo", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        removeCalled = true
        return HttpResponse.json({
          source: "control-plane",
          status: "public_routes_verified",
          stackId: synchronizedUsers.stackId,
          slug: "demo-stack",
          displayName: "Dewar Family Chat",
          category: "Family",
          logoUrl: null,
          matrix: null,
          element: null,
          lastVerifiedAtUtc: "2026-08-25T00:00:00Z",
          detail: null,
        })
      }),
    )

    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], "family.png", {
      type: "image/png",
    })
    await expect(uploadRuntimeStackLogo("demo-stack", file)).resolves.toMatchObject({
      logoUrl: expect.stringContaining("/logo?v="),
    })
    await expect(removeRuntimeStackLogo("demo-stack")).resolves.toMatchObject({ logoUrl: null })
    expect(removeCalled).toBe(true)
  })

  it("STACKS-UX-01F: loads the latest persisted Doctor report through the operator session", async () => {
    server.use(
      http.get("/internal/host-agent/runtime-stacks/demo-stack/doctor/latest", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("content-type")).toBe("application/json")

        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stackId: synchronizedUsers.stackId,
          slug: "demo-stack",
          report: {
            source: "control-plane",
            status: "passed",
            stackId: synchronizedUsers.stackId,
            slug: "demo-stack",
            lastVerifiedStatus: "passed",
            lastVerifiedAtUtc: "2026-08-25T01:08:00Z",
            checkedAtUtc: "2026-08-25T01:08:00Z",
            allPassed: true,
            checks: [],
            detail: "Runtime readiness checks passed.",
            operationId: "doctor-operation-persisted",
            reportId: "doctor-report-persisted",
          },
          detail: null,
        })
      }),
    )

    await expect(getLatestRuntimeStackDoctor("demo-stack")).resolves.toMatchObject({
      status: "ok",
      report: {
        allPassed: true,
        operationId: "doctor-operation-persisted",
        reportId: "doctor-report-persisted",
      },
    })
  })

  it("STACKS-UX-01H: loads paginated previous Doctor reports and historical evidence", async () => {
    server.use(
      http.get("/internal/host-agent/runtime-stacks/demo-stack/doctor/history", ({ request }) => {
        const url = new URL(request.url)
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(url.searchParams.get("page")).toBe("2")
        expect(url.searchParams.get("pageSize")).toBe("10")

        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stackId: synchronizedUsers.stackId,
          slug: "demo-stack",
          reports: [
            {
              reportId: "11111111-1111-1111-1111-111111111111",
              operationId: "22222222-2222-2222-2222-222222222222",
              status: "failed",
              allPassed: false,
              checkedAtUtc: "2026-08-24T01:00:00Z",
              checkCount: 7,
              failedCheckCount: 2,
              warningCount: 0,
              detail: "Runtime readiness checks failed. Failed checks: 2.",
            },
          ],
          totalCount: 11,
          page: 2,
          pageSize: 10,
          totalPages: 2,
          hasPreviousPage: true,
          hasNextPage: false,
          detail: null,
        })
      }),
      http.get(
        "/internal/host-agent/runtime-stacks/demo-stack/doctor/reports/11111111-1111-1111-1111-111111111111",
        ({ request }) => {
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          return HttpResponse.json({
            source: "control-plane",
            status: "failed",
            stackId: synchronizedUsers.stackId,
            slug: "demo-stack",
            lastVerifiedStatus: "failed",
            lastVerifiedAtUtc: "2026-08-24T01:00:00Z",
            checkedAtUtc: "2026-08-24T01:00:00Z",
            allPassed: false,
            checks: [],
            detail: "Runtime readiness checks failed. Failed checks: 2.",
            operationId: "22222222-2222-2222-2222-222222222222",
            reportId: "11111111-1111-1111-1111-111111111111",
          })
        },
      ),
    )

    await expect(listRuntimeStackDoctorHistory("demo-stack", 2, 10)).resolves.toMatchObject({
      totalCount: 11,
      page: 2,
      reports: [{ reportId: "11111111-1111-1111-1111-111111111111" }],
    })
    await expect(
      getRuntimeStackDoctorReport("demo-stack", "11111111-1111-1111-1111-111111111111"),
    ).resolves.toMatchObject({
      reportId: "11111111-1111-1111-1111-111111111111",
      allPassed: false,
    })
  })

  it("STACKS-UX-01H-CORR-01: bounds a Doctor POST that never returns from the browser transport", async () => {
    const fetchSpy = vi.spyOn(globalThis, "fetch").mockImplementation((_input, init) =>
      new Promise<Response>((_resolve, reject) => {
        const signal = init?.signal
        if (!signal) {
          reject(new Error("Doctor fetch did not receive an abort signal."))
          return
        }

        const rejectAborted = () =>
          reject(new DOMException("The operation was aborted.", "AbortError"))

        if (signal.aborted) {
          rejectAborted()
          return
        }

        signal.addEventListener("abort", rejectAborted, { once: true })
      }),
    )

    try {
      await expect(doctorRuntimeStack("demo-stack", 5)).rejects.toBeInstanceOf(
        RuntimeStackDoctorTransportTimeoutError,
      )
      expect(fetchSpy).toHaveBeenCalledWith(
        "/internal/host-agent/runtime-stacks/demo-stack/doctor",
        expect.objectContaining({
          method: "POST",
          signal: expect.any(AbortSignal),
        }),
      )
    } finally {
      fetchSpy.mockRestore()
    }
  })

  it("STACKS-UX-01D: sends category with the explicit inventory query parameters", async () => {
    server.use(
      http.get("/internal/host-agent/runtime-stacks", ({ request }) => {
        const url = new URL(request.url)
        expect(url.searchParams.get("page")).toBe("2")
        expect(url.searchParams.get("pageSize")).toBe("25")
        expect(url.searchParams.get("search")).toBe("school")
        expect(url.searchParams.get("status")).toBe("needs_attention")
        expect(url.searchParams.get("category")).toBe("School")
        expect(url.searchParams.get("sortBy")).toBe("lastChecked")
        expect(url.searchParams.get("sortDirection")).toBe("desc")
        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stacks: [],
          detail: null,
          summary: { totalStacks: 3, healthy: 2, needsAttention: 1, offline: 0, settingUp: 0, unknown: 0 },
          categories: [{ category: "School", count: 2 }],
          totalMatchingStacks: 0,
          page: 1,
          pageSize: 25,
          totalPages: 1,
          hasPreviousPage: false,
          hasNextPage: false,
          isPaged: true,
        })
      }),
    )

    await expect(listRuntimeStacks({
      page: 2,
      pageSize: 25,
      search: "school",
      status: "needs_attention",
      category: "School",
      sortBy: "lastChecked",
      sortDirection: "desc",
    })).resolves.toMatchObject({ status: "ok", pageSize: 25 })
  })



  it("STACK-TURN-01A: inspects stack TURN state through a read-only operator request", async () => {
    server.use(
      http.get("/internal/host-agent/runtime-stacks/demo-stack/turn", ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("content-type")).toBe("application/json")
        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          runtimeStackId: synchronizedUsers.stackId,
          slug: "demo-stack",
          inspectedAtUtc: "2026-07-26T03:30:00Z",
          state: "not-connected",
          management: "none",
          liveConfiguration: null,
          persistedMetadata: {
            recorded: false,
            configured: null,
            turnUris: [],
            publicHost: null,
            realm: null,
            configurationSource: null,
            relayPortsPublished: null,
            sharedSecretPresent: null,
            userLifetime: null,
            allowGuests: null,
            matchesLiveConfiguration: null,
          },
          platform: null,
          matrixRuntime: {
            exists: true,
            running: true,
            identityMatches: true,
            problemCode: null,
            detail: null,
          },
          diagnostics: [],
          warnings: [],
          detail: "This stack is not configured to use a TURN service.",
        })
      }),
    )

    await expect(inspectRuntimeStackTurn("demo-stack")).resolves.toMatchObject({
      state: "not-connected",
      management: "none",
    })
  })

  it("STACK-TURN-01B: reviews and applies a narrow confirmed TURN connection", async () => {
    let appliedRequest: Record<string, unknown> | null = null
    server.use(
      http.post(
        "/internal/host-agent/runtime-stacks/demo-stack/turn/connect/review",
        async ({ request }) => {
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          expect(await request.text()).toBe("")
          return HttpResponse.json({
            source: "control-plane",
            status: "ready",
            runtimeStackId: synchronizedUsers.stackId,
            slug: "demo-stack",
            matrixServerName: "matrix.example.test",
            mode: "configure",
            configurationChangeRequired: true,
            restartRequired: true,
            platformPublicHost: "turn.example.test",
            turnUris: ["turn:turn.example.test:3478?transport=udp"],
            userLifetime: "1h",
            allowGuests: true,
            currentConfigurationSha256: "sha256:before",
            reviewHash: "review-hash",
            confirmationText: "Connect and restart Matrix.",
            consequences: ["Matrix restarts."],
          })
        },
      ),
      http.post(
        "/internal/host-agent/runtime-stacks/demo-stack/turn/connect",
        async ({ request }) => {
          const body = await request.json() as Record<string, unknown>
          appliedRequest = body
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          expect(body).toMatchObject({
            reviewHash: "review-hash",
            confirmConnectToPlatformTurn: true,
            confirmReplaceExternalTurn: false,
          })
          expect(body.idempotencyKey).toMatch(/^turn-connect-demo-stack-/)
          return HttpResponse.json({
            source: "control-plane",
            status: "succeeded",
            operationId: "5a213f1d-6dca-4a36-8b80-b1d503ab44be",
            runtimeStackId: synchronizedUsers.stackId,
            slug: "demo-stack",
            mode: "configure",
            configurationChanged: true,
            matrixRestarted: true,
            rollbackAttempted: false,
            rollbackSucceeded: null,
            stateAfter: "connected",
            errorCode: null,
            detail: "Connected.",
          })
        },
      ),
    )

    const review = await reviewRuntimeStackTurnConnect("demo-stack")
    expect(review).toMatchObject({ mode: "configure", restartRequired: true })

    await expect(connectRuntimeStackTurn("demo-stack", {
      reviewHash: review.reviewHash,
      confirmConnectToPlatformTurn: true,
      confirmReplaceExternalTurn: false,
    })).resolves.toMatchObject({ status: "succeeded", matrixRestarted: true })
    expect(appliedRequest).not.toBeNull()
  })

  it("STACK-TURN-01C: reviews and applies a narrow confirmed TURN disconnection", async () => {
    let appliedRequest: Record<string, unknown> | null = null
    server.use(
      http.post(
        "/internal/host-agent/runtime-stacks/demo-stack/turn/disconnect/review",
        async ({ request }) => {
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          expect(await request.text()).toBe("")
          return HttpResponse.json({
            source: "control-plane",
            status: "ready",
            runtimeStackId: synchronizedUsers.stackId,
            slug: "demo-stack",
            matrixServerName: "matrix.example.test",
            configurationChangeRequired: true,
            restartRequired: true,
            platformPublicHost: "turn.example.test",
            turnUris: ["turn:turn.example.test:3478?transport=udp"],
            currentConfigurationSha256: "sha256:before",
            reviewHash: "disconnect-review-hash",
            confirmationText: "Disconnect and restart Matrix.",
            consequences: ["Matrix restarts."],
          })
        },
      ),
      http.post(
        "/internal/host-agent/runtime-stacks/demo-stack/turn/disconnect",
        async ({ request }) => {
          const body = await request.json() as Record<string, unknown>
          appliedRequest = body
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          expect(body).toMatchObject({
            reviewHash: "disconnect-review-hash",
            confirmDisconnectFromPlatformTurn: true,
          })
          expect(body.idempotencyKey).toMatch(/^turn-disconnect-demo-stack-/)
          expect(body).not.toHaveProperty("sharedSecret")
          return HttpResponse.json({
            source: "control-plane",
            status: "succeeded",
            operationId: "6b324f2e-7edb-4b47-9c91-c2e614bc55cf",
            runtimeStackId: synchronizedUsers.stackId,
            slug: "demo-stack",
            configurationChanged: true,
            matrixRestarted: true,
            rollbackAttempted: false,
            rollbackSucceeded: null,
            stateAfter: "not-connected",
            errorCode: null,
            detail: "Disconnected.",
          })
        },
      ),
    )

    const review = await reviewRuntimeStackTurnDisconnect("demo-stack")
    expect(review).toMatchObject({ configurationChangeRequired: true, restartRequired: true })

    await expect(disconnectRuntimeStackTurn("demo-stack", {
      reviewHash: review.reviewHash,
      confirmDisconnectFromPlatformTurn: true,
    })).resolves.toMatchObject({ status: "succeeded", matrixRestarted: true })
    expect(appliedRequest).not.toBeNull()
  })

  it("USER-REC-03: synchronizes users through the operator session without an agent-secret header", async () => {
    server.use(
      http.post("/internal/host-agent/runtime-stacks/demo-stack/users/synchronize", async ({ request }) => {
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("content-type")).toBe("application/json")
        expect(await request.text()).toBe("")
        return HttpResponse.json(synchronizedUsers)
      }),
    )
    await expect(synchronizeRuntimeStackUsers("demo-stack")).resolves.toMatchObject({
      inventoryStatus: "synchronized",
      synchronizationRequired: false,
    })
  })

  it("USER-PASS-02: submits Matrix admin credentials only to the protected authority endpoint", async () => {
    server.use(
      http.post(
        "/internal/host-agent/runtime-stacks/demo-stack/users/admin-authority",
        async ({ request }) => {
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          expect(await request.json()).toEqual({
            accessToken: null,
            matrixUserId: "@admin:example.test",
            password: "MatrixAdmin!123",
          })

          return HttpResponse.json({
            status: "available",
            canResetPasswords: true,
            source: "operator-password-login",
            adminUserId: "@admin:example.test",
            storedAtUtc: "2026-07-10T08:00:00Z",
            lastValidatedAtUtc: "2026-07-10T08:00:00Z",
            errorCode: null,
          })
        },
      ),
    )

    await expect(
      setRuntimeStackMatrixAdminAuthority("demo-stack", {
        accessToken: null,
        matrixUserId: "@admin:example.test",
        password: "MatrixAdmin!123",
      }),
    ).resolves.toMatchObject({
      status: "available",
      canResetPasswords: true,
    })
  })

  it("USER-PASS-02: resets one projected user through the narrow password endpoint", async () => {
    server.use(
      http.post(
        "/internal/host-agent/runtime-stacks/demo-stack/users/11111111-1111-1111-1111-111111111111/password",
        async ({ request }) => {
          expect(request.headers.get("x-mem-agent-secret")).toBeNull()
          expect(await request.json()).toEqual({
            newPassword: "NewMatrix!123",
          })

          return HttpResponse.json({
            source: "control-plane",
            status: "password_reset",
            runtimeStackId: "7085b97d-3d30-434e-976a-62df0178be16",
            userId: "11111111-1111-1111-1111-111111111111",
            matrixUserId: "@member:example.test",
            logoutDevices: true,
            adminAuthorityInvalidated: false,
            completedAtUtc: "2026-07-10T08:05:00Z",
          })
        },
      ),
    )

    await expect(
      resetRuntimeStackUserPassword(
        "demo-stack",
        "11111111-1111-1111-1111-111111111111",
        { newPassword: "NewMatrix!123" },
      ),
    ).resolves.toMatchObject({
      status: "password_reset",
      logoutDevices: true,
    })
  })
  it("USER-LIFECYCLE-01: deactivates and reactivates one projected Matrix user", async () => {
    const userId = "11111111-1111-1111-1111-111111111111"
    server.use(
      http.post(
        `/internal/host-agent/runtime-stacks/demo-stack/users/${userId}/deactivate`,
        async ({ request }) => {
          expect(await request.json()).toEqual({ erase: false })
          return HttpResponse.json({
            source: "control-plane",
            status: "deactivated",
            runtimeStackId: synchronizedUsers.stackId,
            userId,
            matrixUserId: "@member:example.test",
            isDeactivated: true,
            logoutDevices: true,
            completedAtUtc: "2026-07-10T09:00:00Z",
          })
        },
      ),
      http.post(
        `/internal/host-agent/runtime-stacks/demo-stack/users/${userId}/reactivate`,
        async ({ request }) => {
          expect(await request.json()).toEqual({ newPassword: "Reactivated!123" })
          return HttpResponse.json({
            source: "control-plane",
            status: "reactivated",
            runtimeStackId: synchronizedUsers.stackId,
            userId,
            matrixUserId: "@member:example.test",
            isDeactivated: false,
            logoutDevices: true,
            completedAtUtc: "2026-07-10T09:05:00Z",
          })
        },
      ),
    )

    await expect(deactivateRuntimeStackUser("demo-stack", userId, { erase: false }))
      .resolves.toMatchObject({ status: "deactivated", isDeactivated: true })
    await expect(reactivateRuntimeStackUser("demo-stack", userId, {
      newPassword: "Reactivated!123",
    })).resolves.toMatchObject({ status: "reactivated", isDeactivated: false })
  })

  it("RESTORE-STAGING-SERVICES-UX-CORR-01: preserves not-found classification without exposing the internal request path or raw response body", async () => {
    const slug = "restore-staging-synapse-20260917-abcd1234"
    const path = `/internal/host-agent/runtime-stacks/${slug}`

    server.use(
      http.get(path, () =>
        HttpResponse.json(
          {
            source: "control-plane",
            status: "not_found",
            stackId: null,
            slug,
            matrix: null,
            element: null,
            lastVerifiedAtUtc: null,
            detail: `Runtime stack '${slug}' was not found in the local control-plane manifest store.`,
          },
          { status: 404 },
        ),
      ),
    )

    let thrown: unknown
    try {
      await inspectRuntimeStack(slug)
    } catch (error) {
      thrown = error
    }

    expect(isRuntimeStackNotFoundProblem(thrown)).toBe(true)
    expect(thrown).toBeInstanceOf(Error)
    expect((thrown as Error).message).not.toContain(path)
    expect((thrown as Error).message).not.toContain('"source":"control-plane"')
  })

})
