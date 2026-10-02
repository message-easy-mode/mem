import type { ReactNode } from "react"
import { screen } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"

import { NpmPage } from "./npm-page"
import { PostgresPage } from "./postgres-page"
import { SeqPage } from "./seq-page"

const postgresEndpoint = "/api/operator/services/postgres/"
const npmEndpoint = "/api/operator/services/npm/"
const seqEndpoint = "/api/operator/services/seq/"

function renderPage(page: ReactNode, path: string) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[path]}>{page}</MemoryRouter>,
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("service runtime workspaces", () => {
  it("presents Postgres as a read-only service workspace without generic lifecycle controls", async () => {
    server.use(
      http.get(postgresEndpoint, () =>
        HttpResponse.json({
          serviceName: "postgres",
          containerName: "mem-postgres",
          preferredHostPort: 5432,
          selectedHostPort: 15432,
          hostDataPath: "/srv/mem/postgres",
          exists: true,
          running: true,
          state: "Running",
          container: {
            id: "postgres-1",
            name: "mem-postgres",
            image: "postgres:16.14",
            state: "running",
            running: true,
            ports: [],
          },
          warnings: [],
        }),
      ),
    )

    renderPage(<PostgresPage />, "/services/postgres")

    expect(await screen.findByRole("heading", { name: "Postgres" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute(
      "href",
      "/services",
    )
    expect(await screen.findByText("postgres:16.14")).toBeInTheDocument()
    expect(screen.getByText("15432")).toBeInTheDocument()
    expect(screen.getByText(/PostgreSQL lifecycle and destructive database management/i)).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /^Start$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /^Stop$/i })).not.toBeInTheDocument()
  })

  it("keeps Nginx Proxy Manager external Open distinct from the MEM service workspace", async () => {
    server.use(
      http.get(npmEndpoint, () =>
        HttpResponse.json({
          serviceName: "npm",
          containerName: "mem-npm",
          hostDataPath: "/srv/mem/npm/data",
          hostLetsEncryptPath: "/srv/mem/npm/letsencrypt",
          httpHostPort: 80,
          adminHostPort: 81,
          httpsHostPort: 443,
          exists: true,
          running: true,
          state: "Running",
          container: {
            id: "npm-1",
            name: "mem-npm",
            image: "jc21/nginx-proxy-manager:2.14.0",
            state: "running",
            running: true,
            ports: [],
          },
          warnings: [],
        }),
      ),
    )

    renderPage(<NpmPage />, "/services/nginx-proxy-manager")

    expect(
      await screen.findByRole("heading", { name: "Nginx Proxy Manager" }),
    ).toBeInTheDocument()
    expect(await screen.findByRole("link", { name: /Open Nginx Proxy Manager/ })).toHaveAttribute(
      "href",
      "http://localhost:81",
    )
    expect(screen.getByRole("link", { name: "Manage Domains & Certificates" })).toHaveAttribute(
      "href",
      "/domains/certificates",
    )
    expect(screen.queryByRole("button", { name: /^Start$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /^Stop$/i })).not.toBeInTheDocument()
  })

  it("uses the Seq service page as a runtime handoff to the richer Diagnostics workspace", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get(seqEndpoint, () =>
        HttpResponse.json({
          serviceName: "seq",
          containerName: "mem-seq-local",
          hostDataPath: null,
          uiHostPort: 16341,
          exists: true,
          running: true,
          state: "Running",
          container: {
            id: "seq-1",
            name: "mem-seq-local",
            image: "datalust/seq:2026.1",
            state: "running",
            running: true,
            ports: [],
          },
          warnings: [],
        }),
      ),
    )

    renderPage(<SeqPage />, "/services/seq")

    expect(await screen.findByRole("heading", { name: "Seq" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Dienste" })).toHaveAttribute(
      "href",
      "/services",
    )
    expect(await screen.findByRole("link", { name: "Seq-Diagnose & Einrichtung öffnen" })).toHaveAttribute(
      "href",
      "/diagnostics/seq",
    )
    expect(await screen.findByText("datalust/seq:2026.1")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /^Start$/i })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /^Stop$/i })).not.toBeInTheDocument()
  })
})
