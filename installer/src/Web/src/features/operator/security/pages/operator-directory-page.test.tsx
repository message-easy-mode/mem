import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { http, HttpResponse } from "msw"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { replaceWithFreshOperatorLogin } from "@/features/auth/operator-auth-navigation"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import type { ManagedOperator } from "@/features/operator/security/api/operator-directory.api"

import { OperatorDirectoryPage } from "./operator-directory-page"

vi.mock("@/features/auth/operator-auth-navigation", () => ({
  replaceWithFreshOperatorLogin: vi.fn(),
}))

const replaceWithFreshOperatorLoginMock = vi.mocked(replaceWithFreshOperatorLogin)

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

const currentOwner = {
  operatorId: "owner-1",
  username: "admin",
  email: "admin@example.test",
  isEnabled: true,
  isBootstrapProvisioning: false,
  hasPassword: true,
  hasTotp: true,
  roles: ["platform_owner"],
  createdAtUtc: "2026-07-04T12:00:00+00:00",
  lastLoginAtUtc: "2026-07-04T12:30:00+00:00",
  enrollmentGrantExpiresAtUtc: null,
  isCurrentOperator: true,
}

const readyAuditor = {
  operatorId: "auditor-1",
  username: "audit.reader",
  email: null,
  isEnabled: true,
  isBootstrapProvisioning: false,
  hasPassword: true,
  hasTotp: true,
  roles: ["auditor"],
  createdAtUtc: "2026-07-04T12:00:00+00:00",
  lastLoginAtUtc: null,
  enrollmentGrantExpiresAtUtc: null,
  isCurrentOperator: false,
}

