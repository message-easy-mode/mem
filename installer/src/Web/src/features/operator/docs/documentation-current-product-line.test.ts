import manifestJson from "./release-pack/metadata/manifest.json?raw"

import { describe, expect, it } from "vitest"

type ManifestDocument = Readonly<{
  sourcePath: string
  status: string
  appliesTo: readonly string[]
  preserveLegacyBranding: boolean
}>

type Manifest = Readonly<{
  sourceContentVersion: string
  status: Readonly<{ reviewRequired: boolean; reason: string }>
  documents: readonly ManifestDocument[]
}>

const manifest = JSON.parse(manifestJson) as Manifest

describe("MEM documentation current-product-line contract", () => {
  it("keeps current docs on 0.2.x and isolates legacy material", () => {
    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(manifest.status.reviewRequired).toBe(false)

    const current = manifest.documents.filter(
      (document) => !document.sourcePath.startsWith("docs/legacy/"),
    )
    const legacy = manifest.documents.filter((document) =>
      document.sourcePath.startsWith("docs/legacy/"),
    )

    expect(current.length).toBeGreaterThan(100)
    expect(current.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
    expect(current.every((document) => document.status !== "review-required")).toBe(true)
    expect(current.every((document) => document.status !== "legacy")).toBe(true)
    expect(current.every((document) => document.preserveLegacyBranding === false)).toBe(true)

    expect(legacy.length).toBeGreaterThan(0)
    expect(legacy.every((document) => document.status === "legacy")).toBe(true)
    expect(legacy.every((document) => document.preserveLegacyBranding === true)).toBe(true)
  })
})
