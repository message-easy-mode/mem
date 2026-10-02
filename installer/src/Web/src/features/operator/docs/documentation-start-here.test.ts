import manifestJson from "./release-pack/metadata/manifest.json?raw"

import { describe, expect, it } from "vitest"

import { getDocumentationView } from "./documentation-release-pack"

const startDocumentKeys = [
  "start/welcome",
  "start/what-is-mem",
  "start/is-mem-right-for-you",
  "start/packages",
  "start/install-or-migrate",
  "start/requirements",
  "start/known-limitations",
  "start/glossary",
] as const

type ManifestDocument = Readonly<{
  translationKey: string
  locale: "en" | "de"
  status: string
  appliesTo: readonly string[]
}>

type Manifest = Readonly<{
  sourceContentVersion: string
  documents: readonly ManifestDocument[]
}>

const manifest = JSON.parse(manifestJson) as Manifest

describe("MEM 0.2.0 Start here documentation", () => {
  it("ships one reviewed English and German variant for every Start here topic", () => {
    const englishView = getDocumentationView("en")
    const germanView = getDocumentationView("de")
    const startManifestDocuments = manifest.documents.filter((document) =>
      document.translationKey.startsWith("start/"),
    )

    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(startManifestDocuments).toHaveLength(16)

    for (const key of startDocumentKeys) {
      expect(englishView.documentByKey.get(key)).toMatchObject({ key, locale: "en" })
      expect(germanView.documentByKey.get(key)).toMatchObject({ key, locale: "de" })

      const variants = startManifestDocuments.filter(
        (document) => document.translationKey === key,
      )

      expect(variants.map((document) => document.locale).sort()).toEqual(["de", "en"])
      expect(variants.every((document) => document.status === "supported")).toBe(true)
      expect(variants.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
    }
  })

})
