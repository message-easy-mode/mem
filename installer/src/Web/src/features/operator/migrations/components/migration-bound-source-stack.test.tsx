import { screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { MigrationBoundSourceStack } from "./migration-bound-source-stack"

describe("MigrationBoundSourceStack", () => {
  it("keeps long source identity values readable on constrained layouts", () => {
    renderWithProviders(
      <MigrationBoundSourceStack
        stack={{
          slug: "tester",
          matrixServerName: "matrix-e01affa9.matrixeasyhost.com",
          sourceStackId: "e01affa9-c147-434c-b6f4-84ee9ce1e7d0",
        }}
      />,
    )

    const card = screen.getByLabelText("Source stack bound to this package")
    const facts = card.querySelector("dl")
    expect(facts).not.toBeNull()
    expect(facts).toHaveClass(
      "xl:grid-cols-[minmax(0,0.7fr)_minmax(0,1.3fr)_minmax(0,1.4fr)]",
    )

    expect(screen.getByText("matrix-e01affa9.matrixeasyhost.com")).toHaveClass(
      "[overflow-wrap:anywhere]",
    )
    expect(screen.getByText("e01affa9-c147-434c-b6f4-84ee9ce1e7d0")).toHaveClass(
      "[overflow-wrap:anywhere]",
    )
  })
})
