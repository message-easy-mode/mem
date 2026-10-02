import { describe, expect, it } from "vitest"

import {
  searchDocumentation,
  toDocumentationSearchSnippetText,
  toDocumentationSearchTerms,
} from "./documentation-search"

describe("documentation search index", () => {
  it("tokenises operator queries without sending them to a service", () => {
    expect(toDocumentationSearchTerms("Nginx Proxy Manager")).toEqual(["nginx", "proxy", "manager"])
    expect(toDocumentationSearchTerms("  Docker, docker  ")).toEqual(["docker"])
  })

  it("converts portable source Markdown into readable result snippets", () => {
    expect(
      toDocumentationSearchSnippetText(
        "[!IMPORTANT] **pgAdmin is optional** [Optional pgAdmin](optional-pgadmin.md)",
      ),
    ).toBe("pgAdmin is optional Optional pgAdmin")
  })

  it("ranks English title matches and produces canonical heading-level deep links", async () => {
    const titleResult = (await searchDocumentation("en", "advanced install"))[0]
    const headingResult = (await searchDocumentation("en", "unsupported advanced paths"))[0]

    expect(titleResult.document.id).toBe("installation/advanced-install")
    expect(titleResult.href).toBe("/docs/installation/advanced-install")
    expect(headingResult.href).toBe(
      "/docs/installation/advanced-install#unsupported-advanced-paths",
    )
    expect(headingResult.matchedHeading?.label).toBe("Unsupported advanced paths")
  })

  it("searches and excerpts the current Message Easy Mode branding", async () => {
    const results = await searchDocumentation("en", "Message Easy Mode")

    expect(results.length).toBeGreaterThan(0)
    expect(results.some((result) => /MatrixEasyMode|Matrix Easy Mode/u.test(result.excerpt)))
      .toBe(false)
  })

  it("finds the reviewed Start here guidance in the active language", async () => {
    const englishResults = await searchDocumentation("en", "install or migrate")
    const germanResults = await searchDocumentation("de", "Installieren oder migrieren")

    expect(englishResults[0]?.document.key).toBe("start/install-or-migrate")
    expect(englishResults[0]?.href).toBe("/docs/start/install-or-migrate")
    expect(germanResults[0]?.document.key).toBe("start/install-or-migrate")
    expect(germanResults[0]?.href).toBe("/docs/start/install-or-migrate")
  })

  it("finds reviewed chat-server operations in the active language", async () => {
    const englishResults = await searchDocumentation("en", "Choose the stack identity")
    const germanResults = await searchDocumentation("de", "Stack-Identität wählen")

    expect(englishResults[0]?.document.key).toBe("chat-servers/create")
    expect(englishResults[0]?.href).toBe(
      "/docs/chat-servers/create#choose-the-stack-identity",
    )
    expect(germanResults[0]?.document.key).toBe("chat-servers/create")
    expect(germanResults[0]?.href).toBe(
      "/docs/chat-servers/create#stack-identität-wählen",
    )
  })

  it("finds reviewed backup and restore guidance in the active language", async () => {
    const englishResults = await searchDocumentation("en", "Standard Recreate")
    const germanResults = await searchDocumentation("de", "Standard-Neuerstellung")

    expect(englishResults[0]?.document.key).toBe("backups-restores/standard-recreate")
    expect(englishResults[0]?.href).toBe("/docs/backups-restores/standard-recreate")
    expect(germanResults[0]?.document.key).toBe("backups-restores/standard-recreate")
    expect(germanResults[0]?.href).toBe("/docs/backups-restores/standard-recreate")
  })

  it("finds reviewed migration guidance in the active language", async () => {
    const englishResults = await searchDocumentation("en", "The two workspaces")
    const germanResults = await searchDocumentation("de", "Die beiden Arbeitsbereiche")

    expect(englishResults[0]?.document.key).toBe("migrate")
    expect(englishResults[0]?.href).toBe("/docs/migrate#the-two-workspaces")
    expect(germanResults[0]?.document.key).toBe("migrate")
    expect(germanResults[0]?.href).toBe("/docs/migrate#die-beiden-arbeitsbereiche")
  })

  it("never returns documentation from the inactive language", async () => {
    const englishLoginResults = await searchDocumentation("en", "mem login device")
    const englishGermanResults = await searchDocumentation("en", "Geräteanmeldung")
    const germanLoginResults = await searchDocumentation("de", "Geräteanmeldung")
    const germanEnglishResults = await searchDocumentation("de", "advanced install")

    expect(englishLoginResults.map((result) => result.document.id)).toContain("cli/device-login")
    expect(englishGermanResults).toEqual([])
    expect(germanLoginResults.map((result) => result.document.id)).toContain(
      "de/cli/geraeteanmeldung",
    )
    expect(germanLoginResults[0]?.href).toBe("/docs/cli/device-login")
    expect(germanEnglishResults).toEqual([])
  })

  it.skip("applies language-scoped section and topic filters", async () => {
    const operationsResults = await searchDocumentation("en", "nginx", {
      groupId: "operations",
    })
    const pgAdminResults = await searchDocumentation("en", "optional", { tag: "pgAdmin" })
    const germanSecurityResults = await searchDocumentation("de", "Wiederverwendungsfenster", {
      groupId: "operations",
    })

    expect(operationsResults.length).toBeGreaterThan(0)
    expect(operationsResults.every((result) => result.document.groupKey === "operations")).toBe(true)
    expect(pgAdminResults.length).toBeGreaterThan(0)
    expect(pgAdminResults.every((result) => result.document.tags.includes("pgAdmin"))).toBe(true)
    expect(germanSecurityResults.map((result) => result.document.id)).toContain(
      "de/operations/sicherheits-und-zugriffseinstellungen",
    )
  })

})
