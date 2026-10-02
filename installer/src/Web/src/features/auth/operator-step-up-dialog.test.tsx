import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { verifyOperatorStepUp } from "./control-plane-auth"
import { OperatorStepUpDialog } from "./operator-step-up-dialog"

vi.mock("./control-plane-auth", () => ({
  verifyOperatorStepUp: vi.fn(),
}))

const verifyStepUpMock = vi.mocked(verifyOperatorStepUp)

afterEach(() => {
  vi.clearAllMocks()
})

describe("OperatorStepUpDialog", () => {
  it("portals the viewport-level dialog outside the account-menu layout and keeps short screens scrollable", () => {
    renderWithProviders(
      <OperatorStepUpDialog open onOpenChange={vi.fn()} />,
    )

    const dialog = screen.getByRole("dialog")
    const viewportSurface = dialog.parentElement?.parentElement

    expect(viewportSurface?.parentElement).toBe(document.body)
    expect(viewportSurface).toHaveClass("fixed", "inset-0", "overflow-y-auto", "overscroll-contain")
    expect(dialog.parentElement).toHaveClass("min-h-full", "items-start", "justify-center")
  })

  it("uses only password and current TOTP, clears both after a rejected response, and does not offer recovery-code completion", async () => {
    const user = userEvent.setup()
    const onOpenChange = vi.fn()
    verifyStepUpMock.mockResolvedValue({ status: "invalid" })

    renderWithProviders(
      <OperatorStepUpDialog open onOpenChange={onOpenChange} />,
    )

    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    expect(verifyStepUpMock).toHaveBeenCalledWith("Secure!Foundation123", "123456")
    expect(await screen.findByText("The password or authenticator code was not accepted.")).toBeInTheDocument()
    expect(screen.getByLabelText("Current password")).toHaveValue("")
    expect(screen.getByLabelText("Current authenticator code")).toHaveValue("")
    expect(screen.queryByText("Use a recovery code instead")).not.toBeInTheDocument()
  })

  it("closes and resumes only a caller-supplied confirmed action after successful verification", async () => {
    const user = userEvent.setup()
    const onOpenChange = vi.fn()
    const onVerified = vi.fn()
    verifyStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-07-05T22:10:00Z",
    })

    renderWithProviders(
      <OperatorStepUpDialog
        open
        onOpenChange={onOpenChange}
        onVerified={onVerified}
      />,
    )

    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await waitFor(() => {
      expect(onVerified).toHaveBeenCalledTimes(1)
    })

    expect(onOpenChange).toHaveBeenCalledWith(false)
    expect(screen.queryByText("Identity verified")).not.toBeInTheDocument()
  })

  it("keeps no credential form visible after a short-lived session verification succeeds", async () => {
    const user = userEvent.setup()
    verifyStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-07-04T22:10:00Z",
    })

    renderWithProviders(
      <OperatorStepUpDialog open onOpenChange={vi.fn()} />,
    )

    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    expect(await screen.findByText("Identity verified")).toBeInTheDocument()
    expect(screen.queryByLabelText("Current password")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("Current authenticator code")).not.toBeInTheDocument()
  })
})
