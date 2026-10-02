import { screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"
import { InstallFailureGuidancePanel } from "./install-failure-guidance-panel"

describe("InstallFailureGuidancePanel", () => {
  it("makes a reusable certificate NPM-import failure obvious to the operator", () => {
    renderWithProviders(
      <InstallFailureGuidancePanel
        guidance={{
          title: "Certificate ready; NPM import failed",
          failedPhase: "Certificate → NPM import",
          summary:
            "MEM reached the NPM import phase. A reusable wildcard certificate is already stored by MEM, but NPM could not import it.",
          completedPhases: [
            "The wildcard certificate is stored by MEM and remains available for retry.",
          ],
          technicalReason: "NPM API authority was unavailable.",
          nextAction:
            "Confirm NPM is available through the Control Plane runtime, then retry Setup.",
          retryBehavior:
            "MEM will reuse the existing stored certificate and retry the NPM import. It will not request another certificate.",
          retryable: true,
        }}
      />,
    )

    expect(screen.getByText("Certificate ready; NPM import failed")).toBeInTheDocument()
    expect(screen.getByText("Certificate → NPM import")).toBeInTheDocument()
    expect(screen.getByText("What already succeeded")).toBeInTheDocument()
    expect(screen.getByText(/will not request another certificate/i)).toBeInTheDocument()
  })
})
