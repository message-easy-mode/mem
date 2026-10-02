import { http, HttpResponse } from "msw"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { RuntimeStackUsersResponse } from "../api/stacks.types"
import { StackUsersPanel } from "./stack-users-panel"

const usersEndpoint = "/internal/host-agent/runtime-stacks/restored-stack/users"
const synchronizeEndpoint = `${usersEndpoint}/synchronize`
const firstAdminEndpoint = `${usersEndpoint}/first-admin`

const recoveredAdmin = {
  id: "matrix-user-admin",
  runtimeStackId: "7085b97d-3d30-434e-976a-62df0178be16",
  matrixInstanceId: "matrix-instance-1",
  username: "restored-admin",
  matrixUserId: "@restored-admin:matrix.example.test",
  isAdmin: true,
  isFirstAdmin: false,
  status: "active",
  origin: "synapse-discovered",
  displayName: null,
  email: null,
  lastError: null,
  createdAtUtc: "2026-07-01T10:00:00Z",
  updatedAtUtc: "2026-07-10T00:00:00Z",
  matrixSyncedAtUtc: "2026-07-10T00:00:00Z",
}

function inventory(overrides: Partial<RuntimeStackUsersResponse> = {}): RuntimeStackUsersResponse {
  return {
    source: "control-plane",
    status: "ok",
    stackId: "7085b97d-3d30-434e-976a-62df0178be16",
    slug: "restored-stack",
    inventorySource: "synapse-postgres",
    inventoryStatus: "synchronized",
    inventoryLastAttemptedAtUtc: "2026-07-10T00:00:00Z",
    inventoryLastSynchronizedAtUtc: "2026-07-10T00:00:00Z",
    inventoryUserCount: 1,
    activeAdminCount: 1,
    inventoryErrorCode: null,
    synchronizationRequired: false,
    users: [recoveredAdmin],
    requiresFirstAdmin: false,
    canCreateUsers: true,
    adminAuthority: {
      status: "available",
      canResetPasswords: true,
      source: "created-admin-token",
      adminUserId: "@restored-admin:matrix.example.test",
      storedAtUtc: "2026-07-10T00:00:00Z",
      lastValidatedAtUtc: "2026-07-10T00:00:00Z",
      errorCode: null,
    },
    detail: null,
    ...overrides,
  }
}

afterEach(() => window.localStorage.clear())

