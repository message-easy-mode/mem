import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import {
  changeOperatorPassword,
  regenerateOperatorRecoveryCodes,
  verifyOperatorStepUp,
} from "@/features/auth/control-plane-auth"
import { replaceWithFreshOperatorLogin } from "@/features/auth/operator-auth-navigation"
import { OperatorAccountMenu } from "@/features/auth/operator-account-menu"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { renderWithProviders } from "@/test/render-with-providers"

vi.mock("@/features/auth/control-plane-auth", () => ({
  changeOperatorPassword: vi.fn(),
  verifyOperatorStepUp: vi.fn(),
  regenerateOperatorRecoveryCodes: vi.fn(),
}))

vi.mock("@/features/auth/operator-auth-navigation", () => ({
  replaceWithFreshOperatorLogin: vi.fn(),
}))

const changeOperatorPasswordMock = vi.mocked(changeOperatorPassword)
const verifyOperatorStepUpMock = vi.mocked(verifyOperatorStepUp)
const regenerateOperatorRecoveryCodesMock = vi.mocked(regenerateOperatorRecoveryCodes)
const replaceWithFreshOperatorLoginMock = vi.mocked(replaceWithFreshOperatorLogin)

const operatorSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner", "operator"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

afterEach(() => {
  vi.clearAllMocks()
})

describe("OperatorAccountMenu", () => {
  it("shows the named operator and server-provided roles, then signs out", async () => {
    const user = userEvent.setup()
    const signOut = vi.fn().mockResolvedValue(undefined)

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={signOut}>
        <OperatorAccountMenu />
      </OperatorSessionProvider>,
    )

    await user.click(screen.getByRole("button", { name: "Open account menu" }))

    expect(screen.getAllByText("admin")).toHaveLength(2)
    expect(screen.getByText("Platform Owner")).toBeInTheDocument()
    expect(screen.getByText("Operator")).toBeInTheDocument()
    expect(screen.getByRole("menuitem", { name: "Verify identity" })).toHaveClass("cursor-pointer")
    expect(screen.getByRole("menuitem", { name: "Change password" })).toHaveClass("cursor-pointer")
    expect(screen.getByRole("menuitem", { name: "Regenerate recovery codes" })).toHaveClass("cursor-pointer")
    expect(screen.getByRole("menuitem", { name: "Sign out" })).toHaveClass("cursor-pointer")

    await user.click(screen.getByRole("menuitem", { name: "Verify identity" }))

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Cancel" }))

    await user.click(screen.getByRole("button", { name: "Open account menu" }))
    await user.click(screen.getByRole("menuitem", { name: "Sign out" }))

    await waitFor(() => {
      expect(signOut).toHaveBeenCalledTimes(1)
    })
  })

  it("requires fresh password and TOTP verification, then hard-navigates to a fresh login without a second logout request", async () => {
    const user = userEvent.setup()
    const signOut = vi.fn().mockResolvedValue(undefined)
    verifyOperatorStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-09-15T08:00:00Z",
    })
    changeOperatorPasswordMock.mockResolvedValue({ status: "changed" })

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={signOut}>
        <OperatorAccountMenu />
      </OperatorSessionProvider>,
    )

    await user.click(screen.getByRole("button", { name: "Open account menu" }))
    await user.click(screen.getByRole("menuitem", { name: "Change password" }))

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    expect(await screen.findByRole("dialog", { name: "Change password" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("New password"), "Different!Foundation456")
    await user.type(screen.getByLabelText("Confirm new password"), "Different!Foundation456")
    await user.click(screen.getByRole("button", { name: "Change password" }))

    await waitFor(() => {
      expect(changeOperatorPasswordMock).toHaveBeenCalledWith(
        "Different!Foundation456",
        "Different!Foundation456",
      )
      expect(replaceWithFreshOperatorLoginMock).toHaveBeenCalledTimes(1)
    })

    expect(signOut).not.toHaveBeenCalled()
    expect(screen.getByRole("dialog", { name: "Change password" })).toBeInTheDocument()
    expect(verifyOperatorStepUpMock).toHaveBeenCalledWith("Secure!Foundation123", "123456")
    expect(screen.queryByDisplayValue("Secure!Foundation123")).not.toBeInTheDocument()
    expect(screen.queryByDisplayValue("123456")).not.toBeInTheDocument()
    expect(screen.queryByDisplayValue("Different!Foundation456")).not.toBeInTheDocument()
  })

  it("clears rejected replacement passwords from the form and leaves the session signed in", async () => {
    const user = userEvent.setup()
    const signOut = vi.fn().mockResolvedValue(undefined)
    verifyOperatorStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-09-15T08:00:00Z",
    })
    changeOperatorPasswordMock.mockResolvedValue({ status: "not_accepted" })

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={signOut}>
        <OperatorAccountMenu />
      </OperatorSessionProvider>,
    )

    await user.click(screen.getByRole("button", { name: "Open account menu" }))
    await user.click(screen.getByRole("menuitem", { name: "Change password" }))
    await user.type(await screen.findByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await user.type(await screen.findByLabelText("New password"), "weak")
    await user.type(screen.getByLabelText("Confirm new password"), "weak")
    await user.click(screen.getByRole("button", { name: "Change password" }))

    expect(await screen.findByText("The new password does not meet the MEM password policy.")).toBeInTheDocument()
    expect(screen.getByLabelText("New password")).toHaveValue("")
    expect(screen.getByLabelText("Confirm new password")).toHaveValue("")
    expect(signOut).not.toHaveBeenCalled()
  })

  it("requires fresh identity verification before replacing recovery codes, then shows them once", async () => {
    const user = userEvent.setup()
    const signOut = vi.fn().mockResolvedValue(undefined)
    verifyOperatorStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-07-06T12:10:00Z",
    })
    regenerateOperatorRecoveryCodesMock.mockResolvedValue({
      status: "regenerated",
      recoveryCodes: ["AAAAA-BBBBB", "CCCCC-DDDDD"],
    })

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={signOut}>
        <OperatorAccountMenu />
      </OperatorSessionProvider>,
    )

    await user.click(screen.getByRole("button", { name: "Open account menu" }))
    await user.click(screen.getByRole("menuitem", { name: "Regenerate recovery codes" }))
    await user.click(screen.getByRole("button", { name: "Continue to verification" }))

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await waitFor(() => {
      expect(regenerateOperatorRecoveryCodesMock).toHaveBeenCalledTimes(1)
    })

    expect(await screen.findByRole("dialog", { name: "Save your new recovery codes" })).toBeInTheDocument()
    expect(screen.getByLabelText("New recovery codes")).toHaveTextContent("AAAAA-BBBBB")
    expect(screen.getByRole("button", { name: "Done" })).toBeDisabled()

    await user.click(screen.getByRole("checkbox"))
    await user.click(screen.getByRole("button", { name: "Done" }))

    expect(screen.queryByRole("dialog", { name: "Save your new recovery codes" })).not.toBeInTheDocument()
  })

  it("keeps the menu open and reports a failed sign-out", async () => {
    const user = userEvent.setup()
    const signOut = vi.fn().mockRejectedValue(new Error("network"))

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={signOut}>
        <OperatorAccountMenu />
      </OperatorSessionProvider>,
    )

    await user.click(screen.getByRole("button", { name: "Open account menu" }))
    await user.click(screen.getByRole("menuitem", { name: "Sign out" }))

    expect(await screen.findByRole("status")).toHaveTextContent(
      "MEM could not sign out this browser session. Try again.",
    )
  })
})
