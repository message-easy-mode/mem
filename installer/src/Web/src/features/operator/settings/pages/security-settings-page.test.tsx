import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { SecuritySettingsPage } from "./security-settings-page"

const ownerSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

const operatorSession = {
  ...ownerSession,
  roles: ["operator"],
}

function renderSettings(session = ownerSession) {
  renderWithProviders(
    <MemoryRouter>
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <SecuritySettingsPage />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

describe("SecuritySettingsPage", () => {
  it("renders the recommended policy and reuse window for Platform Owners", async () => {
    server.use(
      http.get("/api/security/settings", () =>
        HttpResponse.json({
          highRiskStepUp: {
            required: true,
            reuseVerificationMinutes: 15,
            allowedReuseVerificationMinutes: [5, 15, 30, 60],
            isDefaulted: true,
            updatedAtUtc: null,
            updatedByOperatorId: null,
          },
        }),
      ),
    )

    renderSettings()

    expect(await screen.findByRole("heading", { name: "Security & access" })).toBeInTheDocument()
    expect(await screen.findByText("High-risk identity verification")).toBeInTheDocument()
    expect(screen.getByText("Required — recommended")).toBeInTheDocument()
    expect(screen.getByText("15 minutes — recommended")).toBeInTheDocument()
  })

  it("warns before saving Not required", async () => {
    const user = userEvent.setup()

    server.use(
      http.get("/api/security/settings", () =>
        HttpResponse.json({
          highRiskStepUp: {
            required: true,
            reuseVerificationMinutes: 15,
            allowedReuseVerificationMinutes: [5, 15, 30, 60],
            isDefaulted: false,
            updatedAtUtc: "2026-07-08T12:00:00+00:00",
            updatedByOperatorId: "owner-1",
          },
        }),
      ),
    )

    renderSettings()

    await screen.findByRole("heading", { name: "Security & access" })
    await screen.findByText("High-risk identity verification")
    await user.click(await screen.findByLabelText(/Not required/i))

    expect(screen.getByText("This reduces protection")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Save security settings" }))

    expect(screen.getByText("Set high-risk verification to Not required?")).toBeInTheDocument()
  })

  it("blocks non-owner sessions from the settings surface", () => {
    renderSettings(operatorSession)

    expect(screen.getByText("Settings are restricted")).toBeInTheDocument()
  })
})
