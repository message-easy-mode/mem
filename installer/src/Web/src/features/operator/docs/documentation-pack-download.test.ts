import { readFile } from "node:fs/promises"
import path from "node:path"

import { afterAll, beforeAll, describe, expect, it, vi } from "vitest"

import {
  getDocumentationReleasePack,
  getDocumentationReleasePackFiles,
} from "@/features/operator/docs/documentation-release-pack"

import { createDocumentationPackArchive } from "./documentation-pack-download"
import { createDocumentationZip } from "./documentation-zip"

const textDecoder = new TextDecoder()
const nativeFetch = globalThis.fetch.bind(globalThis)
const documentationAssetSourcePrefix = "/src/features/operator/docs/release-pack/assets/"

beforeAll(() => {
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const inputUrl =
      typeof input === "string"
        ? input
        : input instanceof URL
          ? input.toString()
          : input.url
    const parsedUrl = new URL(inputUrl, "http://vitest.local")

    if (parsedUrl.pathname.startsWith(documentationAssetSourcePrefix)) {
      const sourcePath = decodeURIComponent(parsedUrl.pathname).replace(/^\/+/, "")
      const sourceBytes = await readFile(path.resolve(process.cwd(), sourcePath))
      const body = new Uint8Array(sourceBytes.byteLength)
      body.set(sourceBytes)

      return new Response(body, { status: 200 })
    }

    return nativeFetch(input, init)
  })
})

afterAll(() => {
  vi.restoreAllMocks()
})

function readUint16(bytes: Uint8Array, offset: number) {
  return bytes[offset] | (bytes[offset + 1] << 8)
}

function readUint32(bytes: Uint8Array, offset: number) {
  return (
    bytes[offset] |
    (bytes[offset + 1] << 8) |
    (bytes[offset + 2] << 16) |
    (bytes[offset + 3] << 24)
  ) >>> 0
}

function readStoredZipEntries(archive: Uint8Array) {
  const endOfCentralDirectoryOffset = archive.byteLength - 22

  expect(readUint32(archive, endOfCentralDirectoryOffset)).toBe(0x06054b50)

  const count = readUint16(archive, endOfCentralDirectoryOffset + 10)
  let centralDirectoryCursor = readUint32(archive, endOfCentralDirectoryOffset + 16)
  const entries = new Map<string, string>()

  for (let index = 0; index < count; index += 1) {
    expect(readUint32(archive, centralDirectoryCursor)).toBe(0x02014b50)
    expect(readUint16(archive, centralDirectoryCursor + 10)).toBe(0)

    const compressedBytes = readUint32(archive, centralDirectoryCursor + 20)
    const uncompressedBytes = readUint32(archive, centralDirectoryCursor + 24)
    const nameBytes = readUint16(archive, centralDirectoryCursor + 28)
    const extraBytes = readUint16(archive, centralDirectoryCursor + 30)
    const commentBytes = readUint16(archive, centralDirectoryCursor + 32)
    const localHeaderOffset = readUint32(archive, centralDirectoryCursor + 42)
    const path = textDecoder.decode(
      archive.slice(centralDirectoryCursor + 46, centralDirectoryCursor + 46 + nameBytes),
    )

    expect(readUint32(archive, localHeaderOffset)).toBe(0x04034b50)
    expect(readUint16(archive, localHeaderOffset + 8)).toBe(0)

    const localNameBytes = readUint16(archive, localHeaderOffset + 26)
    const localExtraBytes = readUint16(archive, localHeaderOffset + 28)
    const contentStart = localHeaderOffset + 30 + localNameBytes + localExtraBytes
    const content = textDecoder.decode(archive.slice(contentStart, contentStart + compressedBytes))

    expect(compressedBytes).toBe(uncompressedBytes)
    entries.set(path, content)
    centralDirectoryCursor += 46 + nameBytes + extraBytes + commentBytes
  }

  return entries
}

function readStoredZipEntryBytes(archive: Uint8Array, wantedPath: string) {
  const endOfCentralDirectoryOffset = archive.byteLength - 22
  const count = readUint16(archive, endOfCentralDirectoryOffset + 10)
  let cursor = readUint32(archive, endOfCentralDirectoryOffset + 16)

  for (let index = 0; index < count; index += 1) {
    const compressedBytes = readUint32(archive, cursor + 20)
    const nameBytes = readUint16(archive, cursor + 28)
    const extraBytes = readUint16(archive, cursor + 30)
    const commentBytes = readUint16(archive, cursor + 32)
    const localHeaderOffset = readUint32(archive, cursor + 42)
    const entryPath = textDecoder.decode(archive.slice(cursor + 46, cursor + 46 + nameBytes))

    if (entryPath === wantedPath) {
      const localNameBytes = readUint16(archive, localHeaderOffset + 26)
      const localExtraBytes = readUint16(archive, localHeaderOffset + 28)
      const contentStart = localHeaderOffset + 30 + localNameBytes + localExtraBytes
      return archive.slice(contentStart, contentStart + compressedBytes)
    }

    cursor += 46 + nameBytes + extraBytes + commentBytes
  }

  return undefined
}

