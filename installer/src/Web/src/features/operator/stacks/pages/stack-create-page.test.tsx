import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { StackCreatePage } from "./stack-create-page"

const createEndpoint =
  "/internal/host-agent/commands/create-chat-stack-runtime"
const operationEndpoint = "/internal/host-agent/operations/:operationId"
const domainsEndpoint = "/api/operator/domains"
const imagePolicyEndpoint = "/internal/host-agent/runtime-images/policy"
const operationId = "9411c5eb-6144-491c-af43-574bc1db4eb9"
const stackId = "870fb4ee-d350-4666-8a95-5b18e4de76f3"

function imagePolicyResponse() {
  return {
    schemaVersion: 1,
    synapse: {
      component: "Synapse",
      repository: "matrixdotorg/synapse",
      version: "1.156.0",
      approvedReference: "matrixdotorg/synapse@sha256:6882d26594b87171e0fe807ac6bd7f0000665cd70e73fb88c58ec9bff14c19ce",
    },
    element: {
      component: "Element",
      repository: "vectorim/element-web",
      version: "1.12.23",
      approvedReference: "vectorim/element-web@sha256:2a65f32acc6fd7163523d1c4b5174de354b5ceb085898b15898f4d8ea01a8e3d",
    },
  }
}

function renderPage() {
  server.use(
    http.get(imagePolicyEndpoint, () => HttpResponse.json(imagePolicyResponse())),
  )
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/new"]}>
      <StackCreatePage />
    </MemoryRouter>,
  )
}

function domainResponse() {
  return [
    {
      id: "domain-secondary",
      baseDomain: "other.example.test",
      displayName: "Other",
      purpose: "additional",
      isMainPlatformDomain: false,
      dnsProvider: "desec",
      dnsZone: "example.test",
      status: "active",
      activeCertificateEntityId: null,
      activeCertificateId: null,
      activeCertificateCommonName: null,
      activeCertificateIsStaging: null,
      activeCertificateExpiresAtUtc: null,
      certificateCount: 0,
      createdAtUtc: "2026-07-11T00:00:00Z",
      updatedAtUtc: "2026-07-11T00:00:00Z",
    },
    {
      id: "domain-main",
      baseDomain: "demo.mem.lab",
      displayName: "Main platform domain",
      purpose: "platform",
      isMainPlatformDomain: true,
      dnsProvider: "desec",
      dnsZone: "mem.lab",
      status: "active",
      activeCertificateEntityId: null,
      activeCertificateId: null,
      activeCertificateCommonName: null,
      activeCertificateIsStaging: null,
      activeCertificateExpiresAtUtc: null,
      certificateCount: 0,
      createdAtUtc: "2026-07-11T00:00:00Z",
      updatedAtUtc: "2026-07-11T00:00:00Z",
    },
  ]
}

function acceptedResponse(slug = "demo-stack") {
  return {
    operationId,
    stackId,
    stackSlug: slug,
    status: "accepted",
    pollUrl: `/internal/host-agent/operations/${operationId}`,
  }
}

function operationResponse(
  status: "running" | "succeeded" | "failed",
  currentStep: string,
) {
  const terminal = status !== "running"
  return {
    operationId,
    runtimeStackId: terminal && status === "succeeded" ? stackId : null,
    status,
    currentStep,
    requestedAtUtc: "2026-08-16T01:11:43Z",
    startedAtUtc: "2026-08-16T01:11:43Z",
    completedAtUtc: terminal ? "2026-08-16T01:14:05Z" : null,
    lastError: status === "failed" ? "Synthetic terminal failure" : null,
    terminal,
    succeeded: status === "succeeded",
  }
}

afterEach(() => {
  window.localStorage.clear()
})

