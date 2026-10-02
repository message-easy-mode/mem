import type { PropsWithChildren, ReactElement } from "react"
import { render } from "@testing-library/react"
import { QueryClientProvider } from "@tanstack/react-query"

import { I18nProvider } from "@/app/i18n/i18n-provider"
import { ThemeProvider } from "@/app/theme"
import { createTestQueryClient } from "@/test/test-query-client"

export function renderWithProviders(ui: ReactElement) {
  const queryClient = createTestQueryClient()

  function Wrapper({ children }: PropsWithChildren) {
    return (
      <I18nProvider>
        <ThemeProvider>
          <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
        </ThemeProvider>
      </I18nProvider>
    )
  }

  return {
    queryClient,
    ...render(ui, { wrapper: Wrapper }),
  }
}
