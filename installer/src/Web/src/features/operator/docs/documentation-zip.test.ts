import { describe, expect, it } from "vitest"

import { createDocumentationZip } from "./documentation-zip"

describe("createDocumentationZip", () => {
  it("rejects unsafe and ambiguous archive layouts before any download is prepared", () => {
    const modifiedAt = new Date("2026-07-06T00:00:00Z")

    expect(() => createDocumentationZip([], modifiedAt)).toThrow("at least one file")
    expect(() => createDocumentationZip([{ path: "../outside.md", content: "x" }], modifiedAt)).toThrow(
      "Unsafe documentation ZIP path",
    )
    expect(() => createDocumentationZip([{ path: "docs\\outside.md", content: "x" }], modifiedAt)).toThrow(
      "Unsafe documentation ZIP path",
    )
    expect(() => createDocumentationZip([
      { path: "docs/example.md", content: "first" },
      { path: "docs/example.md", content: "second" },
    ], modifiedAt)).toThrow("duplicate path")
  })
})
