import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import {
  getSecuritySettings,
  isStepUpRequiredSecuritySettingsProblem,
  updateHighRiskStepUpSettings,
} from "./security-settings.api"

describe("security-settings.api", () => {
  it("reads server-owned high-risk step-up settings", async () => {
    server.use(
      http.get("/api/security/settings", () =>
        HttpResponse.json({
          highRiskStepUp: {
            required: true,
            reuseVerificationMinutes: 15,
            allowedReuseVerificationMinutes: [5, 15, 30, 60],
            isDefaulted: false,
            updatedAtUtc: "2026-07-08T12:00:00+00:00",
            updatedByOperatorId: "owner-1",
          },
        }),
      ),
    )

    await expect(getSecuritySettings()).resolves.toEqual({
      highRiskStepUp: {
        required: true,
        reuseVerificationMinutes: 15,
        allowedReuseVerificationMinutes: [5, 15, 30, 60],
        isDefaulted: false,
        updatedAtUtc: "2026-07-08T12:00:00+00:00",
        updatedByOperatorId: "owner-1",
      },
    })
  })

  it("preserves step-up-required status for the settings page verifier", async () => {
    server.use(
      http.patch("/api/security/settings/high-risk-step-up", () =>
        HttpResponse.json({ status: "step_up_required" }, { status: 403 }),
      ),
    )

    try {
      await updateHighRiskStepUpSettings({
        required: false,
        reuseVerificationMinutes: 15,
      })
      throw new Error("expected_update_to_fail")
    } catch (error) {
      expect(isStepUpRequiredSecuritySettingsProblem(error)).toBe(true)
    }
  })
})
