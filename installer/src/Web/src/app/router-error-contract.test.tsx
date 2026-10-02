import { describe, expect, it } from "vitest"

import { router } from "@/app/router"

describe("global router error contract", () => {
  it("owns a root error boundary and authenticated catch-all route", () => {
    const rootRoute = router.routes.find((route) => route.path === "/")

    expect(rootRoute).toBeTruthy()
    expect(rootRoute && "errorElement" in rootRoute && rootRoute.errorElement).toBeTruthy()
    expect(rootRoute?.children?.some((route) => route.path === "*")).toBe(true)
  })
})
