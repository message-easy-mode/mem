import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"
import { MemApiProblemError } from "@/lib/api-problem"
import { ApiProblemAlert } from "./api-problem-alert"

describe("ApiProblemAlert", () => {
  it("shows safe correlation references and links a recorded incident to Diagnostics", () => {
    const error = new MemApiProblemError({
      method: "POST",
      path: "/api/test",
      status: 500,
      problem: {
        title: "The operation failed",
        detail: "MEM could not complete the operation.",
        code: "operation_failed",
        incidentId: "inc_123",
        traceId: "trace_123",
        suggestedAction: "Review the recorded incident before retrying.",
      },
    })

    renderWithProviders(
      <MemoryRouter>
        <ApiProblemAlert
          error={error}
          fallbackDescription="Fallback"
        />
      </MemoryRouter>,
    )

    expect(screen.getByText("MEM could not complete the operation.")).toBeInTheDocument()
    expect(screen.getByText(/inc_123/)).toBeInTheDocument()
    expect(screen.getByText(/operation_failed/)).toBeInTheDocument()
    expect(screen.getByText(/trace_123/)).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_123",
    )
  })
})
