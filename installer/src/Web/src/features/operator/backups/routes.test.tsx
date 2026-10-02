import { describe, expect, it } from "vitest"

import { backupRoutes } from "./routes"

describe("backup routes", () => {
  it("exposes only canonical restore list and workspace routes", () => {
    expect(backupRoutes.some((route) => route.path === "/restores")).toBe(true)
    expect(
      backupRoutes.some((route) => route.path === "/restores/:restoreSessionId"),
    ).toBe(true)

    expect(
      backupRoutes.some((route) => String(route.path).startsWith("/restore-sessions")),
    ).toBe(false)
  })
})
