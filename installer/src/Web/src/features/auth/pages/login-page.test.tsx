import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import {
  getControlPlaneSession,
  loginOperator,
  verifyOperatorRecoveryCode,
  verifyOperatorTotp,
} from "../control-plane-auth"
import { LoginPage } from "./login-page"

vi.mock("../control-plane-auth", () => ({
  getControlPlaneSession: vi.fn(),
  loginOperator: vi.fn(),
  verifyOperatorRecoveryCode: vi.fn(),
  verifyOperatorTotp: vi.fn(),
}))

const getSessionMock = vi.mocked(getControlPlaneSession)
const loginMock = vi.mocked(loginOperator)
const verifyRecoveryCodeMock = vi.mocked(verifyOperatorRecoveryCode)
const verifyTotpMock = vi.mocked(verifyOperatorTotp)

const completedOwnerSession = {
  authenticated: false,
  authenticationKind: null,
  displayName: null,
  roles: [],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

const authenticatedOwnerSession = {
  authenticated: true,
  authenticationKind: "operator" as const,
  displayName: "owner.alpha",
  roles: ["platform_owner"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

afterEach(() => {
  vi.clearAllMocks()
})

function renderLogin() {
  renderWithProviders(
    <MemoryRouter initialEntries={["/login"]}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/" element={<div>Startup route</div>} />
        <Route path="/bootstrap" element={<div>Bootstrap route</div>} />
        <Route path="/dashboard" element={<div>Dashboard route</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

async function continueToMfa(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText("Username"), "owner.alpha")
  await user.type(screen.getByLabelText("Password"), "Secure!Foundation123")
  await user.click(screen.getByRole("button", { name: "Continue" }))
}

describe("SEC-AUTH login abuse-resistance and recovery-code UX", () => {
  it("reveals and hides the exact password text without changing the submitted value", async () => {
    const user = userEvent.setup()
    getSessionMock.mockResolvedValue(completedOwnerSession)
    loginMock.mockResolvedValue({ status: "mfa_required" })

    renderLogin()

    await user.type(await screen.findByLabelText("Username"), "owner.alpha")
    const password = screen.getByLabelText("Password")
    await user.type(password, "xA593N*\\ZfDL~4")

    expect(password).toHaveAttribute("type", "password")
    await user.click(screen.getByRole("button", { name: "Show password" }))
    expect(password).toHaveAttribute("type", "text")
    expect(password).toHaveValue("xA593N*\\ZfDL~4")

    await user.click(screen.getByRole("button", { name: "Hide password" }))
    expect(password).toHaveAttribute("type", "password")

    await user.click(screen.getByRole("button", { name: "Continue" }))

    expect(loginMock).toHaveBeenCalledWith("owner.alpha", "xA593N*\\ZfDL~4")
    expect(await screen.findByLabelText("Authenticator code")).toBeInTheDocument()
  })

  it("shows a safe rate-limit message for the password stage", async () => {
    const user = userEvent.setup()
    getSessionMock.mockResolvedValue(completedOwnerSession)
    loginMock.mockResolvedValue({ status: "rate_limited" })

    renderLogin()

    await user.type(await screen.findByLabelText("Username"), "owner.alpha")
    await user.type(screen.getByLabelText("Password"), "wrong-password")
    await user.click(screen.getByRole("button", { name: "Continue" }))

    expect(
      await screen.findByText(
        "Too many sign-in attempts have been made. Wait a moment and try again.",
      ),
    ).toBeInTheDocument()
  })

  it("shows the same safe rate-limit message for the authenticator stage", async () => {
    const user = userEvent.setup()
    getSessionMock.mockResolvedValue(completedOwnerSession)
    loginMock.mockResolvedValue({ status: "mfa_required" })
    verifyTotpMock.mockResolvedValue({ status: "rate_limited" })

    renderLogin()

    await continueToMfa(user)

    await user.type(await screen.findByLabelText("Authenticator code"), "000000")
    await user.click(screen.getByRole("button", { name: "Verify and sign in" }))

    expect(
      await screen.findByText(
        "Too many sign-in attempts have been made. Wait a moment and try again.",
      ),
    ).toBeInTheDocument()
  })

  it("uses a recovery code only as the current MFA completion and clears it after a failed attempt", async () => {
    const user = userEvent.setup()
    getSessionMock.mockResolvedValue(completedOwnerSession)
    loginMock.mockResolvedValue({ status: "mfa_required" })
    verifyRecoveryCodeMock.mockResolvedValue({ status: "invalid" })

    renderLogin()

    await continueToMfa(user)

    await user.click(await screen.findByRole("button", { name: "Use a recovery code instead" }))

    expect(
      screen.getByText("Use one saved recovery code. Each recovery code can be used once."),
    ).toBeInTheDocument()

    const recoveryCode = screen.getByLabelText("Recovery code")
    await user.type(recoveryCode, "ABCDE-FGHIJ")
    await user.click(screen.getByRole("button", { name: "Use recovery code" }))

    expect(verifyRecoveryCodeMock).toHaveBeenCalledWith("ABCDE-FGHIJ")

    const rejection = await screen.findByRole("alert")
    expect(rejection).toHaveTextContent("The recovery code was not accepted.")
    expect(recoveryCode).toHaveAttribute("aria-invalid", "true")
    expect(recoveryCode).toHaveAttribute("aria-describedby", "login-recovery-code-error")
    expect(recoveryCode).toHaveValue("")

    await user.type(recoveryCode, "NEXT-CODE")
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
    expect(recoveryCode).not.toHaveAttribute("aria-invalid")
  })

  it("completes the current MFA challenge with a valid recovery code and reaches the requested route", async () => {
    const user = userEvent.setup()
    getSessionMock.mockResolvedValue(completedOwnerSession)
    loginMock.mockResolvedValue({ status: "mfa_required" })
    verifyRecoveryCodeMock.mockResolvedValue({
      status: "authenticated",
      session: authenticatedOwnerSession,
    })

    renderLogin()

    await continueToMfa(user)
    await user.click(await screen.findByRole("button", { name: "Use a recovery code instead" }))
    await user.type(screen.getByLabelText("Recovery code"), "ABCDE-FGHIJ")
    await user.click(screen.getByRole("button", { name: "Use recovery code" }))

    expect(await screen.findByText("Startup route")).toBeInTheDocument()
  })
})
