import { screen } from "@testing-library/react"
import { createMemoryRouter, MemoryRouter, RouterProvider } from "react-router-dom"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { GlobalRouteErrorPage, RouteNotFoundPage } from "./route-error-page"

afterEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("global route error UX", () => {
  it("renders a localized MEM 404 instead of React Router's developer error page", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter initialEntries={["/does-not-exist"]}>
        <RouteNotFoundPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole("heading", { name: "Seite nicht gefunden" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Dashboard öffnen" })).toHaveAttribute("href", "/dashboard")
    expect(screen.getByRole("link", { name: "Dokumentation öffnen" })).toHaveAttribute("href", "/docs")
    expect(screen.queryByText(/Unexpected Application Error/i)).not.toBeInTheDocument()
  })

  it("contains unexpected route errors without rendering raw exception details", async () => {
    const router = createMemoryRouter(
      [
        {
          path: "/",
          element: <div>Root</div>,
          errorElement: <GlobalRouteErrorPage />,
          children: [
            {
              path: "boom",
              loader: () => {
                throw new Error("do-not-render-this-internal-detail")
              },
              element: <div>Boom</div>,
            },
          ],
        },
      ],
      { initialEntries: ["/boom"] },
    )

    renderWithProviders(<RouterProvider router={router} />)

    expect(await screen.findByRole("heading", { name: "MEM could not display this page" })).toBeInTheDocument()
    expect(screen.queryByText("do-not-render-this-internal-detail")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Reload page" })).toBeInTheDocument()
  })
})
