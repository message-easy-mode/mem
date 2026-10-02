import { afterEach, describe, expect, it, vi } from "vitest"

import {
  changeOperatorPassword,
  getControlPlaneSession,
  loginOperator,
  logoutControlPlane,
  prepareFirstOwner,
  regenerateOperatorRecoveryCodes,
  verifyBootstrapCode,
  verifyOperatorRecoveryCode,
  verifyOperatorStepUp,
  verifyOperatorTotp,
} from "./control-plane-auth"

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("control-plane auth transport", () => {
  it("reads the server-managed session without supplying browser authority or permitting cache retention", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          authenticated: false,
          authenticationKind: null,
          displayName: null,
          roles: [],
          requiresFirstOwnerBootstrap: true,
          hasCompletedPlatformOwner: false,
        }),
        { status: 200 },
      ),
    )

    vi.stubGlobal("fetch", fetchMock)

    const session = await getControlPlaneSession()

    expect(session.requiresFirstOwnerBootstrap).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith("/api/auth/session", {
      method: "GET",
      credentials: "include",
      cache: "no-store",
    })
  })

  it("uses the setup code only to issue the scoped bootstrap cookie without permitting cache retention", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          expiresAtUtc: "2026-07-03T12:15:00Z",
        }),
        { status: 200 },
      ),
    )

    vi.stubGlobal("fetch", fetchMock)

    await verifyBootstrapCode("mem_test123")

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/bootstrap/verify", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ token: "mem_test123" }),
    })
  })

  it("sends first-owner details only to the scoped bootstrap endpoint without permitting cache retention", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          username: "first.owner",
          manualEntryKey: "BASE32",
          authenticatorUri: "otpauth://totp/MEM",
        }),
        { status: 200 },
      ),
    )

    vi.stubGlobal("fetch", fetchMock)

    await prepareFirstOwner({
      username: "first.owner",
      email: "",
      password: "Secure!Foundation123",
    })

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/bootstrap/first-owner", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        username: "first.owner",
        email: "",
        password: "Secure!Foundation123",
      }),
    })
  })

  it("does not interpret an invalid operator login response as an authenticated session", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 401 })))

    await expect(loginOperator("first.owner", "wrong")).resolves.toEqual({
      status: "invalid",
    })
  })

  it("surfaces a server rate-limit response without exposing an authenticated session", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation(() =>
        Promise.resolve(
          new Response(
            JSON.stringify({ status: "rate_limited" }),
            { status: 429 },
          ),
        ),
      ),
    )

    await expect(loginOperator("first.owner", "wrong")).resolves.toEqual({
      status: "rate_limited",
    })

    await expect(verifyOperatorTotp("000000")).resolves.toEqual({
      status: "rate_limited",
    })

    await expect(verifyOperatorRecoveryCode("ABCDE-FGHIJ")).resolves.toEqual({
      status: "rate_limited",
    })
  })

  it("posts a recovery code only to the MFA-pending completion endpoint without permitting cache retention", async () => {
    const session = {
      authenticated: true,
      authenticationKind: "operator" as const,
      displayName: "first.owner",
      roles: ["platform_owner"],
      requiresFirstOwnerBootstrap: false,
      hasCompletedPlatformOwner: true,
    }
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          status: "authenticated",
          session,
        }),
        { status: 200 },
      ),
    )

    vi.stubGlobal("fetch", fetchMock)

    await expect(verifyOperatorRecoveryCode("ABCDE-FGHIJ")).resolves.toEqual({
      status: "authenticated",
      session,
    })

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/login/recovery-code", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ code: "ABCDE-FGHIJ" }),
    })
  })

  it("regenerates recovery codes only through the no-store protected endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          status: "recovery_codes_regenerated",
          recoveryCodes: ["AAAAA-BBBBB", "CCCCC-DDDDD"],
        }),
        { status: 200 },
      ),
    )

    vi.stubGlobal("fetch", fetchMock)

    await expect(regenerateOperatorRecoveryCodes()).resolves.toEqual({
      status: "regenerated",
      recoveryCodes: ["AAAAA-BBBBB", "CCCCC-DDDDD"],
    })

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/recovery-codes/regenerate", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
    })
  })

  it("reports an expired or missing step-up grant without treating codes as generated", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ status: "step_up_required" }), { status: 403 }),
      ),
    )

    await expect(regenerateOperatorRecoveryCodes()).resolves.toEqual({
      status: "step_up_required",
    })
  })

  it("posts only the replacement password pair to the self-service password endpoint without cache retention", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ status: "password_changed" }), { status: 200 }),
    )

    vi.stubGlobal("fetch", fetchMock)

    await expect(
      changeOperatorPassword("Different!Foundation456", "Different!Foundation456"),
    ).resolves.toEqual({ status: "changed" })

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/password", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        newPassword: "Different!Foundation456",
        confirmPassword: "Different!Foundation456",
      }),
    })
  })

  it("keeps password-change failures bounded to safe status values", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn()
        .mockResolvedValueOnce(
          new Response(JSON.stringify({ status: "step_up_required" }), { status: 403 }),
        )
        .mockResolvedValueOnce(
          new Response(JSON.stringify({ status: "password_not_accepted" }), { status: 400 }),
        ),
    )

    await expect(
      changeOperatorPassword("Different!Foundation456", "Different!Foundation456"),
    ).resolves.toEqual({ status: "step_up_required" })

    await expect(
      changeOperatorPassword("weak", "weak"),
    ).resolves.toEqual({ status: "not_accepted" })
  })

  it("posts password and current TOTP only to the protected step-up endpoint without cache retention", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(
        JSON.stringify({
          status: "step_up_authenticated",
          expiresAtUtc: "2026-07-04T22:10:00Z",
        }),
        { status: 200 },
      ),
    )

    vi.stubGlobal("fetch", fetchMock)

    await expect(
      verifyOperatorStepUp("Secure!Foundation123", "123456"),
    ).resolves.toEqual({
      status: "verified",
      expiresAtUtc: "2026-07-04T22:10:00Z",
    })

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/step-up", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        password: "Secure!Foundation123",
        code: "123456",
      }),
    })
  })

  it("reports an unavailable step-up verifier without treating the session as verified", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(
      new Response(null, { status: 503 }),
    ))

    await expect(
      verifyOperatorStepUp("Secure!Foundation123", "123456"),
    ).resolves.toEqual({ status: "unavailable" })
  })

  it("signs out through the server-managed cookie endpoint without browser authority or permitting cache retention", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))

    vi.stubGlobal("fetch", fetchMock)

    await logoutControlPlane()

    expect(fetchMock).toHaveBeenCalledWith("/api/auth/logout", {
      method: "POST",
      credentials: "include",
      cache: "no-store",
    })
  })

  it("does not report a failed logout as complete", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(null, { status: 503 })))

    await expect(logoutControlPlane()).rejects.toThrow("logout_unavailable")
  })
})