describe("StackCreatePage", () => {
  it("preselects the main platform domain and submits the HostAgent acceptance contract", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    let submittedCommand: Record<string, unknown> | undefined

    server.use(
      http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())),
      http.post(createEndpoint, async ({ request }) => {
        submittedCommand = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(acceptedResponse("familie-nord"), { status: 202 })
      }),
      http.get(operationEndpoint, () =>
        HttpResponse.json(operationResponse("succeeded", "complete")),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    expect(
      screen.getByRole("heading", { name: "Chatserver erstellen" }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: "Zurück zu den Chatservern" }),
    ).toHaveAttribute("href", "/stacks")
    expect(screen.getByText("Stack-Zusammenfassung")).toBeInTheDocument()
    expect(await screen.findByText("1.156.0")).toBeInTheDocument()
    expect(screen.getByText("1.12.23")).toBeInTheDocument()
    expect(
      screen.getByText("matrixdotorg/synapse · 1.156.0"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("vectorim/element-web · 1.12.23"),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText("Matrix-Image")).not.toBeInTheDocument()
    expect(screen.queryByDisplayValue("latest")).not.toBeInTheDocument()
    expect(screen.getByLabelText("Stack-Slug")).toHaveValue("demo-stack")

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.clear(screen.getByLabelText("Anzeigename"))
    await user.type(screen.getByLabelText("Anzeigename"), "Familienchat Nord")
    await user.type(screen.getByLabelText("Kategorie"), "Familie")
    await user.clear(screen.getByLabelText("Stack-Slug"))
    await user.type(screen.getByLabelText("Stack-Slug"), "Familie Nord!")

    expect(screen.getByText("Wird erstellt als: familie-nord")).toBeInTheDocument()
    expect(screen.getByText("Matrix-URL")).toBeInTheDocument()
    expect(screen.getByText("Element-URL")).toBeInTheDocument()
    expect(
      screen.getByText("https://matrix-familie-nord.demo.mem.lab"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://chat-familie-nord.demo.mem.lab"),
    ).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Stack erstellen" }))

    await waitFor(() => {
      expect(submittedCommand).toMatchObject({
        stackSlug: "familie-nord",
        displayName: "Familienchat Nord",
        category: "Familie",
        requestedDomainId: "domain-main",
      })
      expect(submittedCommand).not.toHaveProperty("matrixImage")
      expect(submittedCommand).not.toHaveProperty("matrixVersion")
      expect(submittedCommand).not.toHaveProperty("elementImage")
      expect(submittedCommand).not.toHaveProperty("elementVersion")
      expect(submittedCommand?.stackId).toEqual(expect.any(String))
      expect(submittedCommand?.matrixInstanceId).toEqual(expect.any(String))
      expect(submittedCommand?.elementInstanceId).toEqual(expect.any(String))
      expect(submittedCommand?.idempotencyKey).toMatch(
        /^create-stack-familie-nord-/,
      )
    })
  }, 10_000)

  it("uses the two-column workflow at half-screen widths and reserves three columns for wide desktops", async () => {
    server.use(http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())))

    const { container } = renderPage()

    await screen.findByText("1.156.0")

    expect(container.querySelector("[data-stack-create-layout]")).toHaveClass(
      "gap-5",
      "min-[900px]:grid-cols-[minmax(320px,1fr)_minmax(360px,1fr)]",
      "2xl:grid-cols-[minmax(300px,1.05fr)_minmax(250px,0.78fr)_minmax(330px,1.05fr)]",
      "2xl:gap-4",
    )
    expect(container.querySelector("[data-stack-create-page]")).toHaveClass(
      "max-w-[1120px]",
      "space-y-5",
    )
    expect(container.querySelector("[data-stack-create-images]")).toHaveClass(
      "min-[900px]:col-start-1",
      "min-[900px]:row-start-2",
      "2xl:col-start-2",
      "2xl:row-start-1",
    )
    expect(container.querySelectorAll("[data-managed-image-row]")).toHaveLength(2)
    for (const row of container.querySelectorAll("[data-managed-image-row]")) {
      expect(row).toHaveClass("space-y-3")
      expect(row).not.toHaveClass("sm:grid-cols-[minmax(0,1fr)_96px]")
    }
    expect(container.querySelectorAll("[data-managed-image-version]")).toHaveLength(2)
    expect(container.querySelector("[data-stack-create-summary]")).toHaveClass(
      "min-[900px]:col-start-2",
      "min-[900px]:row-span-2",
      "2xl:col-start-3",
    )
    expect(container.querySelector("[data-stack-create-notice]")).toHaveClass(
      "min-[900px]:col-span-2",
      "min-[900px]:row-start-3",
      "2xl:row-start-2",
    )
  })

  it("seeds the stack slug from the first edited display name only", async () => {
    server.use(http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())))

    const user = userEvent.setup()
    renderPage()

    const displayNameInput = screen.getByLabelText("Display name")
    const categoryInput = screen.getByLabelText("Category")
    const slugInput = screen.getByLabelText("Stack slug")

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.clear(displayNameInput)
    await user.type(displayNameInput, "QA Bravo")

    expect(slugInput).toHaveValue("qa-bravo")

    await user.click(categoryInput)

    expect(slugInput).toHaveValue("qa-bravo")
    expect(
      screen.getByText("https://matrix-qa-bravo.demo.mem.lab"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://chat-qa-bravo.demo.mem.lab"),
    ).toBeInTheDocument()

    await user.clear(displayNameInput)
    await user.type(displayNameInput, "QA Bravo Renamed")
    await user.click(categoryInput)

    expect(slugInput).toHaveValue("qa-bravo")
  })

  it("does not replace a stack slug that the operator has taken control of", async () => {
    server.use(http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())))

    const user = userEvent.setup()
    renderPage()

    const displayNameInput = screen.getByLabelText("Display name")
    const categoryInput = screen.getByLabelText("Category")
    const slugInput = screen.getByLabelText("Stack slug")

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.clear(slugInput)
    await user.type(slugInput, "custom-bravo")
    await user.clear(displayNameInput)
    await user.type(displayNameInput, "QA Bravo")
    await user.click(categoryInput)

    expect(slugInput).toHaveValue("custom-bravo")
    expect(
      screen.getByText("https://matrix-custom-bravo.demo.mem.lab"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://chat-custom-bravo.demo.mem.lab"),
    ).toBeInTheDocument()
  })

  it("previews the derived Matrix and Element URLs from the normalized slug and selected domain", async () => {
    server.use(http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())))

    const user = userEvent.setup()
    renderPage()

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    expect(
      screen.getByText(
        "Stable technical identity used to derive the Matrix and Element addresses. Use lowercase letters, numbers, and dashes; for example, demo-stack.",
      ),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://matrix-demo-stack.demo.mem.lab"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://chat-demo-stack.demo.mem.lab"),
    ).toBeInTheDocument()
    expect(screen.getByText("Matrix URL").parentElement).toHaveClass(
      "space-y-1",
    )
    expect(screen.getByText("Element URL").parentElement).toHaveClass(
      "space-y-1",
    )
    expect(
      screen.getByText("https://matrix-demo-stack.demo.mem.lab"),
    ).toHaveClass("break-all")
    expect(
      screen.getByText("https://chat-demo-stack.demo.mem.lab"),
    ).toHaveClass("break-all")

    await user.clear(screen.getByLabelText("Stack slug"))
    await user.type(screen.getByLabelText("Stack slug"), "QA Bravo!")

    expect(screen.getByText("Will be created as: qa-bravo")).toBeInTheDocument()
    expect(
      screen.getByText("https://matrix-qa-bravo.demo.mem.lab"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://chat-qa-bravo.demo.mem.lab"),
    ).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText("Domain"), "domain-secondary")

    expect(
      screen.getByText("https://matrix-qa-bravo.other.example.test"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("https://chat-qa-bravo.other.example.test"),
    ).toBeInTheDocument()
  })

  it("tracks a durable accepted operation without submitting duplicate create requests", async () => {
    let requestCount = 0
    let complete = false

    server.use(
      http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())),
      http.post(createEndpoint, () => {
        requestCount += 1
        return HttpResponse.json(acceptedResponse(), { status: 202 })
      }),
      http.get(operationEndpoint, () =>
        HttpResponse.json(
          complete
            ? operationResponse("succeeded", "complete")
            : operationResponse("running", "generate-synapse-config"),
        ),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.click(screen.getByRole("button", { name: "Create stack" }))

    const dialog = await screen.findByRole("alertdialog")
    expect(dialog).toHaveTextContent("Creating chat server")
    expect(dialog).toHaveTextContent("Generating Matrix configuration")
    expect(dialog).toHaveTextContent(operationId)
    expect(dialog).toHaveTextContent("safely refresh or leave this page")
    expect(requestCount).toBe(1)

    const submitButton = document.querySelector<HTMLButtonElement>('button[type="submit"]')
    expect(submitButton).not.toBeNull()
    expect(submitButton).toBeDisabled()

    await user.keyboard("{Escape}")
    expect(screen.getByRole("alertdialog")).toBeInTheDocument()
    expect(requestCount).toBe(1)

    complete = true
    await waitFor(
      () => {
        expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
      },
      { timeout: 3000 },
    )
    expect(requestCount).toBe(1)
  })

  it("restores durable operation tracking after a browser refresh without resubmitting", async () => {
    let postCount = 0
    window.localStorage.setItem(
      "mem.stack-create.pending-operation",
      JSON.stringify({
        operationId,
        stackId,
        stackSlug: "demo-stack",
        displayName: "Demo Stack",
        domain: "demo.mem.lab",
      }),
    )

    server.use(
      http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())),
      http.post(createEndpoint, () => {
        postCount += 1
        return HttpResponse.json(acceptedResponse(), { status: 202 })
      }),
      http.get(operationEndpoint, () =>
        HttpResponse.json(operationResponse("running", "start-matrix")),
      ),
    )

    renderPage()

    const dialog = await screen.findByRole("alertdialog")
    await waitFor(() => {
      expect(dialog).toHaveTextContent("Starting Matrix")
    })
    expect(dialog).toHaveTextContent(operationId)
    expect(postCount).toBe(0)
  })

  it("shows a terminal operation failure without offering an unsafe automatic retry", async () => {
    let requestCount = 0

    server.use(
      http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())),
      http.post(createEndpoint, () => {
        requestCount += 1
        return HttpResponse.json(acceptedResponse(), { status: 202 })
      }),
      http.get(operationEndpoint, () =>
        HttpResponse.json(operationResponse("failed", "verify-readiness")),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.click(screen.getByRole("button", { name: "Create stack" }))

    const failureDialog = await screen.findByRole("alertdialog")
    await waitFor(() => {
      expect(failureDialog).toHaveTextContent("Stack creation failed")
      expect(failureDialog).toHaveTextContent("Verifying chat-server readiness")
    })
    expect(failureDialog).toHaveTextContent(operationId)
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
    expect(screen.getByRole("button", { name: "Close" })).toBeEnabled()
    expect(requestCount).toBe(1)
  })

  it("guides a pre-mutation platform TURN dependency failure to the shared service workspace", async () => {
    server.use(
      http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())),
      http.post(createEndpoint, () =>
        HttpResponse.json(acceptedResponse(), { status: 202 }),
      ),
      http.get(operationEndpoint, () =>
        HttpResponse.json(operationResponse("failed", "require-platform-turn")),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.click(screen.getByRole("button", { name: "Create stack" }))

    const dialog = await screen.findByRole("alertdialog")
    await waitFor(() => {
      expect(dialog).toHaveTextContent("Stack creation failed")
      expect(dialog).toHaveTextContent("Requiring shared platform TURN readiness")
    })
    expect(dialog).toHaveTextContent("Shared platform TURN is required")
    expect(dialog).toHaveTextContent(
      "MEM stopped before creating stack-specific database, filesystem, container, or route resources",
    )
    expect(screen.getByRole("link", { name: "Open platform TURN" })).toHaveAttribute(
      "href",
      "/services/coturn",
    )
    expect(screen.queryByRole("button", { name: "Try again" })).not.toBeInTheDocument()
  })

  it("localises durable creation failure recovery in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    server.use(
      http.get(domainsEndpoint, () => HttpResponse.json(domainResponse())),
      http.post(createEndpoint, () =>
        HttpResponse.json(acceptedResponse(), { status: 202 }),
      ),
      http.get(operationEndpoint, () =>
        HttpResponse.json(operationResponse("failed", "start-matrix")),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toHaveValue("domain-main")
    })

    await user.click(screen.getByRole("button", { name: "Stack erstellen" }))

    expect(
      await screen.findByText("Stack-Erstellung fehlgeschlagen"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("Fehlgeschlagen bei: Matrix wird gestartet"),
    ).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Schließen" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Diagnose öffnen" })).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Erneut versuchen" }),
    ).not.toBeInTheDocument()
  })

  it("keeps creation disabled when no configured domain is available", async () => {
    server.use(http.get(domainsEndpoint, () => HttpResponse.json([])))

    renderPage()

    await waitFor(() => {
      expect(screen.getByLabelText("Domain")).toBeDisabled()
      expect(screen.getByLabelText("Domain")).toHaveTextContent(
        "No domains available",
      )
    })
    expect(screen.getByRole("button", { name: "Create stack" })).toBeDisabled()
  })
})
