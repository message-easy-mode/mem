import { afterEach, describe, expect, it, vi } from "vitest"
import { screen } from "@testing-library/react"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import type { WorkflowStep } from "../api/install.types"
import { InstallOperationProgressCard } from "./install-operation-progress-card"

afterEach(() => {
  vi.useRealTimers()
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("InstallOperationProgressCard", () => {
  it("shows durable current operation, heartbeat context, and completed sub-phases", () => {
    renderWithProviders(<InstallOperationProgressCard step={certificateStep()} />)

    expect(screen.getByText("Current operation")).toBeInTheDocument()
    expect(
      screen.getAllByText("Allow deSEC DNS visibility to settle").length,
    ).toBeGreaterThanOrEqual(1)
    expect(screen.getByText("MEM is still working")).toBeInTheDocument()
    expect(
      screen.getByText(/No action is required\./),
    ).toBeInTheDocument()
    expect(screen.getByText("Prepare TLS certificate request")).toBeInTheDocument()
    expect(screen.getByText("Publish DNS-01 challenge")).toBeInTheDocument()
    expect(screen.getByText("Wait for deSEC authoritative DNS visibility")).toBeInTheDocument()
    expect(screen.getAllByText("Allow deSEC DNS visibility to settle")).toHaveLength(2)
    expect(screen.getByText("Attempt")).toBeInTheDocument()
  })

  it("keeps elapsed time cumulative from the durable Setup start across operation changes", () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date("2026-09-26T00:03:05Z"))

    const step = certificateStep()
    step.progress = {
      ...step.progress!,
      stepStartedAtUtc: "2026-09-26T00:02:45Z",
      lastActivityAtUtc: "2026-09-26T00:03:02Z",
    }

    renderWithProviders(
      <InstallOperationProgressCard
        step={step}
        setupStartedAtUtc="2026-09-26T00:00:00Z"
      />,
    )

    expect(screen.getByText("3m 5s")).toBeInTheDocument()
    expect(screen.queryByText("20s")).not.toBeInTheDocument()
  })

  it("surfaces a stale heartbeat without inventing progress", () => {
    const step = certificateStep()
    step.progress = {
      ...step.progress!,
      lastActivityAtUtc: new Date(Date.now() - 60_000).toISOString(),
    }

    renderWithProviders(<InstallOperationProgressCard step={step} />)

    expect(screen.getByText("MEM is still waiting")).toBeInTheDocument()
    expect(
      screen.getByText(/No new server activity has been recorded recently/),
    ).toBeInTheDocument()
  })

  it("does not leak an unknown server phase summary into the localized primary UI", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const step = certificateStep()
    step.progress = {
      ...step.progress!,
      phaseCode: "future.unknown-phase",
      safeSummary: "Server-owned English technical summary",
      phases: [
        {
          code: "future.unknown-phase",
          status: "Running",
          safeSummary: "Server-owned English technical summary",
          startedAtUtc: new Date(Date.now() - 5_000).toISOString(),
          completedAtUtc: null,
        },
      ],
    }

    renderWithProviders(<InstallOperationProgressCard step={step} />)

    expect(screen.getAllByText("Aktueller Vorgang wird verarbeitet").length).toBeGreaterThanOrEqual(1)
    expect(screen.queryByText("Server-owned English technical summary")).not.toBeInTheDocument()
  })

  it("renders bounded ACME secondary-DNS recovery and retry truthfully", () => {
    const step = certificateStep()
    step.progress = {
      ...step.progress!,
      phaseCode: "certificate.acme-validation-retry",
      phases: [
        ...step.progress!.phases,
        {
          code: "certificate.acme-validation",
          status: "Recovering",
          safeSummary: "Server-owned initial validation miss detail.",
          startedAtUtc: new Date(Date.now() - 15_000).toISOString(),
          completedAtUtc: null,
        },
        {
          code: "certificate.acme-dns-recovery",
          status: "Succeeded",
          safeSummary: "Server-owned recovery detail.",
          startedAtUtc: new Date(Date.now() - 10_000).toISOString(),
          completedAtUtc: new Date(Date.now() - 6_000).toISOString(),
        },
        {
          code: "certificate.acme-validation-retry",
          status: "Running",
          safeSummary: "Server-owned retry detail.",
          startedAtUtc: new Date(Date.now() - 5_000).toISOString(),
          completedAtUtc: null,
        },
      ],
    }

    renderWithProviders(<InstallOperationProgressCard step={step} />)

    expect(
      screen.getByText("Recover from a transient Let's Encrypt secondary DNS miss"),
    ).toBeInTheDocument()
    expect(
      screen.getAllByText("Retry Let's Encrypt DNS validation after bounded recovery").length,
    ).toBeGreaterThanOrEqual(1)
    expect(screen.queryByText("Server-owned recovery detail.")).not.toBeInTheDocument()
    expect(screen.queryByText("Server-owned retry detail.")).not.toBeInTheDocument()
  })

  it("localizes the processing surface in German", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(<InstallOperationProgressCard step={certificateStep()} />)

    expect(screen.getByText("Aktueller Vorgang")).toBeInTheDocument()
    expect(
      screen.getAllByText("Sichtbarkeit bei deSEC-DNS für die Validierung stabilisieren").length,
    ).toBeGreaterThanOrEqual(1)
    expect(screen.getByText("MEM arbeitet weiter")).toBeInTheDocument()
  })
})

