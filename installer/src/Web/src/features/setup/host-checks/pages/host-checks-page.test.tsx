import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import {
  useCurrentHostCheckRun,
  useStartHostCheckRun,
} from "../hooks/host-checks.queries"
import { HostChecksPage } from "./host-checks-page"

vi.mock("../hooks/host-checks.queries", () => ({
  useCurrentHostCheckRun: vi.fn(),
  useStartHostCheckRun: vi.fn(),
}))

const mockUseCurrentHostCheckRun = vi.mocked(useCurrentHostCheckRun)
const mockUseStartHostCheckRun = vi.mocked(useStartHostCheckRun)

describe("STARTUP-INSTALL-REL-01I Server Checks preview actions", () => {
  beforeEach(() => {
    mockUseCurrentHostCheckRun.mockReturnValue({
      data: null,
      isLoading: false,
      error: null,
    } as unknown as ReturnType<typeof useCurrentHostCheckRun>)

    mockUseStartHostCheckRun.mockReturnValue({
      isPending: false,
      mutateAsync: vi.fn(),
    } as unknown as ReturnType<typeof useStartHostCheckRun>)
  })

  it("keeps completed Setup preview read-only instead of offering a mutation that will be locked", () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/check-server?setupPreview=1"]}>
        <HostChecksPage />
      </MemoryRouter>,
    )

    expect(
      screen.getByText("No detailed check run is available in this preview"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/First-time Setup mutations remain locked/i),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Run server checks" }),
    ).not.toBeInTheDocument()
  })

  it("offers exactly one Run server checks action during an active first-time Setup", () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/check-server"]}>
        <HostChecksPage />
      </MemoryRouter>,
    )

    expect(screen.getAllByRole("button", { name: "Run server checks" })).toHaveLength(1)
  })
})