function renderDirectory(session = ownerSession) {
  renderWithProviders(
    <MemoryRouter>
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <OperatorDirectoryPage />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

describe("OperatorDirectoryPage", () => {
  it("shows safe operator lifecycle, role inventory, and owner-only actions", async () => {
    server.use(
      http.get("/api/security/operators", () =>
        HttpResponse.json([
          currentOwner,
          {
            ...readyAuditor,
            isEnabled: false,
            hasTotp: false,
          },
        ]),
      ),
    )

    renderDirectory()

    expect(await screen.findByRole("heading", { name: "Operator access" })).toBeInTheDocument()
    expect(await screen.findByText("admin")).toBeInTheDocument()
    expect(screen.getByText("audit.reader")).toBeInTheDocument()
    expect(screen.getByText("Current session")).toBeInTheDocument()
    expect(screen.getByText("Platform Owner")).toBeInTheDocument()
    expect(screen.getByText("Auditor")).toBeInTheDocument()
    expect(screen.getByText("MFA configured")).toBeInTheDocument()
    expect(screen.getByText("Disabled")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create pending operator" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Edit roles" })).toBeInTheDocument()
    expect(screen.getByText("Enrolment required before enablement.")).toBeInTheDocument()
  })

  it("keeps the Actions column pinned while the operator directory scrolls horizontally", async () => {
    server.use(
      http.get("/api/security/operators", () =>
        HttpResponse.json([currentOwner, readyAuditor]),
      ),
    )

    renderDirectory()

    await screen.findByText("audit.reader")

    expect(screen.getByRole("table")).toHaveClass("min-w-[1040px]")

    const actionsHeader = screen.getByRole("columnheader", { name: "Actions" })
    expect(actionsHeader).toHaveClass("sticky", "right-0", "border-l", "bg-card")

    const currentOperatorActionCell = screen
      .getByRole("button", { name: "Change password" })
      .closest("td")
    expect(currentOperatorActionCell).toHaveClass("sticky", "right-0", "border-l", "bg-card")

    const managedOperatorActionCell = screen
      .getByRole("button", { name: "Edit roles" })
      .closest("td")
    expect(managedOperatorActionCell).toHaveClass("sticky", "right-0", "border-l", "bg-card")
  })

  it("exposes password change only for the current operator and reuses the fresh step-up flow", async () => {
    const user = userEvent.setup()

    server.use(
      http.get("/api/security/operators", () =>
        HttpResponse.json([currentOwner, readyAuditor]),
      ),
      http.post("/api/auth/step-up", async ({ request }) => {
        expect(await request.json()).toEqual({
          password: "Secure!Foundation123",
          code: "123456",
        })

        return HttpResponse.json({
          status: "step_up_authenticated",
          expiresAtUtc: "2026-09-15T08:30:00Z",
        })
      }),
      http.post("/api/auth/password", async ({ request }) => {
        expect(await request.json()).toEqual({
          newPassword: "Different!Foundation456",
          confirmPassword: "Different!Foundation456",
        })

        return HttpResponse.json({ status: "password_changed" })
      }),
    )

    renderDirectory()

    await screen.findByText("audit.reader")

    const changePasswordButtons = screen.getAllByRole("button", { name: "Change password" })
    expect(changePasswordButtons).toHaveLength(1)
    expect(
      screen.queryByText("Manage your own password and recovery codes from the account menu."),
    ).not.toBeInTheDocument()

    await user.click(changePasswordButtons[0]!)

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    const changePasswordDialog = await screen.findByRole("dialog", { name: "Change password" })
    expect(changePasswordDialog).toBeInTheDocument()
    await user.type(screen.getByLabelText("New password"), "Different!Foundation456")
    await user.type(screen.getByLabelText("Confirm new password"), "Different!Foundation456")
    await user.click(within(changePasswordDialog).getByRole("button", { name: "Change password" }))

    await waitFor(() => {
      expect(replaceWithFreshOperatorLoginMock).toHaveBeenCalledTimes(1)
    })
    expect(changePasswordDialog).toBeInTheDocument()
  })

  it("creates a disabled pending operator with an initial role", async () => {
    const user = userEvent.setup()
    let listed: ManagedOperator[] = [currentOwner]

    server.use(
      http.get("/api/security/operators", () => HttpResponse.json(listed)),
      http.post("/api/security/operators", async ({ request }) => {
        expect(await request.json()).toEqual({
          username: "ops.reader",
          email: "ops.reader@example.test",
          roles: ["operator"],
        })

        const created = {
          ...readyAuditor,
          operatorId: "operator-2",
          username: "ops.reader",
          email: "ops.reader@example.test",
          isEnabled: false,
          hasPassword: false,
          hasTotp: false,
          roles: ["operator"],
        }
        listed = [...listed, created]

        return HttpResponse.json(created, { status: 201 })
      }),
    )

    renderDirectory()

    await screen.findByText("admin")
    await user.click(screen.getByRole("button", { name: "Create pending operator" }))
    await user.type(screen.getByLabelText("Username"), "ops.reader")
    await user.type(screen.getByLabelText("Email (optional)"), "ops.reader@example.test")
    const createButtons = screen.getAllByRole("button", { name: "Create pending operator" })
    await user.click(createButtons[createButtons.length - 1]!)

    expect(await screen.findByText("Pending operator created")).toBeInTheDocument()
    expect(screen.getByText("ops.reader is disabled until secure enrolment is completed.")).toBeInTheDocument()
  })

  it("issues a one-time enrolment code for a disabled pending operator and clears it on acknowledgement", async () => {
    const user = userEvent.setup()
    const pending = {
      ...readyAuditor,
      isEnabled: false,
      hasPassword: false,
      hasTotp: false,
      enrollmentGrantExpiresAtUtc: null,
    }

    server.use(
      http.get("/api/security/operators", () =>
        HttpResponse.json([currentOwner, pending]),
      ),
      http.post(`/api/security/operators/${pending.operatorId}/enrollment-grants`, () =>
        HttpResponse.json({
          operatorId: pending.operatorId,
          username: pending.username,
          enrollmentCode: "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
          expiresAtUtc: "2026-07-04T13:00:00+00:00",
        }),
      ),
    )

    renderDirectory()

    await screen.findByText("audit.reader")
    await user.click(screen.getByRole("button", { name: "Issue enrolment code" }))
    expect(screen.getByText("Issue a one-time enrolment code?")).toBeInTheDocument()

    const issueButtons = screen.getAllByRole("button", { name: "Issue enrolment code" })
    await user.click(issueButtons[issueButtons.length - 1]!)

    expect(await screen.findByLabelText("One-time enrolment code")).toHaveTextContent(
      "mem_enrol_0123456789ABCDEF0123456789ABCDEF",
    )

    await user.click(screen.getByRole("button", { name: "I have transferred the code securely" }))

    expect(screen.queryByLabelText("One-time enrolment code")).not.toBeInTheDocument()
  })

  it("preselects current roles, refuses an empty role set in the UI, then verifies identity before resuming the confirmed role change", async () => {
    const user = userEvent.setup()
    let roleUpdateCalls = 0
    let listed: ManagedOperator[] = [
      currentOwner,
      {
        ...readyAuditor,
        roles: ["operator", "auditor"],
      },
    ]

    server.use(
      http.get("/api/security/operators", () => HttpResponse.json(listed)),
      http.put("/api/security/operators/auditor-1/roles", async ({ request }) => {
        roleUpdateCalls += 1
        expect(await request.json()).toEqual({ roles: ["operator"] })

        if (roleUpdateCalls === 1) {
          return HttpResponse.json(
            { status: "step_up_required" },
            { status: 403, headers: { "Cache-Control": "no-store" } },
          )
        }

        listed = [
          currentOwner,
          {
            ...readyAuditor,
            roles: ["operator"],
          },
        ]

        return HttpResponse.json(listed[1])
      }),
      http.post("/api/auth/step-up", async ({ request }) => {
        expect(await request.json()).toEqual({
          password: "Secure!Foundation123",
          code: "123456",
        })

        return HttpResponse.json({
          status: "step_up_authenticated",
          expiresAtUtc: "2026-07-05T12:15:00+00:00",
        })
      }),
    )

    renderDirectory()

    await screen.findByText("audit.reader")
    await user.click(screen.getByRole("button", { name: "Edit roles" }))

    const platformOwner = screen.getByRole("checkbox", { name: "Platform Owner" })
    const operator = screen.getByRole("checkbox", { name: "Operator" })
    const auditor = screen.getByRole("checkbox", { name: "Auditor" })

    expect(platformOwner).toHaveAttribute("data-state", "unchecked")
    expect(operator).toHaveAttribute("data-state", "checked")
    expect(auditor).toHaveAttribute("data-state", "checked")

    await user.click(operator)
    await user.click(auditor)
    expect(screen.getByRole("button", { name: "Review role changes" })).toBeDisabled()

    await user.click(operator)
    await user.click(screen.getByRole("button", { name: "Review role changes" }))
    await user.click(screen.getByRole("button", { name: "Apply role changes" }))

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await waitFor(() => {
      expect(roleUpdateCalls).toBe(2)
    })

    expect(screen.queryByRole("dialog", { name: "Verify your identity" })).not.toBeInTheDocument()
  })

  it("keeps a lifecycle confirmation open and explains final-owner protection", async () => {
    const user = userEvent.setup()

    server.use(
      http.get("/api/security/operators", () =>
        HttpResponse.json([
          currentOwner,
          {
            ...readyAuditor,
            operatorId: "owner-2",
            username: "owner.beta",
            roles: ["platform_owner"],
          },
        ]),
      ),
      http.put("/api/security/operators/owner-2/enabled", () =>
        HttpResponse.json(
          { status: "last_active_platform_owner" },
          { status: 409 },
        ),
      ),
    )

    renderDirectory()

    await screen.findByText("owner.beta")
    await user.click(screen.getByRole("button", { name: "Disable" }))
    expect(screen.getByText("Disable operator?")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Disable operator" }))

    expect(await screen.findByText("MEM must retain at least one active Platform Owner.")).toBeInTheDocument()
    expect(screen.getByText("Disable operator?")).toBeInTheDocument()
  })

  it("does not request or present the directory to a non-owner", () => {
    let requests = 0

    server.use(
      http.get("/api/security/operators", () => {
        requests += 1
        return HttpResponse.json([])
      }),
    )

    renderDirectory(operatorSession)

    expect(screen.getByText("Operator access is restricted")).toBeInTheDocument()
    expect(requests).toBe(0)
  })
})
