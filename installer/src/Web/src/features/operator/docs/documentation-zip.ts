export type DocumentationZipEntry = Readonly<{
  path: string
  content: string
  binaryContent?: Uint8Array
}>

const textEncoder = new TextEncoder()
const maximumZipUint16 = 0xffff
const maximumZipUint32 = 0xffffffff

function assertZipPath(path: string) {
  if (!path || path.startsWith("/") || path.includes("\\") || path.split("/").some((segment) => !segment || segment === "." || segment === "..")) {
    throw new Error(`Unsafe documentation ZIP path: ${path}`)
  }
}

function assertZipEntries(entries: readonly DocumentationZipEntry[]) {
  if (entries.length === 0) {
    throw new Error("Documentation ZIP must contain at least one file.")
  }

  if (entries.length > maximumZipUint16) {
    throw new Error("Documentation ZIP contains too many files.")
  }

  const seenPaths = new Set<string>()

  for (const entry of entries) {
    assertZipPath(entry.path)

    if (seenPaths.has(entry.path)) {
      throw new Error(`Documentation ZIP contains a duplicate path: ${entry.path}`)
    }

    seenPaths.add(entry.path)
  }
}

function crc32(bytes: Uint8Array) {
  let value = 0xffffffff

  for (const byte of bytes) {
    value ^= byte

    for (let bit = 0; bit < 8; bit += 1) {
      value = (value >>> 1) ^ (value & 1 ? 0xedb88320 : 0)
    }
  }

  return (value ^ 0xffffffff) >>> 0
}

function toDosDateTime(value: Date) {
  const year = Math.min(2107, Math.max(1980, value.getUTCFullYear()))
  const month = value.getUTCMonth() + 1
  const day = value.getUTCDate()
  const hours = value.getUTCHours()
  const minutes = value.getUTCMinutes()
  const seconds = Math.floor(value.getUTCSeconds() / 2)

  return {
    date: ((year - 1980) << 9) | (month << 5) | day,
    time: (hours << 11) | (minutes << 5) | seconds,
  }
}

function writeUint16(target: Uint8Array, offset: number, value: number) {
  target[offset] = value & 0xff
  target[offset + 1] = (value >>> 8) & 0xff
}

function writeUint32(target: Uint8Array, offset: number, value: number) {
  target[offset] = value & 0xff
  target[offset + 1] = (value >>> 8) & 0xff
  target[offset + 2] = (value >>> 16) & 0xff
  target[offset + 3] = (value >>> 24) & 0xff
}

type EncodedDocumentationZipEntry = Readonly<{
  path: string
  name: Uint8Array
  content: Uint8Array
  crc32: number
}>

/**
 * Builds a standards-compliant ZIP archive using only the uncompressed "store"
 * method. Documentation packs are generated from trusted bundled Markdown and
 * bounded image assets; avoiding a runtime archive dependency keeps the offline
 * reader self-contained while preserving binary images byte-for-byte.
 */
export function createDocumentationZip(
  entries: readonly DocumentationZipEntry[],
  modifiedAt: Date,
): Uint8Array<ArrayBuffer> {
  assertZipEntries(entries)

  const encodedEntries: readonly EncodedDocumentationZipEntry[] = entries.map((entry) => {
    const name = textEncoder.encode(entry.path)
    const content = entry.binaryContent ?? textEncoder.encode(entry.content)

    if (name.byteLength > maximumZipUint16) {
      throw new Error(`Documentation ZIP path is too long: ${entry.path}`)
    }

    if (content.byteLength > maximumZipUint32) {
      throw new Error(`Documentation ZIP file is too large: ${entry.path}`)
    }

    return {
      path: entry.path,
      name,
      content,
      crc32: crc32(content),
    }
  })

  const localFileBytes = encodedEntries.reduce(
    (total, entry) => total + 30 + entry.name.byteLength + entry.content.byteLength,
    0,
  )
  const centralDirectoryBytes = encodedEntries.reduce(
    (total, entry) => total + 46 + entry.name.byteLength,
    0,
  )
  const endOfCentralDirectoryBytes = 22
  const totalBytes = localFileBytes + centralDirectoryBytes + endOfCentralDirectoryBytes

  if (totalBytes > maximumZipUint32) {
    throw new Error("Documentation ZIP is too large for the supported ZIP format.")
  }

  const archive = new Uint8Array(totalBytes)
  const { date, time } = toDosDateTime(modifiedAt)
  const localOffsets: number[] = []
  let cursor = 0

  for (const entry of encodedEntries) {
    localOffsets.push(cursor)
    writeUint32(archive, cursor, 0x04034b50)
    writeUint16(archive, cursor + 4, 20)
    writeUint16(archive, cursor + 6, 0x0800)
    writeUint16(archive, cursor + 8, 0)
    writeUint16(archive, cursor + 10, time)
    writeUint16(archive, cursor + 12, date)
    writeUint32(archive, cursor + 14, entry.crc32)
    writeUint32(archive, cursor + 18, entry.content.byteLength)
    writeUint32(archive, cursor + 22, entry.content.byteLength)
    writeUint16(archive, cursor + 26, entry.name.byteLength)
    writeUint16(archive, cursor + 28, 0)

    cursor += 30
    archive.set(entry.name, cursor)
    cursor += entry.name.byteLength
    archive.set(entry.content, cursor)
    cursor += entry.content.byteLength
  }

  const centralDirectoryOffset = cursor

  for (const [index, entry] of encodedEntries.entries()) {
    writeUint32(archive, cursor, 0x02014b50)
    writeUint16(archive, cursor + 4, 0x0314)
    writeUint16(archive, cursor + 6, 20)
    writeUint16(archive, cursor + 8, 0x0800)
    writeUint16(archive, cursor + 10, 0)
    writeUint16(archive, cursor + 12, time)
    writeUint16(archive, cursor + 14, date)
    writeUint32(archive, cursor + 16, entry.crc32)
    writeUint32(archive, cursor + 20, entry.content.byteLength)
    writeUint32(archive, cursor + 24, entry.content.byteLength)
    writeUint16(archive, cursor + 28, entry.name.byteLength)
    writeUint16(archive, cursor + 30, 0)
    writeUint16(archive, cursor + 32, 0)
    writeUint16(archive, cursor + 34, 0)
    writeUint16(archive, cursor + 36, 0)
    writeUint32(archive, cursor + 38, 0)
    writeUint32(archive, cursor + 42, localOffsets[index])

    cursor += 46
    archive.set(entry.name, cursor)
    cursor += entry.name.byteLength
  }

  const centralDirectorySize = cursor - centralDirectoryOffset

  writeUint32(archive, cursor, 0x06054b50)
  writeUint16(archive, cursor + 4, 0)
  writeUint16(archive, cursor + 6, 0)
  writeUint16(archive, cursor + 8, encodedEntries.length)
  writeUint16(archive, cursor + 10, encodedEntries.length)
  writeUint32(archive, cursor + 12, centralDirectorySize)
  writeUint32(archive, cursor + 16, centralDirectoryOffset)
  writeUint16(archive, cursor + 20, 0)

  return archive
}
