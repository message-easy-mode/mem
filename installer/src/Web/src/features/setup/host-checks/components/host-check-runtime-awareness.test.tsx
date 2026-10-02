import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import type { HostCheckRunResponse } from "../api/host-checks.types"
import { HostCheckNextStepCard } from "./host-check-next-step-card"
import { HostCheckRow } from "./host-check-row"
import { HostCheckSummaryCard } from "./host-check-summary-card"

const run: HostCheckRunResponse = {
  id: "run-01c",
  status: "Succeeded",
  startedAtUtc: "2026-08-11T04:00:00Z",
  completedAtUtc: "2026-08-11T04:00:01Z",
  summary: {
    passed: 9,
    warnings: 0,
    failed: 0,
    skipped: 2,
    unavailable: 2,
    unknown: 0,
  },
  groups: [
    {
      key: "ports",
      title: "Ports",
      description: "Port authority",
      checks: [
        {
          key: "ports",
          title: "Host port ownership",
          status: "Unavailable",
          blocking: false,
          summary:
            "No Docker-published conflicts were found. Non-Docker listeners are unavailable in this runtime.",
          whyItMatters: "NPM needs its published ports.",
          recommendedAction: "No action is required now.",
          evidence: [
            {
              kind: "Runtime",
              label: "Authority",
              value: "containerized-development",
            },
          ],
        },
      ],
    },
  ],
}

describe("Setup host-check runtime awareness", () => {
  it("projects unavailable evidence without turning it into a warning or blocker", () => {
    renderWithProviders(
      <MemoryRouter>
        <HostCheckSummaryCard run={run} />
        <HostCheckRow check={run.groups[0].checks[0]} />
        <HostCheckNextStepCard run={run} onRerun={vi.fn()} />
      </MemoryRouter>,
    )

    expect(screen.getAllByText("Unavailable").length).toBeGreaterThan(0)
    expect(screen.getByLabelText("2 unavailable")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: /Continue to domain/i })).toBeInTheDocument()
    expect(screen.queryByText("Blocks install")).not.toBeInTheDocument()
  })
})
