import { MemoryRouter, useLocation, useRoutes } from "react-router-dom"
import { screen } from "@testing-library/react"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { documentationRoutes, setupDocumentationRoutes } from "./routes"

function DocumentationRoutesProbe() {
  const routes = useRoutes(documentationRoutes)
  const location = useLocation()

  return (
    <>
      <span data-testid="active-route">{location.pathname}</span>
      {routes}
    </>
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("documentation routes", () => {
  it("exposes the documentation home, legacy home alias, and document routes", () => {
    expect(documentationRoutes.map((route) => route.path)).toEqual([
      "docs",
      "docs/index",
      "docs/*",
    ])
  })

  it("exposes Setup-scoped documentation routes without duplicating the content pack", () => {
    expect(setupDocumentationRoutes.map((route) => route.path)).toEqual([
      "docs",
      "docs/*",
    ])
  })

  it("renders the active-language task-oriented documentation home", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs"]}>
        <DocumentationRoutesProbe />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Wobei benötigen Sie Hilfe?", level: 1 })).toBeInTheDocument()
    expect(screen.getByTestId("active-route")).toHaveTextContent("/docs")
  })

  it("redirects the imported legacy home document to the task-oriented home", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "en")

    renderWithProviders(
      <MemoryRouter initialEntries={["/docs/index"]}>
        <DocumentationRoutesProbe />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "What do you need help with?", level: 1 })).toBeInTheDocument()
    expect(screen.getByTestId("active-route")).toHaveTextContent("/docs")
  })
})
