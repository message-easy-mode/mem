import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"
import { DiagnosticsOutageFallback } from "./diagnostics-outage-fallback"

describe("DiagnosticsOutageFallback", () => {
  it("provides Portainer, docker logs, and persistent CLEF guidance", async () => {
    const user = userEvent.setup()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText },
    })

    renderWithProviders(<DiagnosticsOutageFallback />)

    expect(screen.getByText("Control-plane diagnostics are unavailable")).toBeInTheDocument()
    expect(screen.getByText("Use Portainer during a total API outage")).toBeInTheDocument()
    expect(screen.getByText("sudo docker logs --tail 500 mem-control-plane")).toBeInTheDocument()
    expect(screen.getByText("/data/logs/control-plane/mem-control-plane-.clef")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Copy command" }))
    expect(writeText).toHaveBeenCalledWith("sudo docker logs --tail 500 mem-control-plane")
    expect(screen.getByRole("button", { name: "Copied" })).toBeInTheDocument()
  })
})
