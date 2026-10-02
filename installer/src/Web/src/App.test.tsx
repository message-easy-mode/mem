import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import App from "./App"

vi.mock("@/components/layout/app-shell", () => ({
  AppShell: ({ children }: { children: React.ReactNode }) => (
    <div>
      <div>Operator shell</div>
      {children}
    </div>
  ),
}))

function renderAt(path: string) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/" element={<App />}>
          <Route index element={<div>Startup resolver</div>} />
          <Route path="dashboard" element={<div>Dashboard page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe("STARTUP-01A application shell boundary", () => {
  it("keeps the canonical root resolver outside setup and operator navigation", () => {
    renderAt("/")

    expect(screen.getByText("Startup resolver")).toBeInTheDocument()
    expect(screen.queryByText("Operator shell")).not.toBeInTheDocument()
  })

  it("restores the operator shell after startup routing", () => {
    renderAt("/dashboard")

    expect(screen.getByText("Operator shell")).toBeInTheDocument()
    expect(screen.getByText("Dashboard page")).toBeInTheDocument()
  })
})