describe("documentation pack download", () => {
  it("creates an English-only portable ZIP", async () => {
    const archive = await createDocumentationPackArchive("en")
    const entries = readStoredZipEntries(archive)
    const pack = getDocumentationReleasePack("en")

    expect(archive.slice(0, 4)).toEqual(new Uint8Array([0x50, 0x4b, 0x03, 0x04]))
    expect(entries).toHaveLength(pack.downloadFileCount)
    expect(entries.size).toBe((await getDocumentationReleasePackFiles("en")).length)
    expect(entries.get("README.md")).toContain("English-language documents")
    expect(entries.get("metadata/manifest.json")).toContain('"language": "en"')
    expect(entries.get("metadata/navigation.json")).toContain('"language": "en"')
    expect(entries.get("metadata/search-index.json")).toContain('"language": "en"')
    expect(entries.get("docs/start/welcome.md")).toContain("# Welcome to MEM")
    expect(entries.get("docs/start/welcome.md")).toContain(
      "../../assets/brand/mem-logo-docs.png",
    )
    expect(readStoredZipEntryBytes(archive, "assets/brand/mem-logo-docs.png")).toBeDefined()
    expect(entries.get("docs/installation/index.md")).toContain("# Install MEM 0.2.0")
    expect(entries.get("docs/chat-servers/index.md")).toContain("# Create and operate chat servers")
    expect(entries.get("docs/backups-and-restores/index.md")).toContain("# Back up and restore chat servers")
    expect(entries.get("docs/migrate/index.md")).toContain("# Migrate from MEM 0.1.0")
    expect(Array.from(entries.keys()).some((path) => path.startsWith("docs/de/"))).toBe(false)
  })

  it("creates a German-only portable ZIP with canonical document identities", async () => {
    const archive = await createDocumentationPackArchive("de")
    const entries = readStoredZipEntries(archive)
    const pack = getDocumentationReleasePack("de")

    expect(entries).toHaveLength(pack.downloadFileCount)
    expect(entries.size).toBe((await getDocumentationReleasePackFiles("de")).length)
    expect(entries.get("README.md")).toContain("deutschsprachigen Dokumente")
    expect(entries.get("metadata/manifest.json")).toContain('"language": "de"')
    expect(entries.get("metadata/manifest.json")).toContain('"id": "cli/device-login"')
    expect(entries.get("docs/de/start/welcome.md")).toContain("# Willkommen bei MEM")
    expect(entries.get("docs/de/start/welcome.md")).toContain(
      "../../../assets/brand/mem-logo-docs.png",
    )
    expect(readStoredZipEntryBytes(archive, "assets/brand/mem-logo-docs.png")).toBeDefined()
    expect(entries.get("docs/de/chat-servers/index.md")).toContain("# Chatserver erstellen und betreiben")
    expect(entries.get("docs/de/backups-und-wiederherstellen/index.md")).toContain("# Chatserver sichern und wiederherstellen")
    expect(entries.get("docs/de/migrieren/index.md")).toContain("# Von MEM 0.1.0 migrieren")
    expect(entries.get("docs/de/cli/geraeteanmeldung.md")).toContain(
      "# Mit Geräteanmeldung anmelden",
    )
    expect(entries.has("docs/installation/index.md")).toBe(false)
    expect(entries.has("docs/backups-and-restores/index.md")).toBe(false)
    expect(
      Array.from(entries.keys())
        .filter((path) => path.startsWith("docs/"))
        .every((path) => path.startsWith("docs/de/")),
    ).toBe(true)
  })

  it("preserves binary documentation assets byte-for-byte in portable ZIPs", () => {
    const imageBytes = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
    const archive = createDocumentationZip(
      [
        { path: "docs/installation/example.md", content: "![Example](../../assets/example.png)\n" },
        { path: "assets/example.png", content: "", binaryContent: imageBytes },
      ],
      new Date("2026-09-17T00:00:00Z"),
    )

    expect(readStoredZipEntryBytes(archive, "assets/example.png")).toEqual(imageBytes)
  })

  it("uses immutable language-specific release names", () => {
    expect(getDocumentationReleasePack("en").downloadName).toBe("mem-docs-v0.2.0-en.zip")
    expect(getDocumentationReleasePack("de").downloadName).toBe("mem-docs-v0.2.0-de.zip")
  })
})
