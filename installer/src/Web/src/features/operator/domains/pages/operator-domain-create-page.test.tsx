import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorDomainCreatePage } from "./operator-domain-create-page"

const endpoint = "/api/operator/domains"

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location-probe">{location.pathname}</span>
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/domains/new"]}>
      <Routes>
        <Route
          path="/domains/new"
          element={
            <>
              <OperatorDomainCreatePage />
              <LocationProbe />
            </>
          }
        />
        <Route path="/domains/:domainId" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>,
  )
}

function createdDomain() {
  return {
    id: "domain-matrixeasyhost",
    baseDomain: "matrixeasyhost.com",
    displayName: "matrixeasyhost.com",
    purpose: "stack",
    isMainPlatformDomain: false,
    dnsProvider: "desec",
    dnsZone: "matrixeasyhost.com",
    status: "Pending",
    notes: null,
    activeCertificateEntityId: null,
    certificates: [],
    createdAtUtc: "2026-09-11T00:00:00Z",
    updatedAtUtc: "2026-09-11T00:00:00Z",
  }
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("OperatorDomainCreatePage supported-provider contract", () => {
  it("keeps Add Domain registration-only and fixes the guided provider to deSEC", async () => {
    const user = userEvent.setup()
    let requestBody: Record<string, unknown> | null = null

    server.use(
      http.post(endpoint, async ({ request }) => {
        requestBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(createdDomain(), { status: 201 })
      }),
    )

    renderPage()

    expect(screen.getByText("Registration only")).toBeInTheDocument()
    expect(screen.getByText(/does not contact deSEC/i)).toBeInTheDocument()
    expect(screen.getByText(/make no external DNS or certificate changes/i)).toBeInTheDocument()

    const provider = screen.getByLabelText("DNS provider")
    expect(provider).toHaveValue("deSEC")
    expect(provider).toBeDisabled()

    expect(screen.queryByLabelText("deSEC token")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("ACME email")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("Purpose")).not.toBeInTheDocument()
    expect(screen.queryByText("Test / development")).not.toBeInTheDocument()

    await user.type(screen.getByLabelText("Base domain"), "https://MatrixEasyHost.COM/")
    expect(screen.getByText("Planned wildcard certificate: *.matrixeasyhost.com")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Create domain" }))

    await waitFor(() => {
      expect(requestBody).toEqual({
        baseDomain: "matrixeasyhost.com",
        displayName: "matrixeasyhost.com",
        purpose: "stack",
        dnsProvider: "desec",
        dnsZone: "matrixeasyhost.com",
        notes: null,
      })
    })

    await waitFor(() => {
      expect(screen.getByTestId("location-probe")).toHaveTextContent(
        "/domains/domain-matrixeasyhost",
      )
    })
  })

  it("keeps a deliberate delegated DNS-zone override available without exposing provider selection", async () => {
    const user = userEvent.setup()
    let requestBody: Record<string, unknown> | null = null

    server.use(
      http.post(endpoint, async ({ request }) => {
        requestBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(
          {
            ...createdDomain(),
            dnsZone: "delegated.matrixeasyhost.com",
          },
          { status: 201 },
        )
      }),
    )

    renderPage()

    await user.type(screen.getByLabelText("Base domain"), "matrixeasyhost.com")
    await user.click(screen.getByText("Advanced DNS zone"))
    await user.type(
      screen.getByLabelText("DNS zone override"),
      "delegated.matrixeasyhost.com",
    )

    await user.click(screen.getByRole("button", { name: "Create domain" }))

    await waitFor(() => {
      expect(requestBody).toMatchObject({
        baseDomain: "matrixeasyhost.com",
        dnsProvider: "desec",
        dnsZone: "delegated.matrixeasyhost.com",
        purpose: "stack",
      })
    })
  })

  it("renders the guided registration flow in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderPage()

    expect(screen.getByRole("heading", { name: "Domain hinzufügen" })).toBeInTheDocument()
    expect(screen.getByText("Nur Registrierung")).toBeInTheDocument()
    expect(screen.getByLabelText("Basisdomain")).toBeInTheDocument()
    expect(screen.getByLabelText("DNS-Anbieter")).toHaveValue("deSEC")
    expect(screen.getByText("Erweiterte DNS-Zone")).toBeInTheDocument()
    expect(screen.getByText("Optionale Angaben")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Domain erstellen" })).toBeInTheDocument()
  })
})
