import { describe, expect, it } from "vitest"

import {
  readRuntimeStackInventoryQuery,
  toRuntimeStackListRequest,
} from "./stack-inventory-query"

describe("runtime stack inventory query", () => {
  it("reads supported URL state and projects the server query contract", () => {
    const state = readRuntimeStackInventoryQuery(new URLSearchParams(
      "page=2&pageSize=25&search=school&status=needs_attention&category=Education&sort=last-checked-desc",
    ))

    expect(state).toEqual({
      page: 2,
      pageSize: 25,
      search: "school",
      status: "needs_attention",
      category: "Education",
      sort: "last-checked-desc",
    })
    expect(toRuntimeStackListRequest(state)).toEqual({
      page: 2,
      pageSize: 25,
      search: "school",
      status: "needs_attention",
      category: "Education",
      sortBy: "lastChecked",
      sortDirection: "desc",
    })
  })

  it("falls back safely for invalid URL values and bounds search length", () => {
    const state = readRuntimeStackInventoryQuery(new URLSearchParams(
      `page=-2&pageSize=11&status=broken&sort=id-desc&category=${"y".repeat(60)}&search=${"x".repeat(250)}`,
    ))

    expect(state.page).toBe(1)
    expect(state.pageSize).toBe(10)
    expect(state.status).toBe("all")
    expect(state.category).toHaveLength(40)
    expect(state.sort).toBe("name-asc")
    expect(state.search).toHaveLength(200)
  })
})