describe("StackUsersPanel USER-REC-03", () => {
  it("keeps a dense scrollable user table and reserves a sticky action column at constrained widths", async () => {
    const recoveredMember = {
      ...recoveredAdmin,
      id: "matrix-user-member-responsive",
      username: "restored-member",
      matrixUserId: "@restored-member:matrix.example.test",
      isAdmin: false,
    }

    server.use(http.get(usersEndpoint, () => HttpResponse.json(inventory({
      inventoryUserCount: 2,
      users: [recoveredAdmin, recoveredMember],
    }))))

    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await screen.findByText(recoveredMember.matrixUserId)

    const table = screen.getByRole("table")
    expect(table).toHaveClass("min-w-[1120px]", "table-fixed")

    const actionsHeader = screen.getByRole("columnheader", { name: "Actions" })
    expect(actionsHeader).toHaveClass("sticky", "right-0", "min-w-[290px]", "text-right")

    const row = screen.getByTestId(`matrix-user-row-${recoveredMember.id}`)
    expect(row).toHaveAttribute("data-matrix-user-id", recoveredMember.matrixUserId)

    const actions = screen.getByTestId(`matrix-user-actions-${recoveredMember.id}`)
    expect(actions).toHaveClass("sticky", "right-0", "min-w-[290px]", "text-right")
    expect(actions).toHaveTextContent("Reset password")
    expect(actions).toHaveTextContent("Deactivate account")
  })

  it("automatically synchronizes once, hides creation while unknown, and shows a recovered administrator honestly", async () => {
    let current = inventory({
      inventorySource: null,
      inventoryStatus: "not_synchronized",
      inventoryLastAttemptedAtUtc: null,
      inventoryLastSynchronizedAtUtc: null,
      inventoryUserCount: null,
      activeAdminCount: 0,
      synchronizationRequired: true,
      users: [],
      requiresFirstAdmin: false,
      canCreateUsers: false,
    })
    let synchronizeAttempts = 0
    let releaseSynchronization!: () => void
    const gate = new Promise<void>((resolve) => { releaseSynchronization = resolve })

    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(current)),
      http.post(synchronizeEndpoint, async () => {
        synchronizeAttempts += 1
        await gate
        current = inventory()
        return HttpResponse.json(current)
      }),
    )

    const view = renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    expect(await screen.findByText("Reading the Matrix account inventory from the stack's Synapse database…")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Create Matrix user" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Create first Matrix admin" })).not.toBeInTheDocument()

    releaseSynchronization()
    expect(await screen.findByText("@restored-admin:matrix.example.test")).toBeInTheDocument()
    expect(screen.getByText("Recovered from Synapse")).toBeInTheDocument()
    expect(screen.getByText("Active")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create Matrix user" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Create first Matrix admin" })).not.toBeInTheDocument()

    view.rerender(<StackUsersPanel slugOrId="restored-stack" />)
    await waitFor(() => expect(synchronizeAttempts).toBe(1))
  })

  it("shows retry without a creation form after failure and refreshes to an authoritative empty inventory", async () => {
    let current = inventory({
      inventoryStatus: "failed",
      inventoryLastSynchronizedAtUtc: null,
      inventoryUserCount: null,
      activeAdminCount: 0,
      inventoryErrorCode: "matrix_user_inventory_unavailable",
      synchronizationRequired: true,
      users: [],
      requiresFirstAdmin: false,
      canCreateUsers: false,
      detail: "Matrix user inventory synchronization failed. Retry synchronization before creating users.",
    })
    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(current)),
      http.post(synchronizeEndpoint, () => {
        current = inventory({ inventoryUserCount: 0, activeAdminCount: 0, users: [], requiresFirstAdmin: true })
        return HttpResponse.json(current)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    expect(await screen.findByText("Matrix user synchronization failed")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /Create .*Matrix/ })).not.toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Retry synchronization" }))
    expect(await screen.findByRole("button", { name: "Create first Matrix admin" })).toBeInTheDocument()
    expect(screen.getByText(/Synapse reported no active local Matrix administrator/)).toBeInTheDocument()
  })

  it("shows the first-admin form only for a synchronized inventory with no active administrator", async () => {
    const recoveredMember = {
      ...recoveredAdmin,
      id: "matrix-user-member",
      username: "restored-member",
      matrixUserId: "@restored-member:matrix.example.test",
      isAdmin: false,
    }
    server.use(http.get(usersEndpoint, () => HttpResponse.json(inventory({
      inventoryUserCount: 1,
      activeAdminCount: 0,
      users: [recoveredMember],
      requiresFirstAdmin: true,
    }))))
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    expect(await screen.findByRole("button", { name: "Create first Matrix admin" })).toBeInTheDocument()
    expect(screen.getByText("@restored-member:matrix.example.test")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Create Matrix user" })).not.toBeInTheDocument()
  })

  it("represents deactivated and missing projected accounts without treating them as active", async () => {
    server.use(http.get(usersEndpoint, () => HttpResponse.json(inventory({
      inventoryUserCount: 2,
      users: [
        recoveredAdmin,
        { ...recoveredAdmin, id: "deactivated-user", username: "former-member", matrixUserId: "@former-member:matrix.example.test", isAdmin: false, status: "deactivated" },
        { ...recoveredAdmin, id: "missing-user", username: "projection-only", matrixUserId: "@projection-only:matrix.example.test", isAdmin: false, status: "missing", origin: "mem-created" },
        { ...recoveredAdmin, id: "unconfirmed-user", username: "unconfirmed", matrixUserId: "@unconfirmed:matrix.example.test", isAdmin: false, origin: "mem-creation-unconfirmed" },
      ],
    }))))
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    expect(await screen.findByText("Deactivated")).toBeInTheDocument()
    expect(screen.getByText("Missing from Synapse")).toBeInTheDocument()
    expect(screen.getByText("Created through MEM")).toBeInTheDocument()
    expect(screen.getByText("Creation source unconfirmed")).toBeInTheDocument()
  })

  it("manually synchronizes and refetches the users query", async () => {
    let current = inventory()
    let listRequests = 0
    server.use(
      http.get(usersEndpoint, () => { listRequests += 1; return HttpResponse.json(current) }),
      http.post(synchronizeEndpoint, () => {
        current = inventory({ inventoryUserCount: 2, users: [
          recoveredAdmin,
          { ...recoveredAdmin, id: "matrix-user-member", username: "newly-discovered", matrixUserId: "@newly-discovered:matrix.example.test", isAdmin: false },
        ] })
        return HttpResponse.json(current)
      }),
    )
    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    await screen.findByText("@restored-admin:matrix.example.test")
    await user.click(screen.getByRole("button", { name: "Synchronize users" }))
    expect(await screen.findByText("@newly-discovered:matrix.example.test")).toBeInTheDocument()
    await waitFor(() => expect(listRequests).toBeGreaterThan(1))
  })

  it("re-arms automatic synchronization after a later stale-inventory episode", async () => {
    let current = inventory({
      inventorySource: null,
      inventoryStatus: "not_synchronized",
      inventoryLastAttemptedAtUtc: null,
      inventoryLastSynchronizedAtUtc: null,
      inventoryUserCount: null,
      activeAdminCount: 0,
      synchronizationRequired: true,
      users: [],
      requiresFirstAdmin: false,
      canCreateUsers: false,
    })
    let synchronizeAttempts = 0

    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(current)),
      http.post(synchronizeEndpoint, () => {
        synchronizeAttempts += 1

        if (synchronizeAttempts === 1) {
          current = inventory()
          return HttpResponse.json(current)
        }

        if (synchronizeAttempts === 2) {
          current = inventory({
            inventoryStatus: "not_synchronized",
            inventoryErrorCode: "matrix_user_projection_stale",
            synchronizationRequired: true,
            canCreateUsers: false,
          })
          return HttpResponse.json(current)
        }

        current = inventory({
          inventoryUserCount: 2,
          users: [
            recoveredAdmin,
            {
              ...recoveredAdmin,
              id: "matrix-user-member",
              username: "recovered-member",
              matrixUserId: "@recovered-member:matrix.example.test",
              isAdmin: false,
            },
          ],
        })
        return HttpResponse.json(current)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await screen.findByText("@restored-admin:matrix.example.test")
    await user.click(screen.getByRole("button", { name: "Synchronize users" }))

    expect(await screen.findByText("@recovered-member:matrix.example.test")).toBeInTheDocument()
    await waitFor(() => expect(synchronizeAttempts).toBe(3))
  })

  it("resets the normal create-user form after a successful creation", async () => {
    let current = inventory()

    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(current)),
      http.post(usersEndpoint, async ({ request }) => {
        const body = await request.json() as {
          username: string
          password: string
          isAdmin: boolean
          displayName: string | null
          email: string | null
        }
        const created = {
          ...recoveredAdmin,
          id: "matrix-user-created-member",
          username: body.username,
          matrixUserId: `@${body.username}:matrix.example.test`,
          isAdmin: body.isAdmin,
          displayName: body.displayName,
          email: body.email,
          origin: "mem-created",
        }
        current = inventory({
          inventoryUserCount: 2,
          activeAdminCount: body.isAdmin ? 2 : 1,
          users: [recoveredAdmin, created],
        })
        return HttpResponse.json(created)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    const username = await screen.findByLabelText("Username")
    const password = screen.getByLabelText("Password")
    const displayName = screen.getByLabelText("Display name")
    const email = screen.getByLabelText("Email")
    const admin = screen.getByRole("checkbox", { name: "Matrix admin" })

    await user.type(username, "new.member")
    await user.type(password, "valid-password-123")
    await user.type(displayName, "New Member")
    await user.type(email, "new.member@example.test")
    await user.click(admin)
    await user.click(screen.getByRole("button", { name: "Create Matrix user" }))

    expect(await screen.findByText("@new.member:matrix.example.test")).toBeInTheDocument()
    expect(screen.getByLabelText("Username")).toHaveValue("")
    expect(screen.getByLabelText("Password")).toHaveValue("")
    expect(screen.getByLabelText("Display name")).toHaveValue("")
    expect(screen.getByLabelText("Email")).toHaveValue("")
    expect(screen.getByRole("checkbox", { name: "Matrix admin" })).not.toBeChecked()
    expect(screen.getByRole("button", { name: "Create Matrix user" })).toBeDisabled()
  })

  it("shows safe problem detail instead of internal request diagnostics when user creation fails", async () => {
    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(inventory({
        inventoryUserCount: 0,
        activeAdminCount: 0,
        users: [],
        requiresFirstAdmin: true,
      }))),
      http.post(firstAdminEndpoint, () => HttpResponse.json({
        type: "https://mem.invalid/problems/dependency_request_failed",
        title: "A dependent service did not respond",
        status: 502,
        detail: "MEM could not complete a request to a required dependent service.",
        code: "dependency_request_failed",
        error: "dependency_request_failed",
        traceId: "technical-trace-id",
      }, { status: 502 })),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await user.type(await screen.findByLabelText("Username"), "admin")
    await user.type(screen.getByLabelText("Password"), "valid-password-123")
    await user.click(screen.getByRole("button", { name: "Create first Matrix admin" }))

    expect(await screen.findByText(
      "MEM could not complete a request to a required dependent service.",
    )).toBeInTheDocument()
    expect(screen.queryByText(/POST \/internal\/host-agent/)).not.toBeInTheDocument()
    expect(screen.queryByText(/technical-trace-id/)).not.toBeInTheDocument()
  })

  it("switches from first-admin creation to normal user creation after the inventory refetch", async () => {
    let current = inventory({
      inventoryUserCount: 0,
      activeAdminCount: 0,
      users: [],
      requiresFirstAdmin: true,
    })

    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(current)),
      http.post(firstAdminEndpoint, async ({ request }) => {
        const body = await request.json() as { username: string }
        const created = {
          ...recoveredAdmin,
          id: "matrix-user-created-admin",
          username: body.username,
          matrixUserId: `@${body.username}:matrix.example.test`,
          isFirstAdmin: true,
          origin: "mem-created",
        }
        current = inventory({
          inventoryUserCount: 1,
          activeAdminCount: 1,
          users: [created],
          requiresFirstAdmin: false,
          canCreateUsers: true,
        })
        return HttpResponse.json(created)
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await user.type(await screen.findByLabelText("Username"), "admin")
    await user.type(screen.getByLabelText("Password"), "valid-password-123")
    await user.click(screen.getByRole("button", { name: "Create first Matrix admin" }))

    expect(await screen.findByRole("button", { name: "Create Matrix user" })).toBeInTheDocument()
    expect(screen.getByText("Created through MEM")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Create first Matrix admin" })).not.toBeInTheDocument()
  })

})
