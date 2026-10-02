import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { RuntimeContextDetailsProvider } from "@/features/runtime-context/components/runtime-context-details"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { DiagnosticsRuntimeCard } from "./diagnostics-runtime-card"

function renderRuntimeCard() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/diagnostics"]}>
      <RuntimeContextDetailsProvider>
        <DiagnosticsRuntimeCard />
      </RuntimeContextDetailsProvider>
    </MemoryRouter>,
  )
}

describe("DiagnosticsRuntimeCard", () => {
  it("keeps healthy runtime context compact until the operator asks for details", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({ configuredContainerName: "mem-control-plane" }),
      )),
    )

    renderRuntimeCard()

    expect(await screen.findByText("Containerized production")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Control Plane runtime" })).toBeInTheDocument()
    expect(screen.getByText("Embedded Web application")).toBeInTheDocument()
    expect(screen.getByText("Docker ownership: Exclusive")).toBeInTheDocument()
    expect(screen.getByText(/Control Plane access: SSH tunnel · Loopback only · 127\.0\.0\.1:8443/)).toBeInTheDocument()
    expect(screen.getByText("0.2.0-test · abc123")).toBeInTheDocument()
    expect(screen.queryByText("11111111-1111-1111-1111-111111111111")).not.toBeInTheDocument()

    const toggle = screen.getByRole("button", { name: "Show runtime details" })
    expect(toggle).toHaveAttribute("aria-expanded", "false")
    await user.click(toggle)

    expect(screen.getByText("11111111-1111-1111-1111-111111111111")).toBeInTheDocument()
    expect(screen.getByText("22222222-2222-2222-2222-222222222222")).toBeInTheDocument()
    expect(screen.getByText("Configured Control Plane container: mem-control-plane")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Hide runtime details" })).toHaveAttribute("aria-expanded", "true")
  })

  it("opens the same shared system-information drawer from Diagnostics", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(createRuntimeContext())),
    )

    renderRuntimeCard()

    await screen.findByText("Containerized production")
    await user.click(screen.getByRole("button", { name: "View system information" }))

    const dialog = await screen.findByRole("dialog", { name: "Control Plane runtime" })
    expect(dialog).toBeInTheDocument()
    expect(within(dialog).getByText("Control Plane instance")).toBeInTheDocument()
    expect(within(dialog).getByText("Control Plane access")).toBeInTheDocument()
    expect(within(dialog).getByText(/SSH tunnel · Loopback only · 127\.0\.0\.1:8443/)).toBeInTheDocument()
  })

  it("keeps competing Control Plane ownership and warnings visible while compact", async () => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          mutationsAllowed: false,
          dockerOwnership: {
            state: "conflict",
            mutationsAllowed: false,
            developmentOverrideActive: false,
            competingContainers: ["mem-installer"],
            warningCode: "control_plane_competing_controller_detected",
          },
        }),
      )),
    )

    renderRuntimeCard()

    expect(await screen.findByText("Docker ownership: Conflict — mutations blocked")).toBeInTheDocument()
    expect(screen.getByText("Warning")).toBeInTheDocument()
    expect(screen.getByText("Competing Control Plane containers: mem-installer")).toBeInTheDocument()
    expect(screen.getByText("control_plane_competing_controller_detected")).toBeInTheDocument()
    expect(screen.queryByText("11111111-1111-1111-1111-111111111111")).not.toBeInTheDocument()
  })

  it("fails honestly when the server runtime projection is unavailable", async () => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        { title: "Unavailable" },
        { status: 503 },
      )),
    )

    renderRuntimeCard()

    expect(await screen.findByText("Runtime context is unavailable")).toBeInTheDocument()
    expect(screen.getByRole("status")).toHaveAttribute("data-runtime-context-state", "unavailable")
    expect(screen.getByText(/Do not infer the active environment/)).toBeInTheDocument()
  })
  it("surfaces unsupported public Control Plane exposure as a runtime warning", async () => {
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

    renderRuntimeCard()

    expect(await screen.findByText(/Unsupported exposure · 0\.0\.0\.0:8443/)).toBeInTheDocument()
    expect(screen.getByText("Warning")).toBeInTheDocument()
    expect(screen.getByText("control_plane_exposure_wildcard_binding")).toBeInTheDocument()
  })

})
