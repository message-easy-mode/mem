import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import { DocumentationPackDetailsAction } from "./documentation-pack-details-action"

afterEach(() => {
  window.localStorage.clear()
})

describe("DocumentationPackDetailsAction", () => {
  it.skip("shows the English language-scoped pack facts", async () => {
    const user = userEvent.setup()
    const { container } = renderWithProviders(<DocumentationPackDetailsAction />)
    const details = container.querySelector("details")
    const summary = container.querySelector("summary")

    await user.click(summary!)

    expect(details).toHaveAttribute("open")
    expect(screen.getAllByText("Docs pack details")).toHaveLength(2)
    expect(screen.getByText("Source content version")).toBeInTheDocument()
    expect(screen.getByText("v0.2.0")).toBeInTheDocument()
    expect(screen.getByText("87")).toBeInTheDocument()
    expect(screen.getByText("91")).toBeInTheDocument()
    expect(screen.getByText("Accuracy review required")).toBeInTheDocument()
  })

  it.skip("shows only the German language-scoped pack facts in German mode", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    const user = userEvent.setup()
    const { container } = renderWithProviders(<DocumentationPackDetailsAction />)

    await user.click(container.querySelector("summary")!)

    expect(screen.getByText("Quellinhaltsversion")).toBeInTheDocument()
    expect(screen.getByText("75")).toBeInTheDocument()
    expect(screen.getByText("79")).toBeInTheDocument()
    expect(screen.getByText("Genauigkeitsprüfung erforderlich")).toBeInTheDocument()
  })
})
