import { QueryClient } from "@tanstack/react-query"

/**
 * A fresh client per test prevents cached queries or mutations from leaking
 * between operator scenarios.
 */
export function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        gcTime: 0,
      },
      mutations: {
        retry: false,
      },
    },
  })
}
