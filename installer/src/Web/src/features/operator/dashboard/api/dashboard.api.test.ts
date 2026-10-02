import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { dashboardOverviewFixture } from "../dashboard.test-fixtures"
import { dashboardOverviewEndpoint, getDashboardOverview } from "./dashboard.api"

describe("getDashboardOverview", () => {
  it("requests the single authenticated overview route without browser infrastructure credentials", async () => {
    server.use(
      http.get(dashboardOverviewEndpoint, ({ request }) => {
        expect(request.method).toBe("GET")
        expect(request.headers.get("x-mem-agent-secret")).toBeNull()
        expect(request.headers.get("authorization")).toBeNull()

        return HttpResponse.json(dashboardOverviewFixture)
      }),
    )

    await expect(getDashboardOverview()).resolves.toEqual(dashboardOverviewFixture)
  })
})
