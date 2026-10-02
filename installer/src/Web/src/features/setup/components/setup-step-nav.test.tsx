import { afterEach, describe, expect, it } from "vitest"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen } from "@testing-library/react"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { SetupStepNav } from "./setup-step-nav"

afterEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("SetupStepNav accessibility", () => {
  it("labels the setup progress navigation and exposes the current step", () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/install/installation-123"]}>
        <Routes>
          <Route
            path="/setup/install/:installationId"
            element={<SetupStepNav />}
          />
        </Routes>
      </MemoryRouter>,
    )

    const nav = screen.getByRole("navigation", { name: "Setup progress" })
    expect(nav).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Install platform" })).toHaveAttribute(
      "aria-current",
      "step",
    )
    expect(screen.getByRole("link", { name: "Review" })).not.toHaveAttribute(
      "aria-current",
    )
    expect(screen.getByRole("link", { name: "Public domain" })).toBeInTheDocument()
  })

  it("renders the longer German release labels without changing the step contract", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/check-server"]}>
        <Routes>
          <Route path="/setup/check-server" element={<SetupStepNav />} />
        </Routes>
      </MemoryRouter>,
    )

    const nav = screen.getByRole("navigation", { name: "Einrichtungsfortschritt" })
    expect(nav).toHaveClass("flex-wrap")
    expect(screen.getByRole("link", { name: "Server prüfen" })).toHaveAttribute(
      "aria-current",
      "step",
    )
    expect(screen.getByRole("link", { name: "Öffentliche Domain" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Plattform installieren" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Abschließen" })).toBeInTheDocument()
  })
})
