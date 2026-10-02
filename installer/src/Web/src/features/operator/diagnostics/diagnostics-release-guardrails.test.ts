/// <reference types="node" />

import { readFileSync } from "node:fs"
import path from "node:path"
import { describe, expect, it } from "vitest"

function read(relativePath: string) {
  return readFileSync(path.join(process.cwd(), relativePath), "utf8")
}

describe("Diagnostics release browser guardrails", () => {
  it("keeps Diagnostics browser code on the operator API boundary", () => {
    const sources = [
      "src/features/operator/diagnostics/api/diagnostics.api.ts",
      "src/features/operator/diagnostics/diagnostics-page.tsx",
      "src/features/operator/diagnostics/diagnostics-logs-page.tsx",
      "src/features/operator/diagnostics/diagnostics-seq-page.tsx",
      "src/features/operator/diagnostics/diagnostics-portainer-page.tsx",
      "src/features/operator/diagnostics/components/diagnostic-docker-evidence.tsx",
    ].map(read).join("\n")

    expect(sources).not.toContain("/internal/host-agent/")
    expect(sources).not.toContain("X-MEM-Agent-Secret")
    expect(sources).not.toContain("Diagnostics__TestFixture__Enabled")
  })

  it("keeps contextual Portainer handoff server-authored", () => {
    const evidence = read(
      "src/features/operator/diagnostics/components/diagnostic-docker-evidence.tsx",
    )
    const seq = read("src/features/operator/diagnostics/diagnostics-seq-page.tsx")
    const portainer = read("src/features/operator/diagnostics/diagnostics-portainer-page.tsx")

    expect(evidence).toContain("/api/operator/diagnostics/portainer/incidents/")
    expect(seq).toContain("/api/operator/diagnostics/portainer/seq/container")
    expect([evidence, seq, portainer].join("\n")).not.toMatch(/containerId\s*[:=]/)
    expect([evidence, seq, portainer].join("\n")).not.toMatch(/localhost:\d+/)
  })
})
