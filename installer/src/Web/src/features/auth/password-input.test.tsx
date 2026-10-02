import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { useState } from "react"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { PasswordInput } from "./password-input"

function PasswordHarness() {
  const [value, setValue] = useState("")

  return (
    <div>
      <label htmlFor="password-under-test">Password</label>
      <PasswordInput
        id="password-under-test"
        value={value}
        onChange={(event) => setValue(event.target.value)}
      />
      <button type="button" onClick={() => setValue("")}>Clear</button>
    </div>
  )
}

describe("PasswordInput", () => {
  it("is hidden by default and lets the operator show and hide the exact entered password", async () => {
    const user = userEvent.setup()

    renderWithProviders(<PasswordHarness />)

    const password = screen.getByLabelText("Password")
    expect(password).toHaveAttribute("type", "password")

    await user.type(password, "xA593N*\\ZfDL~4")
    await user.click(screen.getByRole("button", { name: "Show password" }))

    expect(password).toHaveAttribute("type", "text")
    expect(password).toHaveValue("xA593N*\\ZfDL~4")
    expect(screen.getByRole("button", { name: "Hide password" })).toHaveAttribute(
      "aria-pressed",
      "true",
    )

    await user.click(screen.getByRole("button", { name: "Hide password" }))
    expect(password).toHaveAttribute("type", "password")
  })

  it("returns to hidden when the owning form clears the password", async () => {
    const user = userEvent.setup()

    renderWithProviders(<PasswordHarness />)

    const password = screen.getByLabelText("Password")
    await user.type(password, "Temporary!Password123")
    await user.click(screen.getByRole("button", { name: "Show password" }))
    expect(password).toHaveAttribute("type", "text")

    await user.click(screen.getByRole("button", { name: "Clear" }))

    expect(password).toHaveValue("")
    expect(password).toHaveAttribute("type", "password")
    expect(screen.getByRole("button", { name: "Show password" })).toHaveAttribute(
      "aria-pressed",
      "false",
    )
  })
})
