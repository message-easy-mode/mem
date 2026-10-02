/// <reference types="node" />

import { readFileSync, readdirSync } from "node:fs"
import { spawnSync } from "node:child_process"
import path from "node:path"

import { describe, expect, it } from "vitest"

function listMarkdownFiles(root: string): string[] {
  return readdirSync(root, { withFileTypes: true }).flatMap((entry) => {
    const absolute = path.join(root, entry.name)
    return entry.isDirectory()
      ? listMarkdownFiles(absolute)
      : entry.isFile() && entry.name.endsWith(".md")
        ? [absolute]
        : []
  })
}

describe("canonical documentation source", () => {
  it("keeps the checked-in release pack synchronized with docs/", () => {
    const result = spawnSync(
      process.execPath,
      ["./scripts/build-documentation-pack.mjs", "--check"],
      {
        cwd: process.cwd(),
        encoding: "utf8",
      },
    )

    expect(result.stderr).toBe("")
    expect(result.status).toBe(0)
    expect(result.stdout).toMatch(
      /Documentation pack is synchronized: \d+ documents, 1 assets, source content v0\.2\.0\./u,
    )
  })

  it("does not ship retired mem-api/mem-web Docker operations in current documentation", () => {
    const currentFiles = ["en", "de"].flatMap((locale) =>
      listMarkdownFiles(path.join(process.cwd(), "docs", locale)),
    )
    const staleOperationalCommand =
      /\b(?:sudo\s+)?docker\s+(?:logs|exec|inspect|restart|start|stop|rm)\b[^\n]*\bmem-(?:api|web)\b/iu

    for (const file of currentFiles) {
      expect(readFileSync(file, "utf8"), file).not.toMatch(staleOperationalCommand)
    }
  })

  it("uses the canonical pack configuration as the source-content version authority", () => {
    const config = JSON.parse(
      readFileSync(path.join(process.cwd(), "docs/documentation-pack.json"), "utf8"),
    ) as { sourceContentVersion: string }
    const manifest = JSON.parse(
      readFileSync(
        path.join(
          process.cwd(),
          "src/features/operator/docs/release-pack/metadata/manifest.json",
        ),
        "utf8",
      ),
    ) as {
      sourceContentVersion: string
      source: { root: string; format: string }
    }

    expect(config.sourceContentVersion).toBe("0.2.0")
    expect(manifest.sourceContentVersion).toBe(config.sourceContentVersion)
    expect(manifest.source).toEqual({
      root: "docs",
      format: "markdown-frontmatter",
      config: "docs/documentation-pack.json",
    })
  })
})
