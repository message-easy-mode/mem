import { screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import type { HostCheckGroup as HostCheckGroupModel } from "../api/host-checks.types"
import { HostCheckGroup } from "./host-check-group"

const group: HostCheckGroupModel = {
  key: "host",
  title: "Host",
  description: "Host checks",
  checks: [
    {
      key: "cpu",
      title: "CPU",
      status: "Pass",
      blocking: false,
      summary: "Docker reports 4 host CPU cores.",
      whyItMatters: "Capacity matters.",
      recommendedAction: null,
      evidence: [{ kind: "Docker API", label: "NCPU", value: "4" }],
    },
    {
      key: "memory",
      title: "Memory",
      status: "Warning",
      blocking: false,
      summary: "Memory is below the recommended production baseline.",
      whyItMatters: "Capacity matters.",
      recommendedAction: "Add more memory before production use.",
      evidence: [{ kind: "Docker API", label: "Memory", value: "2 GiB" }],
    },
  ],
}

describe("STARTUP-INSTALL-REL-01I compact Server Checks", () => {
  it("keeps attention findings visible while collapsing passed checks and raw evidence", () => {
    renderWithProviders(<HostCheckGroup group={group} />)

    expect(screen.getByRole("heading", { name: "Memory", level: 3 })).toBeInTheDocument()
    expect(screen.getByText("Add more memory before production use.")).toBeInTheDocument()

    const passedSummary = screen.getByText("1 passed check")
    expect(passedSummary).toBeInTheDocument()
    expect(passedSummary.closest("details")).not.toHaveAttribute("open")

    const rawEvidence = screen.getAllByText("Raw evidence")
    for (const item of rawEvidence) {
      expect(item.closest("details")).not.toHaveAttribute("open")
    }
  })
})
