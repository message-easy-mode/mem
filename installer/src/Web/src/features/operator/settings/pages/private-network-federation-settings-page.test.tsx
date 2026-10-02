import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { PrivateNetworkFederationSettingsPage } from "./private-network-federation-settings-page"

const ownerSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

function renderPage() {
  renderWithProviders(
    <MemoryRouter initialEntries={["/settings/network-security"]}>
      <OperatorSessionProvider session={ownerSession} signOut={async () => undefined}>
        <PrivateNetworkFederationSettingsPage />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

function inventory() {
  return {
    source: "control-plane",
    status: "ok",
    stacks: [{
      runtimeStackId: "stack-1",
      slug: "demo-stack",
      matrixServerName: "matrix-demo-stack.deltabox.dev",
      federationMode: "restricted",
      federationConfigurationState: "healthy",
      configurationState: "healthy",
      currentExceptions: [],
      matrixContainerRunning: true,
      latestOperationId: null,
      latestOperationStatus: null,
      latestOperationStep: null,
      problemCode: null,
      detail: null,
    }],
  }
}

describe("PrivateNetworkFederationSettingsPage", () => {
  it("reviews and applies one exact private address from Settings", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/internal/host-agent/security/private-network-federation", () =>
        HttpResponse.json(inventory())),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/review",
        () => HttpResponse.json({
          source: "control-plane",
          status: "ready",
          runtimeStackId: "stack-1",
          slug: "demo-stack",
          matrixServerName: "matrix-demo-stack.deltabox.dev",
          action: "add",
          canonicalAddress: "10.0.0.238",
          canonicalCidr: "10.0.0.238/32",
          currentExceptions: [],
          proposedExceptions: ["10.0.0.238/32"],
          restartRequired: true,
          noChange: false,
          reviewHash: "sha256:review",
          confirmationText: "Allow exact address and restart Matrix.",
        })),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        () => HttpResponse.json({
          source: "control-plane",
          status: "succeeded",
          operationId: "operation-1",
          runtimeStackId: "stack-1",
          slug: "demo-stack",
          action: "add",
          canonicalCidr: "10.0.0.238/32",
          observedExceptions: ["10.0.0.238/32"],
          rollbackAttempted: false,
          rollbackSucceeded: null,
          errorCode: null,
          detail: "Exact exception active.",
        })),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Network security" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: /Security & access/ })).toHaveAttribute("href", "/settings/security-access")
    await user.type(await screen.findByLabelText("Exact private address"), "10.0.0.238")
    await user.click(screen.getByRole("button", { name: "Review addition" }))

    expect((await screen.findAllByText("10.0.0.238/32")).length).toBeGreaterThanOrEqual(1)
    await user.click(screen.getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Private network exception applied")).toBeInTheDocument()
    expect(screen.getByText(/operation-1/)).toBeInTheDocument()
  })

  it("reconciles a lost apply response against healthy server-owned state instead of showing a false failure", async () => {
    const user = userEvent.setup()
    let applied = false
    server.use(
      http.get("/internal/host-agent/security/private-network-federation", () => {
        const value = inventory()
        return HttpResponse.json({
          ...value,
          stacks: value.stacks.map((stack) => ({
            ...stack,
            currentExceptions: applied ? ["10.0.0.238/32"] : [],
          })),
        })
      }),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/review",
        () => HttpResponse.json({
          source: "control-plane",
          status: "ready",
          runtimeStackId: "stack-1",
          slug: "demo-stack",
          matrixServerName: "matrix-demo-stack.deltabox.dev",
          action: "add",
          canonicalAddress: "10.0.0.238",
          canonicalCidr: "10.0.0.238/32",
          currentExceptions: [],
          proposedExceptions: ["10.0.0.238/32"],
          restartRequired: true,
          noChange: false,
          reviewHash: "sha256:review",
          confirmationText: "Allow exact address and restart Matrix.",
        })),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        () => {
          applied = true
          return new HttpResponse("response unavailable", {
            status: 200,
            headers: { "Content-Type": "text/plain" },
          })
        },
      ),
    )

    renderPage()

    await user.type(await screen.findByLabelText("Exact private address"), "10.0.0.238")
    await user.click(screen.getByRole("button", { name: "Review addition" }))
    await user.click(await screen.findByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Private network exception confirmed")).toBeInTheDocument()
    expect(screen.getByText(/refreshed server-owned state proves that the exact exception is active/i)).toBeInTheDocument()
    expect(screen.queryByText("The private network exception could not be changed")).not.toBeInTheDocument()
    expect((await screen.findAllByText("10.0.0.238/32")).length).toBeGreaterThanOrEqual(1)
  })

  it("keeps the failure visible when refreshed state does not prove the requested change", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/internal/host-agent/security/private-network-federation", () =>
        HttpResponse.json(inventory())),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/review",
        () => HttpResponse.json({
          source: "control-plane",
          status: "ready",
          runtimeStackId: "stack-1",
          slug: "demo-stack",
          matrixServerName: "matrix-demo-stack.deltabox.dev",
          action: "add",
          canonicalAddress: "10.0.0.238",
          canonicalCidr: "10.0.0.238/32",
          currentExceptions: [],
          proposedExceptions: ["10.0.0.238/32"],
          restartRequired: true,
          noChange: false,
          reviewHash: "sha256:review",
          confirmationText: "Allow exact address and restart Matrix.",
        })),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        () => HttpResponse.text("upstream response unavailable", { status: 502 }),
      ),
    )

    renderPage()

    await user.type(await screen.findByLabelText("Exact private address"), "10.0.0.238")
    await user.click(screen.getByRole("button", { name: "Review addition" }))
    await user.click(await screen.findByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("The private network exception could not be changed")).toBeInTheDocument()
    expect(screen.queryByText("Private network exception confirmed")).not.toBeInTheDocument()
  })

  it.skip("waits for a Local-only removal to settle before reporting an uncertain apply as failed", async () => {
    const user = userEvent.setup()
    let applied = false
    let postApplyReads = 0

    server.use(
      http.get("/internal/host-agent/security/private-network-federation", () => {
        const value = inventory()
        if (!applied) {
          return HttpResponse.json({
            ...value,
            stacks: value.stacks.map((stack) => ({
              ...stack,
              federationMode: "local_only",
              currentExceptions: ["10.0.0.238/32"],
            })),
          })
        }

        postApplyReads += 1
        return HttpResponse.json({
          ...value,
          stacks: value.stacks.map((stack) => ({
            ...stack,
            federationMode: "local_only",
            currentExceptions: [],
            federationConfigurationState: postApplyReads === 1 ? "incomplete" : "healthy",
          })),
        })
      }),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/review",
        () => HttpResponse.json({
          source: "control-plane",
          status: "ready",
          runtimeStackId: "stack-1",
          slug: "demo-stack",
          matrixServerName: "matrix-demo-stack.deltabox.dev",
          action: "remove",
          canonicalAddress: "10.0.0.238",
          canonicalCidr: "10.0.0.238/32",
          currentExceptions: ["10.0.0.238/32"],
          proposedExceptions: [],
          restartRequired: true,
          noChange: false,
          reviewHash: "sha256:remove-review",
          confirmationText: "Remove exact address and restart Matrix.",
        })),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        () => {
          applied = true
          return new HttpResponse("response unavailable", {
            status: 200,
            headers: { "Content-Type": "text/plain" },
          })
        },
      ),
    )

    renderPage()

    await user.click(await screen.findByRole("button", { name: "Review removal" }))
    await user.click(await screen.findByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Confirming final server state")).toBeInTheDocument()
    expect(await screen.findByText("Private network exception confirmed", {}, { timeout: 3000 })).toBeInTheDocument()
    expect(screen.getByText(/refreshed server-owned state proves that the exact exception is absent/i)).toBeInTheDocument()
    expect(screen.queryByText("The private network exception could not be changed")).not.toBeInTheDocument()
    expect(postApplyReads).toBeGreaterThanOrEqual(2)
  })

  it("reviews and removes an existing exact exception directly while Local-only is healthy", async () => {
    const user = userEvent.setup()
    let reviewedInput: unknown = null
    let appliedInput: unknown = null

    server.use(
      http.get("/internal/host-agent/security/private-network-federation", () => {
        const value = inventory()
        return HttpResponse.json({
          ...value,
          stacks: value.stacks.map((stack) => ({
            ...stack,
            federationMode: "local_only",
            currentExceptions: ["10.0.0.238/32"],
          })),
        })
      }),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/review",
        async ({ request }) => {
          reviewedInput = await request.json()
          return HttpResponse.json({
            source: "control-plane",
            status: "ready",
            runtimeStackId: "stack-1",
            slug: "demo-stack",
            matrixServerName: "matrix-demo-stack.deltabox.dev",
            action: "remove",
            canonicalAddress: "10.0.0.238",
            canonicalCidr: "10.0.0.238/32",
            currentExceptions: ["10.0.0.238/32"],
            proposedExceptions: [],
            restartRequired: true,
            noChange: false,
            reviewHash: "sha256:remove-review",
            confirmationText: "Remove exact address and restart Matrix.",
          })
        },
      ),
      http.post(
        "/internal/host-agent/security/private-network-federation/demo-stack/apply",
        async ({ request }) => {
          appliedInput = await request.json()
          return HttpResponse.json({
            source: "control-plane",
            status: "succeeded",
            operationId: "operation-remove-1",
            runtimeStackId: "stack-1",
            slug: "demo-stack",
            action: "remove",
            canonicalCidr: "10.0.0.238/32",
            observedExceptions: [],
            rollbackAttempted: false,
            rollbackSucceeded: null,
            errorCode: null,
            detail: "Exact exception removed.",
          })
        },
      ),
    )

    renderPage()

    expect(await screen.findByText("Local-only cleanup")).toBeInTheDocument()
    expect(screen.queryByLabelText("Exact private address")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Review removal" }))

    expect(reviewedInput).toEqual({
      address: "10.0.0.238",
      action: "remove",
    })
    expect(await screen.findByText("Remove exact address and restart Matrix.")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Confirm and apply" }))

    expect(await screen.findByText("Private network exception applied")).toBeInTheDocument()
    expect(appliedInput).toMatchObject({
      address: "10.0.0.238",
      action: "remove",
      reviewHash: "sha256:remove-review",
    })
  })

})
