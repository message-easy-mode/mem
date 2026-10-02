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

const chatServerKeys = [
  "chat-servers",
  "chat-servers/create",
  "chat-servers/workspace",
  "chat-servers/users",
  "chat-servers/passwords-and-accounts",
  "chat-servers/network-and-domains",
  "chat-servers/voice-and-video",
  "chat-servers/federation",
  "chat-servers/storage-and-media",
  "chat-servers/daily-operations",
  "chat-servers/remove",
  "chat-servers/troubleshooting",
] as const

describe("MEM 0.2.0 chat-server documentation", () => {
  it("ships one supported English and German variant for every chat-server topic", () => {
    const manifest = JSON.parse(manifestJson) as DocumentationManifest
    const chatServerDocuments = manifest.documents.filter(
      (document) => document.groupKey === "chat-servers",
    )

    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(chatServerDocuments).toHaveLength(chatServerKeys.length * 2)

    for (const key of chatServerKeys) {
      const variants = chatServerDocuments.filter((document) => document.translationKey === key)

      expect(variants.map((document) => document.locale).sort()).toEqual(["de", "en"])
      expect(variants.every((document) => document.status === "supported")).toBe(true)
      expect(variants.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
    }
  })

  it("documents the current stack, account, network, TURN, federation, storage, and removal boundaries", () => {
    const view = getDocumentationView("en")
    const overview = view.documentByKey.get("chat-servers")!
    const create = view.documentByKey.get("chat-servers/create")!
    const workspace = view.documentByKey.get("chat-servers/workspace")!
    const users = view.documentByKey.get("chat-servers/users")!
    const accounts = view.documentByKey.get("chat-servers/passwords-and-accounts")!
    const network = view.documentByKey.get("chat-servers/network-and-domains")!
    const turn = view.documentByKey.get("chat-servers/voice-and-video")!
    const federation = view.documentByKey.get("chat-servers/federation")!
    const storage = view.documentByKey.get("chat-servers/storage-and-media")!
    const daily = view.documentByKey.get("chat-servers/daily-operations")!
    const remove = view.documentByKey.get("chat-servers/remove")!
    const troubleshooting = view.documentByKey.get("chat-servers/troubleshooting")!

    expect(overview.markdown).toContain("Matrix accounts are not MEM Control Plane accounts")
    expect(create.markdown).toContain("matrix-family.example.org")
    expect(create.markdown).toContain("per-stack PostgreSQL database and role")
    expect(create.markdown).toContain("mem-gateway")
    expect(create.markdown).toContain("coturn")
    expect(workspace.markdown).toContain("Run doctor")
    expect(workspace.markdown).toContain("Create backup")
    expect(workspace.markdown).toContain("generic edit, start, stop, or restart controls")
    expect(users.markdown).toContain("Synapse database")
    expect(users.markdown).toContain("first Matrix administrator")
    expect(users.markdown).toContain("`.`")
    expect(users.markdown).toContain("`_`")
    expect(users.markdown).toContain("`-`")
    expect(users.markdown).toContain("`=`")
    expect(accounts.markdown).toContain("does not store that password")
    expect(accounts.markdown).toContain("signs the user out")
    expect(accounts.markdown).toContain("end-to-end encryption keys")
    expect(accounts.markdown).toContain("last active Matrix administrator")
    expect(network.markdown).toContain("matrix-family.example.org")
    expect(network.markdown).toContain("certificate expiry")
    expect(network.markdown).toContain("Run doctor")
    expect(turn.markdown).toContain("External TURN configuration")
    expect(turn.markdown).toContain("does not place a test call")
    expect(turn.markdown).toContain("Active calls may be interrupted")
    expect(turn.markdown).toContain("rollback")
    expect(turn.markdown).toContain("49160-49200/udp")
    expect(turn.markdown).toContain("Split DNS")
    expect(turn.markdown).toContain("Probe-runtime DNS resolution")
    expect(federation.markdown).toContain("exact-domain allowlist")
    expect(federation.markdown).toContain("Wildcards")
    expect(federation.markdown).toContain("Local-only")
    expect(federation.markdown).toContain("existing users, rooms, messages")
    expect(storage.markdown).toContain("top-level **Storage** workspace")
    expect(storage.markdown).toContain("aggregate MEM-managed Matrix-media totals")
    expect(storage.markdown).toContain("MEM data filesystem")
    expect(storage.markdown).toContain("percentage remaining")
    expect(storage.markdown).toContain("**Chat servers → <server> → Services**")
    expect(storage.markdown).toContain("Technical filesystem paths")
    expect(storage.markdown).toContain("PostgreSQL")
    expect(storage.markdown).toContain("media directory")
    expect(storage.markdown).toContain("signing key")
    expect(daily.markdown).toContain("generic container start, stop, restart")
    expect(daily.markdown).toContain("Keep the Control Plane private")
    expect(remove.markdown).toContain("deliberately keeps")
    expect(remove.markdown).toContain("PostgreSQL database")
    expect(remove.markdown).toContain("local Matrix and Element files")
    expect(remove.markdown).toContain("step-up")
    expect(troubleshooting.markdown).toContain("operation ID")
    expect(troubleshooting.markdown).toContain("report ID")
    expect(troubleshooting.markdown).toContain("/var/lib/message-easy-mode/instances")
    expect(troubleshooting.markdown).toContain("npm:81")
    expect(troubleshooting.markdown).toContain("Never include")
  })
})
