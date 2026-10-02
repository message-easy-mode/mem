import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import { listRestoreAttempts } from "./restore-attempts.api"

describe("Restore Attempts API", () => {
  it("uses the canonical restores endpoint and omits the all lifecycle sentinel", async () => {
    server.use(
      http.get("/internal/host-agent/backups/restores", ({ request }) => {
        const url = new URL(request.url)

        expect(url.searchParams.get("page")).toBe("2")
        expect(url.searchParams.get("pageSize")).toBe("25")
        expect(url.searchParams.get("search")).toBe("restore-1")
        expect(url.searchParams.get("status")).toBeNull()
        expect(url.searchParams.get("targetStack")).toBe("cool-stack")
        expect(url.searchParams.get("sortBy")).toBe("source")
        expect(url.searchParams.get("sortDirection")).toBe("asc")

        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          query: {},
          summary: {},
          totalSessions: 0,
          page: 2,
          pageSize: 25,
          totalPages: 1,
          hasPreviousPage: true,
          hasNextPage: false,
          targetStacks: [],
          sessions: [],
          warnings: [],
          detail: null,
        })
      }),
    )

    await expect(
      listRestoreAttempts({
        page: 2,
        pageSize: 25,
        search: " restore-1 ",
        status: "all",
        targetStack: "cool-stack",
        sortBy: "source",
        sortDirection: "asc",
      }),
    ).resolves.toMatchObject({ page: 2, pageSize: 25 })
  })
})
