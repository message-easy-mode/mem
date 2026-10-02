import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorEnrollmentPage } from "./operator-enrollment-page"

function renderEnrollmentPage() {
  renderWithProviders(
    <MemoryRouter initialEntries={["/enroll"]}>
      <Routes>
        <Route path="/enroll" element={<OperatorEnrollmentPage />} />
        <Route path="/dashboard" element={<div>MEM dashboard</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("OperatorEnrollmentPage", () => {
  it("completes pending operator password, TOTP, recovery-code and dashboard journey", async () => {
    const user = userEvent.setup()

    server.use(
      http.get("/api/auth/enrollment/state", () =>
        HttpResponse.json({
          active: false,
          username: null,
          stage: null,
          expiresAtUtc: null,
        }),
      ),
      http.post("/api/auth/enrollment/verify", async ({ request }) => {
        expect(await request.json()).toEqual({
          enrollmentCode: "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
        })

        return HttpResponse.json({
          active: true,
          username: "ops.reader",
          stage: "password",
          expiresAtUtc: "2026-07-04T13:00:00+00:00",
        })
      }),
      http.post("/api/auth/enrollment/prepare", async ({ request }) => {
        expect(await request.json()).toEqual({
          password: "Secure!Enrollment123",
        })

        return HttpResponse.json({
          username: "ops.reader",
          manualEntryKey: "JBSWY3DPEHPK3PXP",
          authenticatorUri: "otpauth://totp/MEM%20Control%20Plane%3Aops.reader?secret=JBSWY3DPEHPK3PXP&issuer=MEM%20Control%20Plane&digits=6",
        })
      }),
      http.post("/api/auth/enrollment/totp", async ({ request }) => {
        expect(await request.json()).toEqual({ code: "123456" })
        return new HttpResponse(null, { status: 204 })
      }),
      http.post("/api/auth/enrollment/complete", () =>
        HttpResponse.json({
          username: "ops.reader",
          recoveryCodes: ["code-one", "code-two"],
        }),
      ),
    )

    renderEnrollmentPage()

    await user.type(
      await screen.findByLabelText("One-time enrolment code"),
      "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
    )
    await user.click(screen.getByRole("button", { name: "Continue enrolment" }))

    await user.type(
      await screen.findByLabelText("New password"),
      "Secure!Enrollment123",
    )
    await user.type(
      screen.getByLabelText("Confirm new password"),
      "Secure!Enrollment123",
    )
    await user.click(screen.getByRole("button", { name: "Prepare authenticator" }))

    expect(await screen.findByText("JBSWY3DPEHPK3PXP")).toBeInTheDocument()

    await user.type(screen.getByLabelText("Authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify and create recovery codes" }))

    expect(await screen.findByLabelText("One-time recovery codes")).toHaveTextContent(
      "code-one",
    )
    expect(screen.getByRole("button", { name: "Finish and open MEM" })).toBeDisabled()

    await user.click(screen.getByLabelText("I have stored these recovery codes in a secure place."))
    await user.click(screen.getByRole("button", { name: "Finish and open MEM" }))

    expect(await screen.findByText("MEM dashboard")).toBeInTheDocument()
  })

  it("shows a one-time-code problem without exposing a continuation form", async () => {
    const user = userEvent.setup()

    server.use(
      http.get("/api/auth/enrollment/state", () =>
        HttpResponse.json({
          active: false,
          username: null,
          stage: null,
          expiresAtUtc: null,
        }),
      ),
      http.post("/api/auth/enrollment/verify", () =>
        HttpResponse.json(
          { status: "enrollment_code_invalid" },
          { status: 409 },
        ),
      ),
    )

    renderEnrollmentPage()

    await user.type(
      await screen.findByLabelText("One-time enrolment code"),
      "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
    )
    await user.click(screen.getByRole("button", { name: "Continue enrolment" }))

    expect(await screen.findByText(
      "This enrolment code is invalid, expired, already used, or no longer available.",
    )).toBeInTheDocument()
    expect(screen.queryByLabelText("New password")).not.toBeInTheDocument()
  })
})
