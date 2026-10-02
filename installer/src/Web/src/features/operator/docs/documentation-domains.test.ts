import manifestJson from "./release-pack/metadata/manifest.json?raw"

import { describe, expect, it } from "vitest"

import { getDocumentationView } from "./documentation-release-pack"

type ManifestDocument = Readonly<{
  translationKey: string
  locale: "en" | "de"
  status: string
  appliesTo: readonly string[]
}>

type Manifest = Readonly<{
  documents: readonly ManifestDocument[]
}>

const manifest = JSON.parse(manifestJson) as Manifest

function expectSupportedDomainsDocument(locale: "en" | "de") {
  const document = manifest.documents.find(
    (candidate) =>
      candidate.translationKey === "operations/domains-and-certificates" &&
      candidate.locale === locale,
  )

  expect(document).toMatchObject({
    translationKey: "operations/domains-and-certificates",
    locale,
    status: "supported",
  })
  expect(document?.appliesTo).toContain("0.2.x")
}

describe("MEM Domains operator documentation", () => {
  it("documents the current Domain-owned certificate and renewal workflow in English", () => {
    const view = getDocumentationView("en")
    const document = view.documentByKey.get("operations/domains-and-certificates")!

    expect(document).toMatchObject({
      key: "operations/domains-and-certificates",
      locale: "en",
    })
    expectSupportedDomainsDocument("en")
    expect(document.markdown).toContain("registration only")
    expect(document.markdown).toContain("server-owned durable operation")
    expect(document.markdown).toContain("Recorded: Running")
    expect(document.markdown).toContain("cannot become the Domain's active production certificate")
    expect(document.markdown).toContain("Domains → Renewal")
    expect(document.markdown).toContain("Open renewal")
    expect(document.markdown).toContain("stored protected server-side")
    expect(document.markdown).toContain("Advanced ingress diagnostics")
    expect(document.markdown).toContain("does **not** redisplay the deSEC token")
    expect(document.markdown).toContain("Delete Domain-owned certificates explicitly first")
    expect(document.markdown).toContain("Successful production issuance normally performs this enrolment automatically")
  })

  it("ships the matching German Domains operations contract", () => {
    const view = getDocumentationView("de")
    const document = view.documentByKey.get("operations/domains-and-certificates")!

    expect(document).toMatchObject({
      key: "operations/domains-and-certificates",
      locale: "de",
    })
    expectSupportedDomainsDocument("de")
    expect(document.markdown).toContain("nur Registrierung")
    expect(document.markdown).toContain("serverseitiger dauerhafter Vorgang")
    expect(document.markdown).toContain("Aufgezeichnet: Läuft")
    expect(document.markdown).toContain("Domains → Verlängerung")
    expect(document.markdown).toContain("Verlängerung öffnen")
    expect(document.markdown).toContain("geschützt serverseitig gespeichert")
    expect(document.markdown).toContain("zeigt das deSEC-Token **nicht erneut** an")
    expect(document.markdown).toContain("Löschen einer Domain ist kein verstecktes Zertifikats-Cascade")
    expect(document.markdown).toContain("erfolgreiche Produktionsausstellung führt diese Hinterlegung normalerweise automatisch durch")
  })

  it("keeps first-time Setup distinct from post-install Add Domain", () => {
    const english = getDocumentationView("en").documentByKey.get(
      "installation/domain-and-certificate",
    )!
    const german = getDocumentationView("de").documentByKey.get(
      "installation/domain-and-certificate",
    )!

    expect(english.markdown).toContain("normal post-install **Domains → Add Domain** workflow")
    expect(english.markdown).toContain("creates a Domain registry entry only")
    expect(german.markdown).toContain("normale nachträgliche Ablauf **Domains → Domain hinzufügen**")
    expect(german.markdown).toContain("nur einen Domain-Registry-Eintrag")
  })
})
