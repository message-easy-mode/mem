/// <reference types="node" />

import { readFileSync } from "node:fs"
import path from "node:path"
import { describe, expect, it } from "vitest"

function read(relativePath: string) {
  return readFileSync(path.join(process.cwd(), relativePath), "utf8")
}

describe("MEM 0.2 diagnostics operator documentation", () => {
  it("keeps supported English and German diagnostics documents aligned", () => {
    const english = read("docs/en/operations/diagnostics-command-centre.md")
    const german = read("docs/de/operations/diagnose-kommandocenter.md")

    for (const document of [english, german]) {
      expect(document).toContain('translationKey: "operations/diagnostics-command-centre"')
      expect(document).toContain('status: "supported"')
      expect(document).toContain('appliesTo: ["0.2.x"]')
      expect(document).toContain("sudo docker logs --tail 500 mem-control-plane")
      expect(document).not.toMatch(/docker\s+logs[^\n]*mem-(?:api|web)/i)
      expect(document).toContain("CLEF")
      expect(document).toMatch(/support report|Supportbericht/i)
      expect(document).toMatch(/Platform Owner/)
    }

    expect(english).toContain("does not create a warning or error incident")
    expect(german).toContain("keinen Warnungs- oder Fehler-Vorfall")
  })

  it("documents Seq delivery, runtime, restart, retention, and secret boundaries", () => {
    const english = read("docs/en/operations/seq-with-mem.md")
    const german = read("docs/de/operations/seq-mit-mem.md")

    for (const document of [english, german]) {
      expect(document).toContain('translationKey: "operations/seq-with-mem"')
      expect(document).toContain('status: "supported"')
      expect(document).toMatch(/API restart|API-Neustart/)
      expect(document).toMatch(/data directory|Datenverzeichnis/)
      expect(document).toMatch(/MEM-native Diagnostics/)
      expect(document).toContain("https://datalust.co/docs")
    }

    expect(english).toContain("The browser does not submit image references")
    expect(german).toContain("Der Browser sendet keine Image-Referenz")
    expect(english).toContain("provisions or reuses a dedicated ingest-only MEM credential")
    expect(german).toContain("dedizierten reinen MEM-Ingest-Schlüssel")
    expect(english).toContain("stages the desired delivery state for the next MEM API start")
    expect(german).toContain("für den nächsten Start der MEM-API")
    expect(english).toContain("server-owned restart contract")
    expect(german).toContain("serverseitigen Neustartvertrag")
    expect(english).not.toContain("This setup stage leaves MEM event delivery unchanged")
    expect(german).not.toContain("Diese Einrichtungsstufe verändert die MEM-Ereigniszustellung nicht")
  })

  it("documents pinned Portainer handoff without arbitrary Docker targeting", () => {
    const english = read("docs/en/tools/optional-portainer.md")
    const german = read("docs/de/tools/portainer.md")

    for (const document of [english, german]) {
      expect(document).toContain('translationKey: "tools/optional-portainer"')
      expect(document).toContain("portainer/portainer-ce:2.39.5")
      expect(document).toContain("portainer_data")
      expect(document).toContain("9443")
      expect(document).toContain("8000")
      expect(document).toContain("https://docs.portainer.io/")
    }

    expect(english).toContain("cannot submit an arbitrary Docker container ID")
    expect(german).toContain("kann keine beliebige Docker-Container-ID senden")
    expect(english).toContain("working 2.39.1 instance")
    expect(german).toContain("funktionierenden 2.39.1-Instanz")
  })
})
