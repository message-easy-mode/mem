import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { http, HttpResponse } from "msw"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { NpmSettingsPage } from "./npm-settings-page"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({
    open,
    onOpenChange,
    onVerified,
  }: {
    open: boolean
    onOpenChange?: (open: boolean) => void
    onVerified?: () => void
  }) => open ? (
    <button
      type="button"
      onClick={() => {
        // Match the real dialog ordering: close first, then notify the caller
        // that the step-up grant was verified.
        onOpenChange?.(false)
        onVerified?.()
      }}
    >
      Complete step-up
    </button>
  ) : null,
}))

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

function projection(email = "admin@deltabox.dev") {
  return {
    runtimeStatus: "Running",
    runtimeExists: true,
    running: true,
    runtimeState: "running",
    runtimeImage: "jc21/nginx-proxy-manager:2.15.1",
    approvedRuntimeImage: "jc21/nginx-proxy-manager:2.15.1",
    approvedRuntimeVersion: "2.15.1",
    runtimeImageAligned: true,
    browserUrl: "http://127.0.0.1:81",
    administratorEmail: email,
    credentialStored: true,
    credentialStatus: "Verified",
    lastVerifiedAtUtc: "2026-08-12T10:49:25Z",
    warning: null,
  }
}

function renderPage(session = ownerSession) {
  renderWithProviders(
    <MemoryRouter initialEntries={["/settings/nginx-proxy-manager"]}>
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <NpmSettingsPage />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

describe("NpmSettingsPage", () => {
  it("shows the verified protected credential and server-owned NPM browser authority", async () => {
    server.use(
      http.get("/api/operator/npm", () => HttpResponse.json(projection())),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Nginx Proxy Manager" })).toBeInTheDocument()
    expect(await screen.findByText("admin@deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("Verified")).toBeInTheDocument()
    expect(screen.getByText("••••••••••••••••")).toBeInTheDocument()
    expect(screen.getAllByText("jc21/nginx-proxy-manager:2.15.1")).toHaveLength(2)
    expect(screen.getByText("Version 2.15.1")).toBeInTheDocument()
    expect(screen.getByText("Aligned")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open Nginx Proxy Manager" })).toHaveAttribute(
      "href",
      "http://127.0.0.1:81",
    )
  })

  it("requires step-up before revealing the stored password and never shows it beforehand", async () => {
    const user = userEvent.setup()
    let revealCalls = 0

    server.use(
      http.get("/api/operator/npm", () => HttpResponse.json(projection())),
      http.post("/api/operator/npm/credential/reveal", () => {
        revealCalls++
        if (revealCalls === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }

        return HttpResponse.json({
          administratorEmail: "admin@deltabox.dev",
          password: "revealed-npm-password",
        })
      }),
    )

    renderPage()

    await screen.findByText("admin@deltabox.dev")
    expect(screen.queryByDisplayValue("revealed-npm-password")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Reveal password" }))
    expect(await screen.findByRole("button", { name: "Complete step-up" })).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByDisplayValue("revealed-npm-password")).toBeInTheDocument()
    expect(revealCalls).toBe(2)
  })

  it("retries a confirmed replacement after step-up and refreshes the verified safe projection", async () => {
    const user = userEvent.setup()
    let updated = false
    let updateCalls = 0

    server.use(
      http.get("/api/operator/npm", () =>
        HttpResponse.json(projection(updated ? "replacement@deltabox.dev" : "admin@deltabox.dev")),
      ),
      http.put("/api/operator/npm/credential", async ({ request }) => {
        updateCalls++
        const body = await request.json()
        expect(body).toEqual({
          email: "replacement@deltabox.dev",
          password: "replacement-password",
        })

        if (updateCalls === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }

        updated = true
        return HttpResponse.json({
          administratorEmail: "replacement@deltabox.dev",
          credentialStored: true,
          status: "Verified",
          verifiedAtUtc: "2026-08-12T11:00:00Z",
        })
      }),
    )

    renderPage()

    await screen.findByText("admin@deltabox.dev")
    await user.click(screen.getByRole("button", { name: "Update stored credential" }))

    const email = screen.getByLabelText("Administrator email")
    await user.clear(email)
    await user.type(email, "replacement@deltabox.dev")
    await user.type(screen.getByLabelText("Administrator password"), "replacement-password")
    await user.click(screen.getByRole("button", { name: "Verify and save credential" }))

    expect(await screen.findByRole("button", { name: "Complete step-up" })).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("NPM credential updated")).toBeInTheDocument()
    expect(await screen.findByText("replacement@deltabox.dev")).toBeInTheDocument()
    expect(updateCalls).toBe(2)
  })

  it("shows runtime pin drift when Docker is not running the MEM-approved NPM release", async () => {
    server.use(
      http.get("/api/operator/npm", () => HttpResponse.json({
        ...projection(),
        runtimeImage: "jc21/nginx-proxy-manager:2.14.0",
        runtimeImageAligned: false,
        warning: "NPM is running an older image.",
      })),
    )

    renderPage()

    expect(await screen.findByText("jc21/nginx-proxy-manager:2.14.0")).toBeInTheDocument()
    expect(screen.getByText("Needs attention")).toBeInTheDocument()
    expect(screen.getByText("NPM runtime needs attention")).toBeInTheDocument()
  })

  it("blocks non-owner sessions from the installed NPM credential surface", () => {
    renderPage(operatorSession)
    expect(screen.getByText("Settings are restricted")).toBeInTheDocument()
  })
})
