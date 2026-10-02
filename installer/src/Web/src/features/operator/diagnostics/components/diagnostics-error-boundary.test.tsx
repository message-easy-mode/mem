import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"
import {
  DiagnosticsRouteErrorPage,
  DiagnosticsSectionErrorBoundary,
} from "./diagnostics-error-boundary"

afterEach(() => {
  window.localStorage.clear()
  vi.restoreAllMocks()
})

describe("DiagnosticsSectionErrorBoundary", () => {
  it("contains a failed section while keeping the surrounding route usable", async () => {
    const user = userEvent.setup()
    let shouldThrow = true
    vi.spyOn(console, "error").mockImplementation(() => undefined)

    function UnstableSection() {
      if (shouldThrow) {
        throw new Error("simulated diagnostics card failure")
      }

      return <p>Recovered diagnostics section</p>
    }

    renderWithProviders(
      <div>
        <h1>Diagnostics route header</h1>
        <DiagnosticsSectionErrorBoundary>
          <UnstableSection />
        </DiagnosticsSectionErrorBoundary>
        <p>Other diagnostics section remains visible</p>
      </div>,
    )

    expect(screen.getByText("Diagnostics route header")).toBeInTheDocument()
    expect(
      screen.getByText("Other diagnostics section remains visible"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("A diagnostics section could not be displayed"),
    ).toBeInTheDocument()

    shouldThrow = false
    await user.click(screen.getByRole("button", { name: "Try section again" }))
    expect(screen.getByText("Recovered diagnostics section")).toBeInTheDocument()
  })
})

describe("DiagnosticsRouteErrorPage", () => {
  it("renders a safe German route fallback without exposing an exception", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(<DiagnosticsRouteErrorPage />)

    expect(
      screen.getByText("Die Diagnose konnte nicht angezeigt werden"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        "MEM hat einen Darstellungsfehler im Browser eingegrenzt. Andere Bereiche der Steuerungsebene bleiben verfügbar.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText(/simulated diagnostics card failure/i)).not.toBeInTheDocument()
  })
})
