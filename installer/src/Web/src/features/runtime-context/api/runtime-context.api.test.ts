import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { createRuntimeContext } from "../runtime-context.test-fixture"
import { getControlPlaneRuntimeContext } from "./runtime-context.api"

describe("runtime context API", () => {
  it("loads the authenticated server-authored runtime projection", async () => {
    let requestedPath = ""
    server.use(
      http.get("/api/operator/runtime-context", ({ request }) => {
        requestedPath = new URL(request.url).pathname
        return HttpResponse.json(createRuntimeContext())
      }),
    )

    const runtime = await getControlPlaneRuntimeContext()

    expect(requestedPath).toBe("/api/operator/runtime-context")
    expect(runtime.runtimeMode).toBe("containerized-production")
    expect(runtime.controlPlaneInstanceId).toBe(
      "11111111-1111-1111-1111-111111111111",
    )
  })
})
