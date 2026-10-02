import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { changeOperatorPassword } from "@/features/auth/control-plane-auth"
import { OperatorChangePasswordDialog } from "@/features/auth/operator-change-password-dialog"
import { renderWithProviders } from "@/test/render-with-providers"

vi.mock("@/features/auth/control-plane-auth", () => ({
  changeOperatorPassword: vi.fn(),
}))

const changeOperatorPasswordMock = vi.mocked(changeOperatorPassword)

afterEach(() => {
  vi.clearAllMocks()
})

describe("OperatorChangePasswordDialog", () => {
  it("keeps the authenticated page covered until the successful-change callback replaces the document", async () => {
    const user = userEvent.setup()
    const onOpenChange = vi.fn()
    const onChanged = vi.fn()
    changeOperatorPasswordMock.mockResolvedValue({ status: "changed" })

    renderWithProviders(
      <OperatorChangePasswordDialog
        open
        onOpenChange={onOpenChange}
        onChanged={onChanged}
      />,
    )

    await user.type(screen.getByLabelText("New password"), "Different!Foundation456")
    await user.type(screen.getByLabelText("Confirm new password"), "Different!Foundation456")
    await user.click(screen.getByRole("button", { name: "Change password" }))

    await waitFor(() => {
      expect(onChanged).toHaveBeenCalledTimes(1)
    })

    expect(onOpenChange).not.toHaveBeenCalled()
    expect(screen.getByRole("dialog", { name: "Change password" })).toBeInTheDocument()
    expect(screen.queryByDisplayValue("Different!Foundation456")).not.toBeInTheDocument()
  })
})
