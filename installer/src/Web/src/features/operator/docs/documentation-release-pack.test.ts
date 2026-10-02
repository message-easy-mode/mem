import { describe, expect, it } from "vitest"

import {
  documentationDocuments,
  documentationNavigationGroups,
  documentationReleasePack,
  findDocumentationDocument,
  getDocumentationReleasePack,
  getDocumentationReadingContext,
  getDocumentationReleasePackFiles,
  getDocumentationView,
  resolveDocumentationHref,
  resolveDocumentationRoute,
} from "./documentation-release-pack"

function stripMarkdownCodeForBrandingAudit(markdown: string) {
  return markdown
    .replace(/```[\s\S]*?```/gu, "")
    .replace(/~~~[\s\S]*?~~~/gu, "")
    .replace(/`+[^`]*`+/gu, "")
}

describe("documentation release pack", () => {
  it("loads the converted Markdown pack with explicit locale and canonical document identity", () => {
    expect(documentationReleasePack.documentCount).toBe(documentationDocuments.length)
    expect(documentationReleasePack.reviewRequired).toBe(false)
    expect(documentationDocuments.length).toBeGreaterThan(0)
    expect(documentationNavigationGroups.map((group) => group.id)).toEqual([
      "start",
      "start-de",
      "installation",
      "installation-de",
      "chat-servers",
      "chat-servers-de",
      "backups-restores",
      "backups-restores-de",
      "migrate",
      "migrate-de",
      "architecture",
      "operations",
      "operations-de",
      "tools",
      "guides",
      "tools-de",
      "releases",
      "cli",
      "cli-de",
    ])

    const welcome = findDocumentationDocument("start/welcome")
    const germanWelcome = findDocumentationDocument("de/start/welcome")
    const installation = findDocumentationDocument("installation")

    expect(welcome).toMatchObject({ key: "start/welcome", locale: "en" })
    expect(germanWelcome).toMatchObject({ key: "start/welcome", locale: "de" })
    expect(welcome?.markdown).not.toMatch(/^---/)
    expect(welcome?.markdown).toContain("MEM means **Message Easy Mode**")
    expect(installation?.headings).toContainEqual({
      id: "installation-sequence",
      label: "Installation sequence",
      level: 2,
    })

    const deviceLogin = findDocumentationDocument("cli/device-login")
    const germanDeviceLogin = findDocumentationDocument("de/cli/geraeteanmeldung")
    const securitySettings = findDocumentationDocument("operations/security-and-access-settings")
    const germanSecuritySettings = getDocumentationView("de").documentByKey.get(
      "operations/security-and-access-settings",
    )

    expect(deviceLogin).toMatchObject({ key: "cli/device-login", locale: "en" })
    expect(germanDeviceLogin).toMatchObject({ key: "cli/device-login", locale: "de" })
    expect(deviceLogin?.markdown).toContain("mem login --device")
    expect(germanDeviceLogin?.markdown).toContain("Geräteanmeldung")
    expect(securitySettings?.markdown).toContain("High-risk step-up")
    expect(germanSecuritySettings).toMatchObject({
      key: "operations/security-and-access-settings",
      locale: "de",
    })
  })

  it("projects one language-aware catalog and release-pack summary at a time", () => {
    const englishView = getDocumentationView("en")
    const germanView = getDocumentationView("de")
    const englishPack = getDocumentationReleasePack("en")
    const germanPack = getDocumentationReleasePack("de")

    const englishDocumentCount = documentationDocuments.filter(
      (document) => document.locale === "en",
    ).length
    const germanDocumentCount = documentationDocuments.filter(
      (document) => document.locale === "de",
    ).length

    expect(englishView.documents).toHaveLength(englishDocumentCount)
    expect(germanView.documents).toHaveLength(germanDocumentCount)
    expect(englishView.documents.every((document) => document.locale === "en")).toBe(true)
    expect(germanView.documents.every((document) => document.locale === "de")).toBe(true)
    expect(englishView.defaultDocument.key).toBe("start/welcome")
    expect(germanView.defaultDocument.key).toBe("start/welcome")
    expect(englishView.documentByKey.get("cli/device-login")?.title).toBe(
      "Sign in with device login",
    )
    expect(germanView.documentByKey.get("cli/device-login")?.title).toBe(
      "Mit Geräteanmeldung anmelden",
    )
    expect(germanView.navigationGroups.map((group) => group.key)).toEqual([
      "start",
      "installation",
      "chat-servers",
      "backups-restores",
      "migrate",
      "operations",
      "tools",
      "cli",
    ])
    expect(englishPack).toMatchObject({
      language: "en",
      documentCount: englishDocumentCount,
      downloadFileCount: englishDocumentCount + 5,
      downloadName: "mem-docs-v0.2.0-en.zip",
    })
    expect(germanPack).toMatchObject({
      language: "de",
      documentCount: germanDocumentCount,
      downloadFileCount: germanDocumentCount + 5,
      downloadName: "mem-docs-v0.2.0-de.zip",
    })
  })

  it("projects the current MEM identity while retaining the historical 0.1.0 name", () => {
    const englishPack = getDocumentationReleasePack("en")
    const currentDocuments = documentationDocuments.filter(
      (document) => document.key !== "releases/0.1.0",
    )
    const legacyRelease = documentationDocuments.find(
      (document) => document.key === "releases/0.1.0",
    )

    expect(englishPack).toMatchObject({
      productName: "MEM",
      productFullName: "Message Easy Mode",
      controlPlaneName: "MEM Control Plane",
      packName: "MEM Documentation",
      brandingProjectionVersion: 1,
    })
    expect(
      currentDocuments.every((document) =>
        !/MatrixEasyMode|Matrix Easy Mode/u.test(
          [
            document.title,
            document.summary,
            document.tags.join(" "),
            document.headings.map((heading) => heading.label).join(" "),
            stripMarkdownCodeForBrandingAudit(document.markdown),
          ].join("\n"),
        ),
      ),
    ).toBe(true)
    expect(
      documentationDocuments.find((document) => document.key === "operations/configuration")
        ?.markdown,
    ).toContain("/var/lib/message-easy-mode")
    expect(legacyRelease?.title).toBe("MatrixEasyMode v0.1.0")
    expect(legacyRelease?.markdown).toContain("MatrixEasyMode v0.1.0")
  })

  it("builds language-scoped previous, next, and related reading context", () => {
    const englishContext = getDocumentationReadingContext("en", "cli/device-login")
    const germanContext = getDocumentationReadingContext("de", "cli/device-login")

    expect(englishContext.previousDocument).toMatchObject({
      key: "cli/profiles",
      locale: "en",
    })
    expect(englishContext.nextDocument).toMatchObject({
      key: "cli/account-and-logout",
      locale: "en",
    })
    expect(germanContext.previousDocument).toMatchObject({
      key: "cli/profiles",
      locale: "de",
    })
    expect(germanContext.nextDocument).toMatchObject({
      key: "cli/account-and-logout",
      locale: "de",
    })
    expect(englishContext.relatedDocuments).toHaveLength(3)
    expect(germanContext.relatedDocuments).toHaveLength(3)
    expect(englishContext.relatedDocuments.every((document) => document.locale === "en")).toBe(true)
    expect(germanContext.relatedDocuments.every((document) => document.locale === "de")).toBe(true)
    expect(englishContext.relatedDocuments.some((document) => document.key === "cli/device-login")).toBe(false)
    expect(germanContext.relatedDocuments.some((document) => document.key === "cli/device-login")).toBe(false)
  })

  it("builds portable files containing only the selected documentation language", async () => {
    const englishFiles = await getDocumentationReleasePackFiles("en")
    const germanFiles = await getDocumentationReleasePackFiles("de")
    const englishPaths = englishFiles.map((file) => file.path)
    const germanPaths = germanFiles.map((file) => file.path)

    expect(englishFiles).toHaveLength(getDocumentationReleasePack("en").downloadFileCount)
    expect(germanFiles).toHaveLength(getDocumentationReleasePack("de").downloadFileCount)
    expect(englishPaths.some((path) => path.startsWith("docs/de/"))).toBe(false)
    expect(germanPaths.filter((path) => path.startsWith("docs/")).every((path) => path.startsWith("docs/de/"))).toBe(true)
    expect(englishPaths).toContain("docs/start/welcome.md")
    expect(englishPaths).toContain("docs/installation/index.md")
    expect(englishPaths).toContain("docs/chat-servers/index.md")
    expect(englishPaths).toContain("docs/backups-and-restores/index.md")
    expect(englishPaths).toContain("docs/migrate/index.md")
    expect(germanPaths).toContain("docs/de/start/welcome.md")
    expect(germanPaths).toContain("docs/de/chat-servers/index.md")
    expect(germanPaths).toContain("docs/de/backups-und-wiederherstellen/index.md")
    expect(germanPaths).toContain("docs/de/migrieren/index.md")
    expect(germanPaths).toContain("docs/de/cli/geraeteanmeldung.md")
    expect(englishPaths).toContain("assets/brand/mem-logo-docs.png")
    expect(germanPaths).toContain("assets/brand/mem-logo-docs.png")
    expect(
      englishFiles.find((file) => file.path === "assets/brand/mem-logo-docs.png")?.binaryContent
        ?.byteLength,
    ).toBeGreaterThan(0)
    expect(
      germanFiles.find((file) => file.path === "assets/brand/mem-logo-docs.png")?.binaryContent
        ?.byteLength,
    ).toBeGreaterThan(0)
    expect(englishFiles.find((file) => file.path === "README.md")?.content).toContain(
      "English-language documents",
    )
    expect(germanFiles.find((file) => file.path === "README.md")?.content).toContain(
      "deutschsprachigen Dokumente",
    )
    expect(englishFiles.find((file) => file.path === "README.md")?.content).toContain(
      "MEM (Message Easy Mode)",
    )
    expect(englishFiles.find((file) => file.path === "docs/architecture/index.md")?.content)
      .not.toMatch(/MatrixEasyMode|Matrix Easy Mode/u)
    expect(englishFiles.find((file) => file.path === "docs/releases/0.1.0.md")?.content)
      .toContain("MatrixEasyMode v0.1.0")

    const englishManifest = JSON.parse(
      englishFiles.find((file) => file.path === "metadata/manifest.json")!.content,
    ) as {
      language: string
      product: { shortName: string; fullName: string; controlPlaneName: string }
      brandingProjectionVersion: number
      documents: readonly {
        id: string
        title: string
        brandingNormalized: boolean
        sourcePackSha256: string
        sourcePackBytes: number
        projectedChecksumIncluded: boolean
      }[]
    }
    const germanManifest = JSON.parse(
      germanFiles.find((file) => file.path === "metadata/manifest.json")!.content,
    ) as { language: string; documents: readonly { id: string }[] }

    expect(englishManifest.language).toBe("en")
    expect(englishManifest.product).toEqual({
      shortName: "MEM",
      fullName: "Message Easy Mode",
      controlPlaneName: "MEM Control Plane",
    })
    expect(englishManifest.brandingProjectionVersion).toBe(1)
    expect(
      englishManifest.documents.find((document) => document.id === "architecture")?.title,
    ).toBe("Architecture")
    expect(
      englishManifest.documents.find((document) => document.id === "releases/0.1.0")
        ?.brandingNormalized,
    ).toBe(false)
    const projectedArchitectureManifest = englishManifest.documents.find(
      (document) => document.id === "architecture",
    )!

    expect(projectedArchitectureManifest.sourcePackSha256).toMatch(/^[a-f0-9]{64}$/u)
    expect(projectedArchitectureManifest.sourcePackBytes).toBeGreaterThan(0)
    expect(projectedArchitectureManifest.projectedChecksumIncluded).toBe(false)
    expect(englishManifest.documents).toHaveLength(getDocumentationView("en").documents.length)
    expect(germanManifest.language).toBe("de")
    expect(germanManifest.documents).toHaveLength(getDocumentationView("de").documents.length)
    expect(germanManifest.documents.map((document) => document.id)).toContain("cli/device-login")
    expect(germanManifest.documents.some((document) => document.id.startsWith("de/"))).toBe(false)
  })

  it("resolves canonical routes, legacy German aliases, and missing translations honestly", () => {
    expect(resolveDocumentationRoute("de", "start/welcome")).toMatchObject({
      kind: "document",
      document: { id: "de/start/welcome", key: "start/welcome" },
    })
    expect(resolveDocumentationRoute("en", "de/start/welcome")).toEqual({
      kind: "redirect",
      documentKey: "start/welcome",
      reason: "legacy-alias",
    })
    expect(resolveDocumentationRoute("de", "cli/device-login")).toMatchObject({
      kind: "document",
      document: { id: "de/cli/geraeteanmeldung", key: "cli/device-login" },
    })
    expect(resolveDocumentationRoute("en", "de/cli/geraeteanmeldung")).toEqual({
      kind: "redirect",
      documentKey: "cli/device-login",
      reason: "legacy-alias",
    })
    expect(resolveDocumentationRoute("de", "installation")).toMatchObject({
      kind: "document",
      document: { id: "de/installation", key: "installation" },
    })
    expect(resolveDocumentationRoute("de", "backups-restores")).toMatchObject({
      kind: "document",
      document: { id: "de/backups-restores", key: "backups-restores" },
    })
    expect(resolveDocumentationRoute("de", "migrate")).toMatchObject({
      kind: "document",
      document: { id: "de/migrate", key: "migrate" },
    })
    expect(resolveDocumentationRoute("de", "architecture")).toEqual({
      kind: "redirect",
      reason: "translation-unavailable",
    })
    expect(resolveDocumentationRoute("en", "not-found")).toEqual({
      kind: "redirect",
      reason: "not-found",
    })
  })

  it("maps only known local Markdown links into canonical documentation reader routes", () => {
    const advancedInstall = findDocumentationDocument("installation/advanced-install")
    const germanDeviceLogin = findDocumentationDocument("de/cli/geraeteanmeldung")

    expect(advancedInstall).toBeDefined()
    expect(resolveDocumentationHref(advancedInstall!, "index.md")).toBe("/docs/installation")
    expect(resolveDocumentationHref(advancedInstall!, "../start/requirements.md")).toBe(
      "/docs/start/requirements",
    )
    expect(resolveDocumentationHref(germanDeviceLogin!, "/docs/de/cli/konto-und-abmeldung")).toBe(
      "/docs/cli/account-and-logout",
    )
    expect(resolveDocumentationHref(advancedInstall!, "https://example.org")).toBeUndefined()
    expect(resolveDocumentationHref(advancedInstall!, "#unsupported-advanced-paths")).toBeUndefined()
    expect(resolveDocumentationHref(advancedInstall!, "../../outside.md")).toBeUndefined()
  })
})
