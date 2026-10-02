import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import {
  approveCliDeviceAuthorization,
  denyCliDeviceAuthorization,
  reviewCliDeviceAuthorization,
} from "../cli-device-authorization"
import { CliDeviceAuthorizationPage } from "./cli-device-authorization-page"

vi.mock("../cli-device-authorization", () => ({
  reviewCliDeviceAuthorization: vi.fn(),
  approveCliDeviceAuthorization: vi.fn(),
  denyCliDeviceAuthorization: vi.fn(),
}))

vi.mock("../operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({
    open,
    onOpenChange,
    onVerified,
  }: {
    open: boolean
    onOpenChange: (open: boolean) => void
    onVerified?: () => void
  }) => open ? (
    <button
      type="button"
      onClick={() => {
        onOpenChange(false)
        onVerified?.()
      }}
    >
      Complete verification
    </button>
  ) : null,
}))

const reviewMock = vi.mocked(reviewCliDeviceAuthorization)
const approveMock = vi.mocked(approveCliDeviceAuthorization)
const denyMock = vi.mocked(denyCliDeviceAuthorization)

function renderPage() {
  renderWithProviders(
    <MemoryRouter>
      <CliDeviceAuthorizationPage />
    </MemoryRouter>,
  )
}

async function reviewKnownDevice(user: ReturnType<typeof userEvent.setup>) {
  reviewMock.mockResolvedValue({
    status: "authorization_pending",
    deviceLabel: "SSH host shell",
    expiresAtUtc: "2026-07-06T23:10:00Z",
  })

  await user.type(screen.getByLabelText("MEM CLI code"), "abcd-efgh")
  await user.click(screen.getByRole("button", { name: "Review device" }))

  expect(await screen.findByText("SSH host shell")).toBeInTheDocument()
}

afterEach(() => {
  vi.clearAllMocks()
})

describe("CliDeviceAuthorizationPage", () => {
  it("reviews only the safe pending device label and expiry from a code held in component state", async () => {
    const user = userEvent.setup()
    renderPage()

    await reviewKnownDevice(user)

    expect(reviewMock).toHaveBeenCalledWith("ABCD-EFGH")
    expect(screen.getByText("No credential is shown in this browser")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Approve device" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Deny device" })).toBeInTheDocument()
  })

  it("closes the exact approval confirmation, verifies identity, and retries only the reviewed code", async () => {
    const user = userEvent.setup()
    approveMock
      .mockResolvedValueOnce({ status: "step_up_required" })
      .mockResolvedValueOnce({ status: "authorization_approved" })
    renderPage()

    await reviewKnownDevice(user)
    await user.click(screen.getByRole("button", { name: "Approve device" }))

    const confirmation = await screen.findByRole("alertdialog")
    await user.click(within(confirmation).getByRole("button", { name: "Approve device" }))

    expect(approveMock).toHaveBeenCalledWith("ABCD-EFGH")

    await user.click(await screen.findByRole("button", { name: "Complete verification" }))

    await waitFor(() => {
      expect(approveMock).toHaveBeenCalledTimes(2)
    })

    expect(approveMock).toHaveBeenLastCalledWith("ABCD-EFGH")
    expect(await screen.findByText("MEM CLI is authorized")).toBeInTheDocument()
    expect(screen.queryByText("SSH host shell")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("MEM CLI code")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Authorize another device" })).toBeInTheDocument()
  })

  it("denies a reviewed device without requesting step-up and never renders a credential", async () => {
    const user = userEvent.setup()
    denyMock.mockResolvedValue({ status: "authorization_denied" })
    renderPage()

    await reviewKnownDevice(user)
    await user.click(screen.getByRole("button", { name: "Deny device" }))

    await waitFor(() => {
      expect(denyMock).toHaveBeenCalledWith("ABCD-EFGH")
    })

    expect(await screen.findByText("MEM CLI authorization denied")).toBeInTheDocument()
    expect(screen.queryByText("Complete verification")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("MEM CLI code")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Authorize another device" })).toBeInTheDocument()
    expect(screen.queryByText(/credential/i)).not.toHaveTextContent("deviceCredential")
  })

  it("shows an opaque unavailable message instead of a raw response detail", async () => {
    const user = userEvent.setup()
    reviewMock.mockResolvedValue({ status: "authorization_unavailable" })
    renderPage()

    await user.type(screen.getByLabelText("MEM CLI code"), "ABCD-EFGH")
    await user.click(screen.getByRole("button", { name: "Review device" }))

    expect(
      await screen.findByText("MEM could not find a pending CLI authorization for this code."),
    ).toBeInTheDocument()
    expect(screen.queryByText("must-not-reach-ui")).not.toBeInTheDocument()
  })
})
