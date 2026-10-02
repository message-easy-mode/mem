import { http, HttpResponse } from "msw"
import { setupServer } from "msw/node"

import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"

/**
 * Shared MSW server. The runtime projection is a global AppShell dependency,
 * so tests receive a production-shaped default and may override it per case.
 */
export const server = setupServer(
  http.get("/api/operator/runtime-context", () => HttpResponse.json(
    createRuntimeContext(),
  )),
)
