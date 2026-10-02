import { afterEach, describe, expect, it, vi } from "vitest"

import {
  approveCliDeviceAuthorization,
  denyCliDeviceAuthorization,
  reviewCliDeviceAuthorization,
} from "./cli-device-authorization"

afterEach(() => {
  vi.unstubAllGlobals()
})

describe("CLI device authorization browser transport", () => {
  it("reviews a pending device with a same-origin no-store request and exposes only safe details", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: "authorization_pending",
      deviceLabel: "SSH host shell",
      expiresAtUtc: "2026-07-06T23:10:00Z",
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }))
    vi.stubGlobal("fetch", fetchMock)

    await expect(reviewCliDeviceAuthorization("ABCD-EFGH")).resolves.toEqual({
      status: "authorization_pending",
      deviceLabel: "SSH host shell",
      expiresAtUtc: "2026-07-06T23:10:00Z",
    })

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/auth/cli-device/authorizations/review",
      expect.objectContaining({
        method: "POST",
        credentials: "include",
        cache: "no-store",
        headers: expect.objectContaining({
          "X-MEM-Operator-Request": "1",
          "Content-Type": "application/json",
        }),
        body: JSON.stringify({ userCode: "ABCD-EFGH" }),
      }),
    )
  })

  it("maps the stable step-up refusal without exposing a raw response body", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: "step_up_required",
      unsafeDetail: "must-not-reach-ui",
    }), {
      status: 403,
      headers: { "Content-Type": "application/json" },
    }))
    vi.stubGlobal("fetch", fetchMock)

    await expect(approveCliDeviceAuthorization("ABCD-EFGH")).resolves.toEqual({
      status: "step_up_required",
    })
  })

  it("denies with the same same-origin no-store request contract", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: "authorization_denied",
    }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    }))
    vi.stubGlobal("fetch", fetchMock)

    await expect(denyCliDeviceAuthorization("ABCD-EFGH")).resolves.toEqual({
      status: "authorization_denied",
    })

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/auth/cli-device/authorizations/deny",
      expect.objectContaining({
        method: "POST",
        credentials: "include",
        cache: "no-store",
        headers: expect.objectContaining({
          "X-MEM-Operator-Request": "1",
        }),
        body: JSON.stringify({ userCode: "ABCD-EFGH" }),
      }),
    )
  })
})
