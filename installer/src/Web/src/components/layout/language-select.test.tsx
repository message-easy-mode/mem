import { afterEach, describe, expect, it } from "vitest"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { LanguageSelect } from "@/components/layout/language-select"
import { renderWithProviders } from "@/test/render-with-providers"

afterEach(() => {
  window.localStorage.clear()
  document.documentElement.lang = ""
})

describe("LanguageSelect", () => {
  it("switches to German and persists the operator choice", async () => {
    const user = userEvent.setup()

    renderWithProviders(<LanguageSelect />)

    const selector = screen.getByRole("combobox", { name: "Language" })
    await user.selectOptions(selector, "de")

    expect(selector).toHaveValue("de")
    expect(screen.getByRole("combobox", { name: "Sprache" })).toBeInTheDocument()
    expect(window.localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe("de")
    expect(document.documentElement.lang).toBe("de")
  })
})
