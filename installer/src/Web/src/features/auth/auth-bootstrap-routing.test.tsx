import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { AuthGuard } from "./auth-guard"
import {
  getControlPlaneSession,
  logoutControlPlane,
} from "./control-plane-auth"
import { useOperatorSession } from "./operator-session-context"
import { BootstrapPage } from "./pages/bootstrap-page"
import { LoginPage } from "./pages/login-page"
import { UnlockPage } from "./pages/unlock-page"

vi.mock("./control-plane-auth", () => ({
  cancelBootstrap: vi.fn(),
  completeBootstrap: vi.fn(),
  getControlPlaneSession: vi.fn(),
  loginOperator: vi.fn(),
  logoutControlPlane: vi.fn(),
  prepareFirstOwner: vi.fn(),
  verifyBootstrapCode: vi.fn(),
  verifyBootstrapTotp: vi.fn(),
  verifyOperatorTotp: vi.fn(),
}))

const getSessionMock = vi.mocked(getControlPlaneSession)
const logoutMock = vi.mocked(logoutControlPlane)

const noOwnerSession = {
  authenticated: false,
  authenticationKind: null,
  displayName: null,
  roles: [],
  requiresFirstOwnerBootstrap: true,
  hasCompletedPlatformOwner: false,
}

const completedOwnerSession = {
  authenticated: false,
  authenticationKind: null,
  displayName: null,
  roles: [],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

const operatorSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

const installerTransitionSession = {
  authenticated: true as const,
  authenticationKind: "installer_transition" as const,
  displayName: "MEM Control Plane Setup",
  roles: [],
  requiresFirstOwnerBootstrap: true,
  hasCompletedPlatformOwner: false,
}

afterEach(() => {
  vi.clearAllMocks()
})

function SignedInProbe() {
  const { session, signOut } = useOperatorSession()

  return (
    <>
      <div>Signed in as {session.displayName}</div>
      <button type="button" onClick={() => void signOut()}>
        Sign out probe
      </button>
    </>
  )
}

function renderProtectedRoute(element = <div>Protected dashboard</div>) {
  renderWithProviders(
    <MemoryRouter initialEntries={["/dashboard"]}>
      <Routes>
        <Route
          path="/dashboard"
          element={
            <AuthGuard>
              {element}
            </AuthGuard>
          }
        />
        <Route path="/bootstrap" element={<div>Scoped bootstrap route</div>} />
        <Route path="/login" element={<div>Named login route</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("SEC-AUTH browser routing and normal operator sessions", () => {
  it("routes a no-owner control plane into scoped bootstrap instead of generic unlock", async () => {
    getSessionMock.mockResolvedValue(noOwnerSession)

    renderProtectedRoute()

    expect(await screen.findByText("Scoped bootstrap route")).toBeInTheDocument()
    expect(screen.queryByText("Protected dashboard")).not.toBeInTheDocument()
  })

  it("routes an established control plane to named login", async () => {
    getSessionMock.mockResolvedValue(completedOwnerSession)

    renderProtectedRoute()

    expect(await screen.findByText("Named login route")).toBeInTheDocument()
  })

  it("does not treat a transitional installer cookie as a normal browser operator session", async () => {
    getSessionMock.mockResolvedValue(installerTransitionSession)

    renderProtectedRoute()

    expect(await screen.findByText("Scoped bootstrap route")).toBeInTheDocument()
    expect(screen.queryByText("Protected dashboard")).not.toBeInTheDocument()
  })

  it("does not present normal sign-in before the first owner exists", async () => {
    getSessionMock.mockResolvedValue(noOwnerSession)

    renderWithProviders(
      <MemoryRouter initialEntries={["/login"]}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route path="/bootstrap" element={<div>Scoped bootstrap route</div>} />
          <Route path="/dashboard" element={<div>Protected dashboard</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("Scoped bootstrap route")).toBeInTheDocument()
    expect(screen.queryByLabelText("Username")).not.toBeInTheDocument()
  })

  it("does not expose first-owner setup after a Platform Owner exists", async () => {
    getSessionMock.mockResolvedValue(completedOwnerSession)

    renderWithProviders(
      <MemoryRouter initialEntries={["/bootstrap"]}>
        <Routes>
          <Route path="/bootstrap" element={<BootstrapPage />} />
          <Route path="/login" element={<div>Named login route</div>} />
          <Route path="/dashboard" element={<div>Protected dashboard</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("Named login route")).toBeInTheDocument()
    expect(screen.queryByLabelText("One-time setup code")).not.toBeInTheDocument()
  })

  it("forwards legacy browser unlock bookmarks into scoped bootstrap", () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/unlock"]}>
        <Routes>
          <Route path="/unlock" element={<UnlockPage />} />
          <Route path="/bootstrap" element={<div>Scoped bootstrap route</div>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByText("Scoped bootstrap route")).toBeInTheDocument()
  })

  it("redirects to named login after an authenticated operator signs out", async () => {
    const user = userEvent.setup()
    getSessionMock.mockResolvedValue(operatorSession)
    logoutMock.mockResolvedValue(undefined)

    renderProtectedRoute(<SignedInProbe />)

    expect(await screen.findByText("Signed in as admin")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Sign out probe" }))

    await waitFor(() => {
      expect(logoutMock).toHaveBeenCalledTimes(1)
    })
    expect(await screen.findByText("Named login route")).toBeInTheDocument()
  })

  it("redirects an expired operator session when the browser returns to the foreground", async () => {
    getSessionMock
      .mockResolvedValueOnce(operatorSession)
      .mockResolvedValueOnce(completedOwnerSession)

    renderProtectedRoute(<SignedInProbe />)

    expect(await screen.findByText("Signed in as admin")).toBeInTheDocument()

    window.dispatchEvent(new Event("focus"))

    await waitFor(
      () => {
        expect(getSessionMock).toHaveBeenCalledTimes(2)
      },
      { timeout: 5_000 },
    )
    expect(
      await screen.findByText("Named login route", {}, { timeout: 5_000 }),
    ).toBeInTheDocument()
  })
})
