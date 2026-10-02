/// <reference types="node" />

import { readFileSync } from "node:fs"
import path from "node:path"

import manifestJson from "./release-pack/metadata/manifest.json?raw"

import { describe, expect, it } from "vitest"

import { getDocumentationView } from "./documentation-release-pack"

type ManifestDocument = Readonly<{
  id: string
  translationKey: string
  locale: "en" | "de"
  groupKey: string
  status: string
  appliesTo: readonly string[]
}>

type DocumentationManifest = Readonly<{
  sourceContentVersion: string
  documents: readonly ManifestDocument[]
}>

const cliKeys = [
  "cli",
  "cli/install",
  "cli/profiles",
  "cli/device-login",
  "cli/account-and-logout",
  "cli/host-and-stack-commands",
  "cli/backup-commands",
  "cli/restore-commands",
  "cli/json-and-scripting",
  "cli/troubleshooting",
  "cli/security-model",
] as const

const importedCliIdsByKey = {
  cli: ["cli/index", "de/cli/index"],
  "cli/install": ["cli/install", "de/cli/installieren"],
  "cli/profiles": ["cli/profiles", "de/cli/profile"],
  "cli/device-login": ["cli/device-login", "de/cli/geraeteanmeldung"],
  "cli/account-and-logout": ["cli/account-and-logout", "de/cli/konto-und-abmeldung"],
  "cli/host-and-stack-commands": ["cli/host-and-stack-commands", "de/cli/host-und-stack-befehle"],
  "cli/backup-commands": ["cli/backup-commands", "de/cli/backup-befehle"],
  "cli/restore-commands": ["cli/restore-commands", "de/cli/restore-befehle"],
  "cli/json-and-scripting": ["cli/json-and-scripting", "de/cli/json-und-skripting"],
  "cli/troubleshooting": ["cli/troubleshooting", "de/cli/fehlerbehebung"],
  "cli/security-model": ["cli/security-model", "de/cli/sicherheitsmodell"],
} as const satisfies Readonly<Record<(typeof cliKeys)[number], readonly [string, string]>>

const currentCommandLines = [
  "mem profile create <name> --server <url>",
  "mem login --device",
  "mem account show",
  "mem host status",
  "mem stack doctor <slug-or-id>",
  "mem backups export <catalog-entry-id> --out <path>",
  "mem restores private-test destroy <restore-session-id> --yes",
  "mem restores recreate execute <restore-session-id>",
  "mem restores handover complete <restore-session-id> --yes",
] as const

describe("MEM 0.2.0 CLI documentation", () => {
  it("ships one reviewed English and German variant for every CLI topic", () => {
    const manifest = JSON.parse(manifestJson) as DocumentationManifest
    const documents = manifest.documents.filter((document) => document.groupKey === "cli")

    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(documents).toHaveLength(cliKeys.length * 2)

    for (const key of cliKeys) {
      const variants = documents.filter((document) => document.translationKey === key)

      expect(variants.map((document) => document.locale).sort()).toEqual(["de", "en"])
      expect(variants.map((document) => document.id).sort()).toEqual(
        [...importedCliIdsByKey[key]].sort(),
      )
      expect(variants.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
      expect(variants.every((document) => document.status === (key === "cli/install" ? "advanced" : "supported"))).toBe(true)
    }
  })

  it("documents the current command, identity, recovery, and safety boundaries", () => {
    const view = getDocumentationView("en")
    const overview = view.documentByKey.get("cli")!
    const installation = view.documentByKey.get("cli/install")!
    const profiles = view.documentByKey.get("cli/profiles")!
    const login = view.documentByKey.get("cli/device-login")!
    const account = view.documentByKey.get("cli/account-and-logout")!
    const host = view.documentByKey.get("cli/host-and-stack-commands")!
    const backups = view.documentByKey.get("cli/backup-commands")!
    const restores = view.documentByKey.get("cli/restore-commands")!
    const json = view.documentByKey.get("cli/json-and-scripting")!
    const troubleshooting = view.documentByKey.get("cli/troubleshooting")!
    const security = view.documentByKey.get("cli/security-model")!

    expect(overview.markdown).toContain("Create or remove a chat server")
    expect(overview.markdown).toContain("Create a new backup capture")
    expect(overview.markdown).toContain("mem backups")
    expect(overview.markdown).toContain("mem restores")
    expect(installation.markdown).toContain("disabled by default")
    expect(installation.markdown).toContain("/usr/local/bin/mem")
    expect(profiles.markdown).toContain("MEM_SERVER_URL")
    expect(profiles.markdown).toContain("named profile")
    expect(login.markdown).toContain("`--json` is intentionally not supported")
    expect(login.markdown).toContain("short code")
    expect(account.markdown).toContain("server revocation could not be confirmed")
    expect(host.markdown).toContain("Exit code `2`")
    expect(backups.markdown).toContain("does not currently create a new backup capture")
    expect(backups.markdown).toContain("retained uploaded ZIP archive")
    expect(restores.markdown).toContain("--execute-production-recreate")
    expect(restores.markdown).toContain("--acknowledge-no-automatic-rollback")
    expect(json.markdown).toContain("JSON property names, status values, error codes")
    expect(troubleshooting.markdown).toContain("installer_token_retired")
    expect(security.markdown).toContain("MEM_INSTALLER_TOKEN")
    expect(security.markdown).toContain("It never falls back to")
    expect(security.markdown).toContain("--device-credential")
  })

  it("keeps documented command examples anchored to the current CLI help inventory", () => {
    const cliHelp = readFileSync(
      path.resolve(process.cwd(), "../../../cli/src/Mem.Cli/Output/CliHelp.cs"),
      "utf8",
    )
    const installerConfiguration = JSON.parse(
      readFileSync(path.resolve(process.cwd(), "../Api/appsettings.json"), "utf8"),
    ) as { MemCliHostCommand: { Enabled: boolean } }

    for (const commandLine of currentCommandLines) {
      expect(cliHelp).toContain(commandLine)
    }

    expect(installerConfiguration.MemCliHostCommand.Enabled).toBe(false)
  })
})
