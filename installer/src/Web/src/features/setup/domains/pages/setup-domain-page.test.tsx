import { http, HttpResponse } from "msw"
import { MemoryRouter, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { SetupDomainPage } from "./setup-domain-page"

const endpoint = "/api/setup/domains/plan"
const validateEndpoint = "/api/setup/domains/plan/validate"

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location-probe">{location.pathname}</span>
}

function renderPage(initialEntry = "/setup/domain") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <SetupDomainPage />
      <LocationProbe />
    </MemoryRouter>,
  )
}

function emptyPlan() {
  return {
    installationId: "11111111-1111-1111-1111-111111111111",
    configured: false,
    succeeded: false,
    status: "NotConfigured",
    message: "Domain planning has not been validated yet.",
    errorCode: null,
    errorDetail: null,
    domain: "",
    zone: "",
    acmeEmail: "",
    dnsProvider: "desec",
    useStaging: false,
    providerAccessConfirmed: false,
    providerCredentialStored: false,
    validatedAtUtc: null,
    evidence: [],
  }
}

function validatedPlan() {
  return {
    installationId: "11111111-1111-1111-1111-111111111111",
    configured: true,
    succeeded: true,
    status: "Validated",
    message: "Domain plan validated. No DNS records or certificates have been changed yet.",
    errorCode: null,
    errorDetail: null,
    domain: "*.example.com",
    zone: "example.com",
    acmeEmail: "admin@example.com",
    dnsProvider: "desec",
    useStaging: false,
    providerAccessConfirmed: true,
    providerCredentialStored: true,
    validatedAtUtc: "2026-08-11T05:00:00Z",
    evidence: [
      {
        key: "dns.providerAccess",
        value: "read-only access confirmed",
        sensitive: false,
        status: "Succeeded",
      },
      {
        key: "externalMutation",
        value: "none; DNS and ACME mutation is deferred until after Review",
        sensitive: false,
        status: "Skipped",
      },
    ],
  }
}

describe("SetupDomainPage validation-only boundary", () => {
  it("validates and stores the plan without presenting certificate mutation as a Domain-step action", async () => {
    const user = userEvent.setup()
    let requestBody: Record<string, unknown> | null = null

    server.use(
      http.get(endpoint, () => HttpResponse.json(emptyPlan())),
      http.post(validateEndpoint, async ({ request }) => {
        requestBody = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(validatedPlan())
      }),
    )

    renderPage()

    expect(await screen.findByText("Validation only")).toBeInTheDocument()
    expect(screen.getByText(/does not create TXT records/i)).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /create certificate/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /cancel/i })).not.toBeInTheDocument()
    expect(screen.getByRole("checkbox", { name: /Use Let's Encrypt staging during installation/i })).not.toBeChecked()
    expect(screen.getByText(/staging certificates are not trusted by normal browsers/i)).toBeInTheDocument()

    await user.type(screen.getByLabelText("Base domain"), "https://Example.COM/")
    await user.type(screen.getByLabelText("ACME email"), "admin@example.com")
    await user.type(screen.getByLabelText("deSEC token"), "secret-desec-token")
    await user.click(screen.getByRole("button", { name: "Validate domain plan" }))

    expect(await screen.findByText("Domain plan validated")).toBeInTheDocument()
    expect(screen.getByText("Read-only access confirmed")).toBeInTheDocument()
    expect(screen.getByText("Stored protected")).toBeInTheDocument()
    expect(
      screen.getByText("Enabled after successful production certificate provisioning"),
    ).toBeInTheDocument()
    expect(screen.getByText(/Review is still the consent boundary/i)).toBeInTheDocument()

    expect(requestBody).toMatchObject({
      baseDomain: "example.com",
      acmeEmail: "admin@example.com",
      dnsProvider: "desec",
      providerToken: "secret-desec-token",
      useStaging: false,
    })

    expect(screen.queryByDisplayValue("secret-desec-token")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Continue to review" }))
    expect(screen.getByTestId("location-probe")).toHaveTextContent("/setup/review")
  })

  it("reopens a validated plan without returning the protected provider token to the browser", async () => {
    const user = userEvent.setup()

    server.use(http.get(endpoint, () => HttpResponse.json(validatedPlan())))

    renderPage()

    expect(await screen.findByText("Domain plan validated")).toBeInTheDocument()
    expect(screen.queryByLabelText("deSEC token")).not.toBeInTheDocument()
    expect(screen.getByText("Stored protected")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Edit plan" }))

    expect(screen.getByLabelText("Base domain")).toHaveValue("example.com")
    expect(screen.getByLabelText("ACME email")).toHaveValue("admin@example.com")
    expect(screen.getByLabelText("deSEC token")).toHaveValue("")
    expect(screen.getByRole("button", { name: "Validate updated plan" })).toBeDisabled()
  })

  it("shows an actionable zone-not-accessible message with provider status evidence", async () => {
    const user = userEvent.setup()

    server.use(
      http.get(endpoint, () => HttpResponse.json(emptyPlan())),
      http.post(validateEndpoint, () =>
        HttpResponse.json({
          ...emptyPlan(),
          status: "ValidationFailed",
          message:
            "deSEC could not find DNS zone 'wrong.example' in the account accessible with this token. Check the base domain and confirm the token belongs to the account that manages this zone.",
          errorCode: "DesecZoneNotAccessible",
          evidence: [
            {
              key: "desecResponse",
              value: "404 NotFound",
              sensitive: false,
              status: "Failed",
            },
          ],
        }),
      ),
    )

    renderPage()

    await screen.findByRole("button", { name: "Validate domain plan" })
    await user.type(screen.getByLabelText("Base domain"), "wrong.example")
    await user.type(screen.getByLabelText("ACME email"), "admin@example.com")
    await user.type(screen.getByLabelText("deSEC token"), "bad-zone-token")
    await user.click(screen.getByRole("button", { name: "Validate domain plan" }))

    expect(
      await screen.findByText(/could not find DNS zone 'wrong\.example'/i),
    ).toBeInTheDocument()
    expect(screen.getByText("404 NotFound")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Continue to review" })).not.toBeInTheDocument()
  })

  it("keeps provider validation failure in the planning step without offering Review continuation", async () => {
    const user = userEvent.setup()

    server.use(
      http.get(endpoint, () => HttpResponse.json(emptyPlan())),
      http.post(validateEndpoint, () =>
        HttpResponse.json({
          ...emptyPlan(),
          status: "ValidationFailed",
          message: "deSEC rejected the credential.",
          errorCode: "DesecUnauthorized",
          evidence: [],
        }),
      ),
    )

    renderPage()

    await screen.findByRole("button", { name: "Validate domain plan" })
    await user.type(screen.getByLabelText("Base domain"), "example.com")
    await user.type(screen.getByLabelText("ACME email"), "admin@example.com")
    await user.type(screen.getByLabelText("deSEC token"), "bad-token")
    await user.click(screen.getByRole("button", { name: "Validate domain plan" }))

    expect(await screen.findByText("deSEC rejected the credential.")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Continue to review" })).not.toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Validate domain plan" })).toBeEnabled(),
    )
  })
  it("keeps completed-Setup preview read-only instead of offering domain mutation", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(emptyPlan())))

    renderPage("/setup/domain?setupPreview=1")

    expect(
      await screen.findByText("Domain planning is read-only in this preview"),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText("Base domain")).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Validate domain plan" }),
    ).not.toBeInTheDocument()
  })

})
