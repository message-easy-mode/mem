import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import {
  completeBootstrap,
  getControlPlaneSession,
  prepareFirstOwner,
  verifyBootstrapCode,
  verifyBootstrapTotp,
} from "../control-plane-auth"
import { BootstrapPage } from "./bootstrap-page"

vi.mock("../control-plane-auth", () => ({
  cancelBootstrap: vi.fn(),
  completeBootstrap: vi.fn(),
  getControlPlaneSession: vi.fn(),
  prepareFirstOwner: vi.fn(),
  verifyBootstrapCode: vi.fn(),
  verifyBootstrapTotp: vi.fn(),
}))

vi.mock("qrcode.react", () => ({
  QRCodeSVG: ({
    value,
    title,
  }: {
    value: string
    title?: string
  }) => (
    <svg
      aria-label={title}
      data-testid="totp-enrollment-qr-code"
      data-value={value}
      role="img"
    />
  ),
}))

const completeBootstrapMock = vi.mocked(completeBootstrap)
const getSessionMock = vi.mocked(getControlPlaneSession)
const prepareFirstOwnerMock = vi.mocked(prepareFirstOwner)
const verifyBootstrapCodeMock = vi.mocked(verifyBootstrapCode)
const verifyBootstrapTotpMock = vi.mocked(verifyBootstrapTotp)

const noOwnerSession = {
  authenticated: false,
  authenticationKind: null,
  displayName: null,
  roles: [],
  requiresFirstOwnerBootstrap: true,
  hasCompletedPlatformOwner: false,
}

afterEach(() => {
  vi.clearAllMocks()
})

describe("BootstrapPage authenticator enrolment", () => {
  it("shows a local QR code only after the server prepares the TOTP enrolment", async () => {
    const user = userEvent.setup()
    const authenticatorUri =
      "otpauth://totp/MEM%20Control%20Plane:admin?secret=BASE32&issuer=MEM%20Control%20Plane&digits=6"

    getSessionMock.mockResolvedValue(noOwnerSession)
    verifyBootstrapCodeMock.mockResolvedValue({
      expiresAtUtc: "2026-07-04T10:45:00Z",
    })
    prepareFirstOwnerMock.mockResolvedValue({
      username: "admin",
      manualEntryKey: "BASE32",
      authenticatorUri,
    })

    renderWithProviders(
      <MemoryRouter initialEntries={["/bootstrap"]}>
        <Routes>
          <Route path="/bootstrap" element={<BootstrapPage />} />
          <Route path="/" element={<div>Startup route</div>} />
          <Route path="/dashboard" element={<div>Dashboard</div>} />
          <Route path="/login" element={<div>Login</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.queryByTestId("totp-enrollment-qr-code")).not.toBeInTheDocument()

    await user.type(await screen.findByLabelText("One-time setup code"), "mem_test123")
    await user.click(screen.getByRole("button", { name: "Verify setup code" }))

    await user.type(await screen.findByLabelText("Owner username"), "admin")
    await user.type(screen.getByLabelText("Password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Confirm password"), "Secure!Foundation123")
    await user.click(screen.getByRole("button", { name: "Prepare authenticator setup" }))

    expect(await screen.findByRole("img", { name: "Scan this QR code" })).toHaveAttribute(
      "data-value",
      authenticatorUri,
    )
    expect(screen.getByText("Can't scan? Enter the details manually")).toBeInTheDocument()
    expect(screen.getByText("BASE32")).toBeInTheDocument()
  })

  it("returns first-owner completion to the canonical startup resolver", async () => {
    const user = userEvent.setup()

    getSessionMock.mockResolvedValue(noOwnerSession)
    verifyBootstrapCodeMock.mockResolvedValue({
      expiresAtUtc: "2026-07-04T10:45:00Z",
    })
    prepareFirstOwnerMock.mockResolvedValue({
      username: "admin",
      manualEntryKey: "BASE32",
      authenticatorUri: "otpauth://totp/MEM:admin?secret=BASE32",
    })
    verifyBootstrapTotpMock.mockResolvedValue(undefined)
    completeBootstrapMock.mockResolvedValue({
      username: "admin",
      recoveryCodes: ["AAAAA-BBBBB"],
    })

    renderWithProviders(
      <MemoryRouter initialEntries={["/bootstrap"]}>
        <Routes>
          <Route path="/bootstrap" element={<BootstrapPage />} />
          <Route path="/" element={<div>Startup route</div>} />
          <Route path="/dashboard" element={<div>Dashboard</div>} />
          <Route path="/login" element={<div>Login</div>} />
        </Routes>
      </MemoryRouter>,
    )

    await user.type(await screen.findByLabelText("One-time setup code"), "mem_test123")
    await user.click(screen.getByRole("button", { name: "Verify setup code" }))
    await user.type(await screen.findByLabelText("Owner username"), "admin")
    await user.type(screen.getByLabelText("Password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Confirm password"), "Secure!Foundation123")
    await user.click(screen.getByRole("button", { name: "Prepare authenticator setup" }))
    await user.type(await screen.findByLabelText("Authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify authenticator" }))
    await user.click(await screen.findByRole("checkbox"))
    await user.click(screen.getByRole("button", { name: "Finish and open MEM" }))

    expect(await screen.findByText("Startup route")).toBeInTheDocument()
    expect(screen.queryByText("Dashboard")).not.toBeInTheDocument()
  })
})
