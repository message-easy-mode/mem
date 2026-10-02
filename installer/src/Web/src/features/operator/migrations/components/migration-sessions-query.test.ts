import { describe, expect, it } from "vitest"

import {
  readMigrationSessionsQuery,
  toMigrationSessionInventoryRequest,
} from "./migration-sessions-query"

describe("migration sessions URL query", () => {
  it("parses the complete URL-backed inventory contract", () => {
    const query = readMigrationSessionsQuery(new URLSearchParams({
      page: "3",
      pageSize: "25",
      search: "  David  ",
      lifecycle: "active",
      action: "review",
      stage: "prepare-and-test",
      targetStack: "tester",
      sortBy: "stage",
      sortDirection: "asc",
      includeArchived: "true",
    }))

    expect(query).toEqual({
      page: 3,
      pageSize: 25,
      search: "David",
      lifecycle: "active",
      action: "review",
      stage: "prepare-and-test",
      targetStack: "tester",
      sortBy: "stage",
      sortDirection: "asc",
      includeArchived: true,
    })
    expect(toMigrationSessionInventoryRequest(query)).toEqual({
      page: 3,
      pageSize: 25,
      search: "David",
      lifecycle: "active",
      action: "review",
      stage: "prepare-and-test",
      targetStack: "tester",
      sortBy: "stage",
      sortDirection: "asc",
      includeArchived: true,
    })
  })

  it("normalizes invalid values and makes archived history explicit", () => {
    expect(readMigrationSessionsQuery(new URLSearchParams({
      page: "0",
      pageSize: "100",
      lifecycle: "deleted",
      action: "erase",
      stage: "unknown",
      sortBy: "size",
      sortDirection: "sideways",
    }))).toEqual({
      page: 1,
      pageSize: 10,
      search: "",
      lifecycle: "all",
      action: "all",
      stage: "",
      targetStack: "",
      sortBy: "updated",
      sortDirection: "desc",
      includeArchived: false,
    })

    expect(readMigrationSessionsQuery(new URLSearchParams({
      lifecycle: "archived",
    })).includeArchived).toBe(true)
  })
})
