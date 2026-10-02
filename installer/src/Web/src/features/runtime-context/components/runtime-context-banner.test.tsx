import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { createRuntimeContext } from "../runtime-context.test-fixture"
import { RuntimeContextWarningBanner } from "./runtime-context-banner"

function renderBanner() {
  return renderWithProviders(
    <MemoryRouter>
      <RuntimeContextWarningBanner />
    </MemoryRouter>,
  )
}

describe("RuntimeContextWarningBanner", () => {
  it("does not use a full-width banner for a healthy development runtime", async () => {
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
        }))
      }),
    )

    renderBanner()

    await waitFor(() => expect(requestCount).toBe(1))
    expect(screen.queryByTestId("runtime-context-warning-banner")).not.toBeInTheDocument()
  })

  it("does not render a warning banner for healthy production", async () => {
    let requestCount = 0
    server.use(
      http.get("/api/operator/runtime-context", () => {
        requestCount += 1
        return HttpResponse.json(createRuntimeContext())
      }),
    )

    renderBanner()

    await waitFor(() => expect(requestCount).toBe(1))
    expect(screen.queryByTestId("runtime-context-warning-banner")).not.toBeInTheDocument()
  })

  it("promotes an unsafe production ownership state even though production has no environment pill", async () => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          showDevelopmentBanner: false,
          mutationsAllowed: false,
          dockerOwnership: {
            state: "conflict",
            mutationsAllowed: false,
            developmentOverrideActive: false,
            competingContainers: ["mem-control-plane-dev"],
            warningCode: "control_plane_competing_controller_detected",
          },
        }),
      )),
    )

    renderBanner()

    const banner = await screen.findByRole("alert")
    expect(banner).toHaveTextContent("Control Plane runtime requires attention")
    expect(banner).toHaveTextContent("Containerized production")
    expect(screen.getByRole("link", { name: "Review runtime diagnostics" })).toHaveAttribute("href", "/diagnostics")
  })

  it("promotes server-owned runtime warnings and shared-development ownership overrides", async () => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          runtimeMode: "containerized-development",
          environmentName: "Development",
          stateRootKind: "development-volume",
          stateRootProfile: "default",
          showDevelopmentBanner: true,
          dockerOwnership: {
            state: "shared-development-override",
            mutationsAllowed: true,
            developmentOverrideActive: true,
            competingContainers: ["mem-control-plane"],
            warningCode: "control_plane_shared_docker_host_override_active",
          },
        }),
      )),
    )

    renderBanner()

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Runtime validation, Docker ownership, or Control Plane exposure requires attention",
    )
  })
  it("promotes an unsupported Control Plane host binding even when Docker ownership is healthy", async () => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          controlPlaneExposure: {
            state: "needs-attention",
            accessMode: "unsupported",
            hostAddress: "0.0.0.0",
            hostPort: 8443,
            bindingCount: 1,
            warningCode: "control_plane_exposure_wildcard_binding",
            isPrivate: false,
          },
        }),
      )),
    )

    renderBanner()

    const banner = await screen.findByRole("alert")
    expect(banner).toHaveTextContent("Control Plane runtime requires attention")
    expect(banner).toHaveTextContent("Control Plane exposure requires attention")
  })

})
