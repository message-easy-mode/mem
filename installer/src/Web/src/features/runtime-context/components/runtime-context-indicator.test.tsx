import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { createRuntimeContext } from "../runtime-context.test-fixture"
import { RuntimeContextDetailsProvider } from "./runtime-context-details"
import { RuntimeContextIndicator } from "./runtime-context-indicator"

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location-probe">{location.pathname}</span>
}

function renderIndicator() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks"]}>
      <RuntimeContextDetailsProvider>
        <RuntimeContextIndicator />
        <LocationProbe />
      </RuntimeContextDetailsProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe("RuntimeContextIndicator", () => {
  it("shows a compact local-development identity and opens the shared details drawer", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          runtimeMode: "local-development",
          environmentName: "Development",
          runningInContainer: false,
          stateRootKind: "repository-local",
          stateRootProfile: "default",
          uiDeliveryMode: "vite",
          configuredContainerName: null,
          showDevelopmentBanner: true,
        }),
      )),
    )

    renderIndicator()

    const trigger = await screen.findByRole("button", {
      name: "Open runtime information for Development · Local source",
    })
    expect(trigger).toHaveTextContent("Development · Local source")
    expect(trigger.querySelector(".lucide-chevron-down")).toBeNull()
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument()

    await user.click(trigger)

    const dialog = await screen.findByRole("dialog", { name: "Control Plane runtime" })
    expect(dialog).toHaveTextContent("Local source development")
    expect(dialog).toHaveTextContent("Vite UI")
    expect(dialog).toHaveTextContent("Repository-local state (installer/data)")
    expect(screen.getByRole("button", { name: "Open runtime diagnostics" })).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Close runtime information" }))
    expect(trigger).toHaveFocus()

    await user.click(trigger)
    await user.click(screen.getByRole("button", { name: "Open runtime diagnostics" }))
    expect(screen.getByTestId("location-probe")).toHaveTextContent("/diagnostics")
  })

  it("renders compact copyable runtime identities and the release codename in the shared drawer", async () => {
    const user = userEvent.setup()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText },
    })

    const controlPlaneInstanceId = "e53c5a42-7d75-449e-a9a3-f6e71ecef3d2"
    const apiProcessInstanceId = "0ae83861-e17a-42fd-94a3-4d972455625e"

    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          runtimeMode: "local-development",
          environmentName: "Development",
          runningInContainer: false,
          stateRootKind: "repository-local",
          stateRootProfile: "default",
          uiDeliveryMode: "vite",
          configuredContainerName: null,
          showDevelopmentBanner: true,
          controlPlaneInstanceId,
          apiProcessInstanceId,
          version: "0.2.0",
          commit: "bb1eefdfe32cf5797e446f5033387e0ce0a42973",
        }),
      )),
    )

    renderIndicator()

    await user.click(await screen.findByTestId("runtime-context-indicator"))
    const dialog = await screen.findByRole("dialog", { name: "Control Plane runtime" })

    expect(dialog).toHaveClass("inset-y-0", "right-0", "h-dvh")
    expect(dialog.className).toContain("sm:w-[min(48rem,calc(100vw-2rem))]")
    expect(dialog).toHaveTextContent("About this information")
    expect(dialog).toHaveTextContent("0.2.0 · baby-fish")
    expect(screen.getByTitle(controlPlaneInstanceId)).toHaveTextContent(controlPlaneInstanceId)
    expect(screen.getByTitle(apiProcessInstanceId)).toHaveTextContent(apiProcessInstanceId)
    expect(dialog).toHaveTextContent("Commit")
    expect(screen.getByTitle("bb1eefdfe32cf5797e446f5033387e0ce0a42973")).toHaveTextContent("bb1eefdfe32c…e0a42973")

    await user.click(screen.getByRole("button", { name: "Copy Control Plane instance" }))
    expect(writeText).toHaveBeenCalledWith(controlPlaneInstanceId)
    expect(screen.getByRole("button", { name: "Copied Control Plane instance" })).toBeInTheDocument()
  })

  it("labels containerized development distinctly", async () => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          runtimeMode: "containerized-development",
          environmentName: "Development",
          stateRootKind: "development-volume",
          stateRootProfile: "default",
          uiDeliveryMode: "embedded-spa",
          configuredContainerName: "mem-control-plane-dev",
          showDevelopmentBanner: true,
        }),
      )),
    )

    renderIndicator()

    expect(await screen.findByRole("button", {
      name: "Open runtime information for Development · Container",
    })).toHaveTextContent("Development · Container")
  })

  it("does not show an environment pill for healthy production", async () => {
    let requestCount = 0
    server.use(
      http.get("/api/operator/runtime-context", () => {
        requestCount += 1
        return HttpResponse.json(createRuntimeContext())
      }),
    )

    renderIndicator()

    await waitFor(() => {
      expect(requestCount).toBeGreaterThan(0)
    })
    expect(screen.queryByTestId("runtime-context-indicator")).not.toBeInTheDocument()
  })

  it("hides the compact pill when the server-owned runtime state requires attention", async () => {
    let requestCount = 0
    server.use(
      http.get("/api/operator/runtime-context", () => {
        requestCount += 1
        return HttpResponse.json(createRuntimeContext({
          runtimeMode: "local-development",
          environmentName: "Development",
          runningInContainer: false,
          stateRootKind: "repository-local",
          stateRootProfile: "default",
          uiDeliveryMode: "vite",
          configuredContainerName: null,
          showDevelopmentBanner: true,
          validationState: "warning",
          warnings: ["runtime_mode_defaulted_for_development"],
        }))
      }),
    )

    renderIndicator()

    await waitFor(() => {
      expect(requestCount).toBeGreaterThan(0)
    })
    expect(screen.queryByTestId("runtime-context-indicator")).not.toBeInTheDocument()
  })

  it("copies only the bounded safe runtime projection from the details drawer", async () => {
    const user = userEvent.setup()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText },
    })

    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          runtimeMode: "local-development",
          environmentName: "Development",
          runningInContainer: false,
          stateRootKind: "repository-local",
          stateRootProfile: "default",
          uiDeliveryMode: "vite",
          configuredContainerName: null,
          showDevelopmentBanner: true,
          restart: {
            kind: "local-process",
            guidanceCode: "restart_local_process",
            command: "secret-looking-restart-command",
            commandAvailable: true,
          },
        }),
      )),
    )

    renderIndicator()

    await user.click(await screen.findByTestId("runtime-context-indicator"))
    await user.click(screen.getByRole("button", { name: "Copy runtime context" }))

    expect(writeText).toHaveBeenCalledTimes(1)
    const copied = String(writeText.mock.calls[0]?.[0])
    expect(copied).toContain("Runtime mode: Local source development")
    expect(copied).toContain("Control Plane instance: 11111111-1111-1111-1111-111111111111")
    expect(copied).not.toContain("secret-looking-restart-command")
    expect(screen.getByRole("button", { name: "Copied" })).toBeInTheDocument()
  })
})
