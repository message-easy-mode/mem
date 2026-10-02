import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorRecoveryCodesDialog } from "./operator-recovery-codes-dialog"

describe("OperatorRecoveryCodesDialog", () => {
  it("keeps newly generated codes in the one-time dialog until they are explicitly acknowledged", async () => {
    const user = userEvent.setup()
    const onAcknowledge = vi.fn()

    renderWithProviders(
      <OperatorRecoveryCodesDialog
        recoveryCodes={["AAAAA-BBBBB", "CCCCC-DDDDD"]}
        onAcknowledge={onAcknowledge}
      />,
    )

    const dialog = screen.getByRole("dialog", { name: "Save your new recovery codes" })
    const viewportSurface = dialog.parentElement?.parentElement

    expect(viewportSurface?.parentElement).toBe(document.body)
    expect(viewportSurface).toHaveClass("fixed", "inset-0", "overflow-y-auto", "overscroll-contain")
    expect(screen.getByLabelText("New recovery codes").textContent).toBe(
      "AAAAA-BBBBB\nCCCCC-DDDDD",
    )

    const done = screen.getByRole("button", { name: "Done" })
    expect(done).toBeDisabled()

    await user.click(screen.getByRole("checkbox"))
    await user.click(done)

    expect(onAcknowledge).toHaveBeenCalledTimes(1)
  })
})