function certificateStep(): WorkflowStep {
  return {
    order: 8,
    name: "Issue and import platform certificate",
    title: "Issue and import platform certificate",
    kind: "server-step",
    category: "Installation",
    tags: [],
    requiresHumanAction: false,
    isCheckpoint: false,
    status: "Running",
    message: "Running...",
    errorMessage: null,
    attemptCount: 2,
    startedAtUtc: "2026-08-15T00:00:00Z",
    completedAtUtc: null,
    progress: {
      schemaVersion: 1,
      installationId: "80a6361e-b1fb-4be7-a2e7-daed0d8a62d8",
      stepId: "5a4da80f-0f5a-42bf-a6ad-b2c5f41af0b9",
      stepSequence: 8,
      stepName: "Issue and import platform certificate",
      attemptNumber: 2,
      stepStatus: "Running",
      phaseCode: "certificate.dns-stability",
      phaseStatus: "Running",
      safeSummary: "Certificate issuance is still in progress.",
      stepStartedAtUtc: new Date(Date.now() - 90_000).toISOString(),
      lastActivityAtUtc: new Date(Date.now() - 3_000).toISOString(),
      phases: [
        {
          code: "certificate.prepare",
          status: "Succeeded",
          safeSummary: "Preparing the reviewed TLS certificate request.",
          startedAtUtc: new Date(Date.now() - 90_000).toISOString(),
          completedAtUtc: new Date(Date.now() - 85_000).toISOString(),
        },
        {
          code: "certificate.dns-publish",
          status: "Succeeded",
          safeSummary: "Publishing the DNS-01 challenge through deSEC.",
          startedAtUtc: new Date(Date.now() - 85_000).toISOString(),
          completedAtUtc: new Date(Date.now() - 75_000).toISOString(),
        },
        {
          code: "certificate.dns-authoritative",
          status: "Succeeded",
          safeSummary: "Waiting for the DNS challenge on all deSEC authoritative DNS servers.",
          startedAtUtc: new Date(Date.now() - 75_000).toISOString(),
          completedAtUtc: new Date(Date.now() - 20_000).toISOString(),
        },
        {
          code: "certificate.dns-stability",
          status: "Running",
          safeSummary: "The DNS challenge remains visible on deSEC authoritative DNS; MEM is continuing the bounded settling window before Let's Encrypt validation.",
          startedAtUtc: new Date(Date.now() - 20_000).toISOString(),
          completedAtUtc: null,
        },
      ],
    },
  }
}
