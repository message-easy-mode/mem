import { createHash } from "node:crypto"
import { promises as fs } from "node:fs"
import path from "node:path"
import process from "node:process"
import { fileURLToPath } from "node:url"

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url))
const webRoot = path.resolve(scriptDirectory, "..")
const sourceRoot = path.join(webRoot, "docs")
const outputRoot = path.join(
  webRoot,
  "src/features/operator/docs/release-pack",
)
const generatedDocsRoot = path.join(outputRoot, "docs")
const generatedMetadataRoot = path.join(outputRoot, "metadata")
const sourceAssetsRoot = path.join(sourceRoot, "assets")
const generatedAssetsRoot = path.join(outputRoot, "assets")
const packConfigPath = path.join(sourceRoot, "documentation-pack.json")

const supportedDocumentationAssetMediaTypes = new Map([
  [".png", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".webp", "image/webp"],
])

const requiredFields = [
  "id",
  "translationKey",
  "locale",
  "groupId",
  "groupKey",
  "groupLabel",
  "groupOrder",
  "title",
  "description",
  "order",
  "status",
  "appliesTo",
  "tags",
  "route",
  "aliases",
  "outputPath",
  "preserveLegacyBranding",
]

function fail(message) {
  throw new Error(message)
}

function normalizeNewlines(value) {
  return value.replaceAll("\r\n", "\n").replaceAll("\r", "\n")
}

function toBuffer(value) {
  return Buffer.isBuffer(value) ? value : Buffer.from(value, "utf8")
}

function sha256(value) {
  return createHash("sha256").update(toBuffer(value)).digest("hex")
}

function serializeJson(value) {
  return `${JSON.stringify(value, null, 2)}\n`
}

function parseFrontMatterValue(rawValue, sourcePath, key) {
  const value = rawValue.trim()

  if (value.length === 0) {
    fail(`${sourcePath}: front-matter field '${key}' has no value.`)
  }

  try {
    return JSON.parse(value)
  } catch {
    return value
  }
}

export function parseDocumentationSource(sourcePath, content) {
  const normalized = normalizeNewlines(content).replace(/^\uFEFF/u, "")

  if (!normalized.startsWith("---\n")) {
    fail(`${sourcePath}: documentation source must begin with front matter.`)
  }

  const closingDelimiterIndex = normalized.indexOf("\n---\n", 4)

  if (closingDelimiterIndex < 0) {
    fail(`${sourcePath}: documentation front matter is not closed.`)
  }

  const frontMatterText = normalized.slice(4, closingDelimiterIndex)
  const markdown = normalized.slice(closingDelimiterIndex + 5).replace(/^\n+/u, "")
  const frontMatter = {}

  for (const [lineIndex, line] of frontMatterText.split("\n").entries()) {
    if (line.trim().length === 0 || line.trimStart().startsWith("#")) {
      continue
    }

    const separatorIndex = line.indexOf(":")

    if (separatorIndex < 1) {
      fail(`${sourcePath}:${lineIndex + 2}: expected 'key: value' front matter.`)
    }

    const key = line.slice(0, separatorIndex).trim()

    if (Object.hasOwn(frontMatter, key)) {
      fail(`${sourcePath}: duplicate front-matter field '${key}'.`)
    }

    frontMatter[key] = parseFrontMatterValue(
      line.slice(separatorIndex + 1),
      sourcePath,
      key,
    )
  }

  for (const field of requiredFields) {
    if (!Object.hasOwn(frontMatter, field)) {
      fail(`${sourcePath}: missing required front-matter field '${field}'.`)
    }
  }

  return { frontMatter, markdown, normalized }
}

function assertString(value, sourcePath, field) {
  if (typeof value !== "string" || value.trim().length === 0) {
    fail(`${sourcePath}: '${field}' must be a non-empty string.`)
  }

  return value.trim()
}

function assertInteger(value, sourcePath, field) {
  if (!Number.isSafeInteger(value) || value < 0) {
    fail(`${sourcePath}: '${field}' must be a non-negative integer.`)
  }

  return value
}

function assertStringArray(value, sourcePath, field) {
  if (!Array.isArray(value) || value.some((item) => typeof item !== "string")) {
    fail(`${sourcePath}: '${field}' must be an array of strings.`)
  }

  return value.map((item) => item.trim()).filter(Boolean)
}

function assertBoolean(value, sourcePath, field) {
  if (typeof value !== "boolean") {
    fail(`${sourcePath}: '${field}' must be true or false.`)
  }

  return value
}

function assertSafeRelativePath(value, sourcePath, field, requiredPrefix) {
  const normalized = path.posix.normalize(assertString(value, sourcePath, field))

  if (
    normalized.startsWith("../") ||
    normalized.startsWith("/") ||
    normalized.includes("\\") ||
    normalized === "." ||
    (requiredPrefix && !normalized.startsWith(requiredPrefix))
  ) {
    fail(`${sourcePath}: '${field}' contains an unsafe path: ${value}`)
  }

  return normalized
}

function stripInlineMarkdown(value) {
  return value
    .replace(/!\[([^\]]*)\]\([^)]*\)/gu, "$1")
    .replace(/\[([^\]]+)\]\([^)]*\)/gu, "$1")
    .replace(/<[^>]+>/gu, " ")
    .replace(/[`*_~]/gu, "")
    .replace(/\s+/gu, " ")
    .trim()
}

function slugifyHeading(value, occurrences) {
  const base = stripInlineMarkdown(value)
    .toLocaleLowerCase()
    .normalize("NFKC")
    .replace(/[^\p{L}\p{N}\s-]/gu, "")
    .trim()
    .replace(/\s+/gu, "-")
    .replace(/-+/gu, "-") || "section"
  const occurrence = occurrences.get(base) ?? 0
  occurrences.set(base, occurrence + 1)

  return occurrence === 0 ? base : `${base}-${occurrence}`
}

export function extractHeadings(markdown) {
  const headings = []
  const occurrences = new Map()
  let insideFence = false
  let fenceMarker = ""

  for (const line of normalizeNewlines(markdown).split("\n")) {
    const fenceMatch = line.match(/^\s*(```+|~~~+)/u)

    if (fenceMatch) {
      if (!insideFence) {
        insideFence = true
        fenceMarker = fenceMatch[1][0]
      } else if (fenceMatch[1][0] === fenceMarker) {
        insideFence = false
        fenceMarker = ""
      }
      continue
    }

    if (insideFence) {
      continue
    }

    const headingMatch = line.match(/^(#{1,6})\s+(.+?)\s*#*\s*$/u)

    if (!headingMatch) {
      continue
    }

    const title = stripInlineMarkdown(headingMatch[2])
    headings.push({
      level: headingMatch[1].length,
      title,
      anchor: slugifyHeading(title, occurrences),
    })
  }

  return headings
}

function toSearchBody(markdown) {
  return normalizeNewlines(markdown)
    .replace(/^---\n[\s\S]*?\n---\n/u, "")
    .replace(/```[^\n]*\n([\s\S]*?)```/gu, "$1")
    .replace(/~~~[^\n]*\n([\s\S]*?)~~~/gu, "$1")
    .replace(/!\[([^\]]*)\]\([^)]*\)/gu, "$1")
    .replace(/\[([^\]]+)\]\([^)]*\)/gu, "$1")
    .replace(/\[![A-Z][A-Z _-]*\]/gu, " ")
    .replace(/^#{1,6}\s+/gmu, "")
    .replace(/(^|\s)(?:[-+*]|\d+\.)\s+/gu, "$1")
    .replace(/[|]/gu, " ")
    .replace(/[`*_~]/gu, "")
    .replace(/<[^>]+>/gu, " ")
    .replace(/\s+/gu, " ")
    .trim()
}

async function listFilesRecursively(root) {
  const results = []

  async function visit(directory) {
    for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
      const absolutePath = path.join(directory, entry.name)

      if (entry.isDirectory()) {
        await visit(absolutePath)
      } else if (entry.isFile()) {
        results.push(absolutePath)
      }
    }
  }

  await visit(root)
  return results.sort((left, right) => left.localeCompare(right))
}

async function loadDocumentationAssets() {
  const assets = []

  async function visit(directory) {
    let entries
    try {
      entries = await fs.readdir(directory, { withFileTypes: true })
    } catch (error) {
      if (error?.code === "ENOENT") {
        return
      }
      throw error
    }

    for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name))) {
      const absolutePath = path.join(directory, entry.name)
      const stat = await fs.lstat(absolutePath)

      if (stat.isSymbolicLink()) {
        fail(`Documentation assets may not contain symlinks: ${absolutePath}`)
      }

      if (stat.isDirectory()) {
        await visit(absolutePath)
        continue
      }

      if (!stat.isFile()) {
        fail(`Documentation assets may contain only regular files: ${absolutePath}`)
      }

      const relativePath = path.relative(sourceAssetsRoot, absolutePath).split(path.sep).join("/")

      if (relativePath === "README.md") {
        continue
      }

      const extension = path.extname(relativePath).toLocaleLowerCase()
      const mediaType = supportedDocumentationAssetMediaTypes.get(extension)

      if (!mediaType) {
        fail(
          `docs/assets/${relativePath}: unsupported documentation asset type. ` +
            "Supported types are PNG, JPEG, and WebP.",
        )
      }

      const content = await fs.readFile(absolutePath)
      assets.push({
        sourcePath: `docs/assets/${relativePath}`,
        path: `assets/${relativePath}`,
        mediaType,
        bytes: content.byteLength,
        sha256: sha256(content),
        content,
      })
    }
  }

  await visit(sourceAssetsRoot)
  return assets.sort((left, right) => left.path.localeCompare(right.path))
}

function rewriteDocumentationImageReferences(
  relativeSourcePath,
  outputPath,
  markdown,
  availableAssetPaths,
) {
  const referencedAssets = new Set()
  const result = []
  let insideFence = false
  let fenceMarker = ""

  for (const line of normalizeNewlines(markdown).split("\n")) {
    const fenceMatch = line.match(/^\s*(```+|~~~+)/u)

    if (fenceMatch) {
      if (!insideFence) {
        insideFence = true
        fenceMarker = fenceMatch[1][0]
      } else if (fenceMatch[1][0] === fenceMarker) {
        insideFence = false
        fenceMarker = ""
      }
      result.push(line)
      continue
    }

    if (insideFence) {
      result.push(line)
      continue
    }

    result.push(
      line.replace(/!\[([^\]\n]*)\]\(([^)\n]+)\)/gu, (fullMatch, altText, rawTarget) => {
        const alt = altText.trim()
        const target = rawTarget.trim()

        if (!alt) {
          fail(`docs/${relativeSourcePath}: documentation images require non-empty alt text.`)
        }

        if (/\s/u.test(target)) {
          fail(
            `docs/${relativeSourcePath}: documentation image target '${target}' contains whitespace. ` +
              "Use a simple relative asset path without an inline title.",
          )
        }

        if (/^(?:[a-z][a-z0-9+.-]*:|\/\/|\/|#)/iu.test(target)) {
          fail(
            `docs/${relativeSourcePath}: documentation image '${target}' must be a local relative ` +
              "path under docs/assets so offline packs remain portable.",
          )
        }

        const sourceAssetPath = path.posix.normalize(
          path.posix.join(path.posix.dirname(relativeSourcePath), target),
        )

        if (
          sourceAssetPath.startsWith("../") ||
          sourceAssetPath === "." ||
          !sourceAssetPath.startsWith("assets/")
        ) {
          fail(
            `docs/${relativeSourcePath}: documentation image '${target}' must resolve under docs/assets.`,
          )
        }

        const packAssetPath = sourceAssetPath

        if (!availableAssetPaths.has(packAssetPath)) {
          fail(
            `docs/${relativeSourcePath}: documentation image '${target}' resolves to missing ` +
              `'docs/${sourceAssetPath}'.`,
          )
        }

        const portableTarget = path.posix.relative(
          path.posix.dirname(outputPath),
          packAssetPath,
        )

        referencedAssets.add(packAssetPath)
        return `![${altText}](${portableTarget})`
      }),
    )
  }

  return {
    markdown: result.join("\n"),
    assets: [...referencedAssets].sort((left, right) => left.localeCompare(right)),
  }
}

function expectedLocaleFromSourcePath(relativeSourcePath) {
  if (relativeSourcePath.startsWith("en/")) {
    return "en"
  }

  if (relativeSourcePath.startsWith("de/")) {
    return "de"
  }

  if (relativeSourcePath.startsWith("legacy/")) {
    return undefined
  }

  fail(`Unsupported documentation source location: docs/${relativeSourcePath}`)
}

function normalizeDocument(relativeSourcePath, parsed, availableAssetPaths) {
  const sourcePath = `docs/${relativeSourcePath}`
  const metadata = parsed.frontMatter
  const locale = assertString(metadata.locale, sourcePath, "locale")
  const expectedLocale = expectedLocaleFromSourcePath(relativeSourcePath)

  if (!(["en", "de"].includes(locale))) {
    fail(`${sourcePath}: unsupported locale '${locale}'.`)
  }

  if (expectedLocale && locale !== expectedLocale) {
    fail(`${sourcePath}: locale '${locale}' does not match its source directory.`)
  }

  const outputPath = assertSafeRelativePath(
    metadata.outputPath,
    sourcePath,
    "outputPath",
    "docs/",
  )
  const projectedImages = rewriteDocumentationImageReferences(
    relativeSourcePath,
    outputPath,
    parsed.markdown,
    availableAssetPaths,
  )
  const portableMarkdown = projectedImages.markdown.endsWith("\n")
    ? projectedImages.markdown
    : `${projectedImages.markdown}\n`
  const portableContent = [
    "---",
    `title: ${assertString(metadata.title, sourcePath, "title")}`,
    `description: ${assertString(metadata.description, sourcePath, "description")}`,
    `section: ${assertString(metadata.groupLabel, sourcePath, "groupLabel")}`,
    `order: ${assertInteger(metadata.order, sourcePath, "order")}`,
    "---",
    "",
    portableMarkdown,
  ].join("\n")

  const document = {
    id: assertString(metadata.id, sourcePath, "id"),
    translationKey: assertString(metadata.translationKey, sourcePath, "translationKey"),
    locale,
    groupId: assertString(metadata.groupId, sourcePath, "groupId"),
    groupKey: assertString(metadata.groupKey, sourcePath, "groupKey"),
    groupLabel: assertString(metadata.groupLabel, sourcePath, "groupLabel"),
    groupOrder: assertInteger(metadata.groupOrder, sourcePath, "groupOrder"),
    title: assertString(metadata.title, sourcePath, "title"),
    description: assertString(metadata.description, sourcePath, "description"),
    order: assertInteger(metadata.order, sourcePath, "order"),
    status: assertString(metadata.status, sourcePath, "status"),
    appliesTo: assertStringArray(metadata.appliesTo, sourcePath, "appliesTo"),
    tags: assertStringArray(metadata.tags, sourcePath, "tags"),
    route: assertString(metadata.route, sourcePath, "route"),
    aliases: assertStringArray(metadata.aliases, sourcePath, "aliases"),
    outputPath,
    preserveLegacyBranding: assertBoolean(
      metadata.preserveLegacyBranding,
      sourcePath,
      "preserveLegacyBranding",
    ),
    sourcePath,
    sourceContent: parsed.normalized.endsWith("\n") ? parsed.normalized : `${parsed.normalized}\n`,
    content: portableContent,
    markdown: projectedImages.markdown,
    assets: projectedImages.assets,
  }

  if (!document.route.startsWith("/docs")) {
    fail(`${sourcePath}: route must start with '/docs'.`)
  }

  if (document.aliases.some((alias) => !alias.startsWith("/docs"))) {
    fail(`${sourcePath}: every alias must start with '/docs'.`)
  }

  const headings = extractHeadings(document.markdown)

  if (headings.length === 0 || headings[0].level !== 1) {
    fail(`${sourcePath}: document must contain a level-one heading.`)
  }

  if (headings[0].title !== document.title) {
    fail(
      `${sourcePath}: first heading '${headings[0].title}' does not match title '${document.title}'.`,
    )
  }

  return { ...document, headings }
}

function validateDocuments(documents, config) {
  const ids = new Map()
  const outputs = new Map()
  const routes = new Map()
  const translations = new Map()
  const groups = new Map()

  for (const document of documents) {
    const isLegacy = document.sourcePath.startsWith("docs/legacy/")

    if (isLegacy) {
      if (document.status !== "legacy") {
        fail(`${document.sourcePath}: legacy documentation must use status 'legacy'.`)
      }
      if (!document.preserveLegacyBranding) {
        fail(`${document.sourcePath}: legacy documentation must preserve legacy branding.`)
      }
    } else {
      if (!document.appliesTo.includes(config.currentProductLine)) {
        fail(`${document.sourcePath}: current documentation must apply to '${config.currentProductLine}'.`)
      }
      if (document.status === "review-required" || document.status === "legacy") {
        fail(`${document.sourcePath}: current documentation cannot ship with status '${document.status}'.`)
      }
      if (document.preserveLegacyBranding) {
        fail(`${document.sourcePath}: current documentation cannot preserve legacy branding.`)
      }

      const legacyRuntimeCommandPatterns = [
        /\b(?:sudo\s+)?docker\s+logs\b[^\n]*\bmem-(?:api|web)\b/iu,
        /\b(?:sudo\s+)?docker\s+(?:exec|inspect|restart|start|stop|rm)\b[^\n]*\bmem-(?:api|web)\b/iu,
      ]

      if (legacyRuntimeCommandPatterns.some((pattern) => pattern.test(document.markdown))) {
        fail(
          `${document.sourcePath}: current documentation cannot operationalize the retired mem-api/mem-web containers.`,
        )
      }
    }

    if (ids.has(document.id)) {
      fail(`Duplicate document id '${document.id}' in ${ids.get(document.id)} and ${document.sourcePath}.`)
    }
    ids.set(document.id, document.sourcePath)

    if (outputs.has(document.outputPath)) {
      fail(
        `Duplicate output path '${document.outputPath}' in ${outputs.get(document.outputPath)} and ${document.sourcePath}.`,
      )
    }
    outputs.set(document.outputPath, document.sourcePath)

    for (const route of [document.route, ...document.aliases]) {
      if (routes.has(route)) {
        fail(`Duplicate route or alias '${route}' in ${routes.get(route)} and ${document.sourcePath}.`)
      }
      routes.set(route, document.sourcePath)
    }

    const translationIdentity = `${document.translationKey}:${document.locale}`
    if (translations.has(translationIdentity)) {
      fail(
        `Duplicate ${document.locale} translation for '${document.translationKey}' in ${translations.get(translationIdentity)} and ${document.sourcePath}.`,
      )
    }
    translations.set(translationIdentity, document.sourcePath)

    const groupIdentity = `${document.groupId}:${document.locale}`
    const existingGroup = groups.get(groupIdentity)
    const currentGroup = {
      groupKey: document.groupKey,
      groupLabel: document.groupLabel,
      groupOrder: document.groupOrder,
    }

    if (existingGroup && JSON.stringify(existingGroup) !== JSON.stringify(currentGroup)) {
      fail(`${document.sourcePath}: inconsistent metadata for group '${document.groupId}'.`)
    }
    groups.set(groupIdentity, currentGroup)
  }

  const outputPaths = new Set(documents.map((document) => document.outputPath))

  for (const document of documents) {
    const withoutFences = document.markdown.replace(/```[\s\S]*?```|~~~[\s\S]*?~~~/gu, "")
    const localLinks = [...withoutFences.matchAll(/(?<!!)\[[^\]]+\]\(([^)]+)\)/gu)]

    for (const match of localLinks) {
      const rawTarget = match[1].trim()
      const targetWithoutAnchor = rawTarget.split("#", 1)[0]

      if (
        targetWithoutAnchor.length === 0 ||
        /^(?:https?:|mailto:|tel:|data:)/iu.test(targetWithoutAnchor) ||
        targetWithoutAnchor.startsWith("/")
      ) {
        continue
      }

      const resolved = path.posix.normalize(
        path.posix.join(path.posix.dirname(document.outputPath), targetWithoutAnchor),
      )

      if (!outputPaths.has(resolved)) {
        fail(`${document.sourcePath}: local link '${rawTarget}' resolves to missing '${resolved}'.`)
      }
    }
  }
}

async function loadCanonicalDocuments(config, assets) {
  const sourceDirectories = ["en", "de", "legacy"].map((directory) =>
    path.join(sourceRoot, directory),
  )
  const sourceFiles = []

  for (const directory of sourceDirectories) {
    sourceFiles.push(
      ...(await listFilesRecursively(directory)).filter((file) => file.endsWith(".md")),
    )
  }

  const documents = []
  const availableAssetPaths = new Set(assets.map((asset) => asset.path))

  for (const absolutePath of sourceFiles.sort((left, right) => left.localeCompare(right))) {
    const relativeSourcePath = path.relative(sourceRoot, absolutePath).split(path.sep).join("/")
    const content = await fs.readFile(absolutePath, "utf8")
    documents.push(
      normalizeDocument(
        relativeSourcePath,
        parseDocumentationSource(`docs/${relativeSourcePath}`, content),
        availableAssetPaths,
      ),
    )
  }

  validateDocuments(documents, config)

  const referencedAssetPaths = new Set(documents.flatMap((document) => document.assets))
  for (const asset of assets) {
    if (!referencedAssetPaths.has(asset.path)) {
      fail(`${asset.sourcePath}: documentation asset is not referenced by any document.`)
    }
  }

  return documents
}

function buildManifest(config, documents, assets) {
  return {
    schemaVersion: 2,
    generatedAtUtc: config.generatedAtUtc,
    generatorVersion: config.generatorVersion,
    sourceContentVersion: config.sourceContentVersion,
    source: {
      root: "docs",
      format: "markdown-frontmatter",
      config: "docs/documentation-pack.json",
    },
    status: {
      reviewRequired: config.reviewRequired,
      reason: config.reviewReason,
    },
    ...(assets.length > 0
      ? {
          assets: assets.map((asset) => ({
            path: asset.path,
            mediaType: asset.mediaType,
            bytes: asset.bytes,
            sha256: asset.sha256,
          })),
        }
      : {}),
    documents: documents.map((document) => {
      const bytes = Buffer.byteLength(document.content, "utf8")
      const sourceChecksum = sha256(document.sourceContent)
      const checksum = sha256(document.content)

      return {
        id: document.id,
        translationKey: document.translationKey,
        locale: document.locale,
        title: document.title,
        description: document.description,
        section: document.groupLabel,
        groupId: document.groupId,
        groupKey: document.groupKey,
        order: document.order,
        status: document.status,
        appliesTo: document.appliesTo,
        tags: document.tags,
        route: document.route,
        aliases: document.aliases,
        sourcePath: document.sourcePath,
        sourceFormat: "markdown-frontmatter",
        path: document.outputPath,
        sourceSha256: sourceChecksum,
        sha256: checksum,
        headings: document.headings,
        bytes,
        preserveLegacyBranding: document.preserveLegacyBranding,
        ...(document.assets.length > 0 ? { assets: document.assets } : {}),
      }
    }),
  }
}

function buildNavigation(config, documents) {
  const groups = new Map()

  for (const document of documents) {
    const group = groups.get(document.groupId) ?? {
      id: document.groupId,
      key: document.groupKey,
      locale: document.locale,
      title: document.groupLabel,
      order: document.groupOrder,
      items: [],
    }

    group.items.push({
      id: document.id,
      translationKey: document.translationKey,
      locale: document.locale,
      title: document.title,
      description: document.description,
      path: document.outputPath,
      route: document.route,
      aliases: document.aliases,
      order: document.order,
      status: document.status,
    })
    groups.set(document.groupId, group)
  }

  return {
    schemaVersion: 2,
    generatedAtUtc: config.generatedAtUtc,
    source: "docs",
    groups: [...groups.values()]
      .sort((left, right) => left.order - right.order || left.id.localeCompare(right.id))
      .map((group) => ({
        ...group,
        items: group.items.sort(
          (left, right) => left.order - right.order || left.id.localeCompare(right.id),
        ),
      })),
  }
}

function buildSearchIndex(config, documents) {
  return {
    schemaVersion: 2,
    generatedAtUtc: config.generatedAtUtc,
    source: "docs",
    documents: documents.map((document) => ({
      id: document.id,
      translationKey: document.translationKey,
      locale: document.locale,
      title: document.title,
      description: document.description,
      section: document.groupLabel,
      groupKey: document.groupKey,
      tags: document.tags,
      path: document.outputPath,
      route: document.route,
      aliases: document.aliases,
      headings: document.headings,
      body: toSearchBody(document.markdown),
    })),
  }
}


function buildPackReadme(config, documents) {
  const currentDocuments = documents.filter((document) => !document.sourcePath.startsWith("docs/legacy/"))
  const legacyDocuments = documents.filter((document) => document.sourcePath.startsWith("docs/legacy/"))
  const englishCount = currentDocuments.filter((document) => document.locale === "en").length
  const germanCount = currentDocuments.filter((document) => document.locale === "de").length

  return `# MEM Offline Documentation Pack — ${config.sourceContentVersion}\n\nThis package contains the documentation bundled with the MEM Control Plane. The current non-legacy documentation is reviewed for ${config.currentProductLine}; historical MatrixEasyMode material is packaged only as explicitly legacy content.\n\n## Contents\n\n- \`docs/\` — portable Markdown used by the in-product reader.\n- \`metadata/manifest.json\` — document identity, status, version applicability, routes and hashes.\n- \`metadata/navigation.json\` — sidebar/navigation model.\n- \`metadata/search-index.json\` — offline search records.\n- \`MEM-KNOWLEDGE-BASE.md\` — one-file concatenation for offline/AI-assisted reading.\n- \`AI-USAGE.md\` — safe-use guidance for AI-assisted documentation work.\n- \`CONVERSION_REPORT.md\` — preserved historical conversion notes.\n- \`SHA256SUMS\` — checksums for the complete pack.\n\n## Current status\n\n- Source content version: **${config.sourceContentVersion}**\n- Current product line: **${config.currentProductLine}**\n- Current English documents: **${englishCount}**\n- Current German documents: **${germanCount}**\n- Explicit legacy documents: **${legacyDocuments.length}**\n- Review required: **${config.reviewRequired ? "yes" : "no"}**\n\n${config.reviewReason}\n\n## Operator rule\n\nPrefer the exact installed release identity, current Diagnostics/runtime evidence, and current supported pages over historical commands. Never use a legacy Compose or \`stack.sh\` procedure as a MEM 0.2.x instruction unless a release-specific migration guide explicitly requires it.\n\n## Rendering\n\nPortable pages require CommonMark-style Markdown plus GitHub-style alerts. Raw HTML should remain disabled and local assets/links should be treated as documentation content, not executable configuration. Image references are rewritten to portable relative paths so extracted Markdown continues to render its bundled images offline.\n`
}

function buildAiUsage(config) {
  return `# Using this MEM documentation pack with AI\n\nThis pack is generic product documentation for MEM ${config.sourceContentVersion}. It is not a live configuration export.\n\n## Recommended input\n\nUse \`MEM-KNOWLEDGE-BASE.md\` for broad ingestion or selected files under \`docs/\` for a narrower question.\n\n## Grounding rules\n\n- Treat non-legacy pages as current guidance for ${config.currentProductLine}.\n- Treat pages marked \`legacy\` as historical context only.\n- Confirm commands against the installed MEM version, current UI, runtime evidence, and release-specific notes before production mutation.\n- Prefer server-owned Diagnostics, operation IDs, support reports, and safe CLI JSON over guesses from container names or ports.\n\n## Never request or upload secrets\n\nDo not ask users to upload passwords, TOTP secrets, recovery codes, bearer credentials, setup tokens, TURN shared secrets, signing keys, certificate private keys, Secret Service output, raw database dumps, unrestricted environment/configuration files, or backup payload contents.\n\n## Production path context\n\nCurrent production documentation distinguishes private Control Plane state under \`/data\`, host-visible MEM data under \`/var/lib/message-easy-mode\`, and installed software under \`/opt/mem\`. Do not recommend developer-home paths as production fixes.\n\n## Machine contracts\n\nCommand names, flags, environment variables, JSON fields, status values, and error codes remain English machine contracts even when human-facing documentation is German.\n`
}

function buildKnowledgeBase(documents) {
  const parts = [
    "# MEM — Offline Documentation Knowledge Base",
    "",
    "This file concatenates the portable Markdown documents bundled into MEM. Current pages are written for MEM 0.2.x; explicitly legacy pages are historical reference only. Do not combine this generic documentation with credentials, private keys, recovery codes, raw database dumps, backup payloads, or unrestricted host-specific logs.",
  ]

  for (const document of documents) {
    parts.push(
      "",
      "---",
      "",
      `# ${document.title}`,
      "",
      `Source: \`${document.outputPath}\``,
      `Locale: ${document.locale}`,
      `Section: ${document.groupLabel}`,
      `Status: ${document.status}`,
      `Applies to: ${document.appliesTo.join(", ")}`,
      "",
      document.markdown.trim(),
    )
  }

  return `${parts.join("\n")}\n`
}

function buildSha256Sums(files) {
  return [...files.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([relativePath, content]) => `${sha256(content)}  ${relativePath}`)
    .join("\n") + "\n"
}

async function buildExpectedOutputs() {
  const config = JSON.parse(await fs.readFile(packConfigPath, "utf8"))

  for (const field of [
    "sourceContentVersion",
    "generatedAtUtc",
    "reviewReason",
    "generatorVersion",
    "currentProductLine",
  ]) {
    if (typeof config[field] !== "string" || config[field].trim().length === 0) {
      fail(`docs/documentation-pack.json: '${field}' must be a non-empty string.`)
    }
  }

  if (typeof config.reviewRequired !== "boolean") {
    fail("docs/documentation-pack.json: 'reviewRequired' must be true or false.")
  }

  const assets = await loadDocumentationAssets()
  const documents = (await loadCanonicalDocuments(config, assets)).sort(
    (left, right) => left.order - right.order || left.id.localeCompare(right.id),
  )
  const outputs = new Map()

  for (const asset of assets) {
    outputs.set(asset.path, asset.content)
  }

  for (const document of documents) {
    outputs.set(document.outputPath, document.content)
  }

  outputs.set("metadata/manifest.json", serializeJson(buildManifest(config, documents, assets)))
  outputs.set("metadata/navigation.json", serializeJson(buildNavigation(config, documents)))
  outputs.set("metadata/search-index.json", serializeJson(buildSearchIndex(config, documents)))
  outputs.set("README.md", buildPackReadme(config, documents))
  outputs.set("AI-USAGE.md", buildAiUsage(config))
  outputs.set("MEM-KNOWLEDGE-BASE.md", buildKnowledgeBase(documents))

  const conversionReport = await fs.readFile(path.join(outputRoot, "CONVERSION_REPORT.md"), "utf8")
  const checksummedFiles = new Map(outputs)
  checksummedFiles.set("CONVERSION_REPORT.md", normalizeNewlines(conversionReport))
  outputs.set("SHA256SUMS", buildSha256Sums(checksummedFiles))

  return { config, documents, assets, outputs }
}

async function listGeneratedOutputPaths() {
  const generatedPaths = []

  for (const absolutePath of await listFilesRecursively(generatedDocsRoot)) {
    if (absolutePath.endsWith(".md")) {
      generatedPaths.push(path.relative(outputRoot, absolutePath).split(path.sep).join("/"))
    }
  }

  try {
    for (const absolutePath of await listFilesRecursively(generatedAssetsRoot)) {
      generatedPaths.push(path.relative(outputRoot, absolutePath).split(path.sep).join("/"))
    }
  } catch (error) {
    if (error?.code !== "ENOENT") {
      throw error
    }
  }

  for (const name of ["manifest.json", "navigation.json", "search-index.json"]) {
    generatedPaths.push(`metadata/${name}`)
  }

  for (const name of ["README.md", "AI-USAGE.md", "MEM-KNOWLEDGE-BASE.md", "SHA256SUMS"]) {
    generatedPaths.push(name)
  }

  return generatedPaths.sort((left, right) => left.localeCompare(right))
}

async function writeOutputs(outputs) {
  const expectedPaths = new Set(outputs.keys())

  for (const relativePath of await listGeneratedOutputPaths()) {
    if (
      !expectedPaths.has(relativePath) &&
      (relativePath.startsWith("docs/") || relativePath.startsWith("assets/"))
    ) {
      await fs.rm(path.join(outputRoot, relativePath), { force: true })
    }
  }

  for (const [relativePath, content] of outputs) {
    const destination = path.join(outputRoot, relativePath)
    await fs.mkdir(path.dirname(destination), { recursive: true })
    await fs.writeFile(destination, content)
  }
}

async function checkOutputs(outputs) {
  const differences = []
  const expectedPaths = new Set(outputs.keys())

  for (const [relativePath, expectedContent] of outputs) {
    const destination = path.join(outputRoot, relativePath)
    let actualContent

    try {
      actualContent = await fs.readFile(destination)
    } catch (error) {
      if (error?.code === "ENOENT") {
        differences.push(`${relativePath}: missing`)
        continue
      }
      throw error
    }

    if (!actualContent.equals(toBuffer(expectedContent))) {
      differences.push(`${relativePath}: generated content is out of date`)
    }
  }

  for (const relativePath of await listGeneratedOutputPaths()) {
    if (
      !expectedPaths.has(relativePath) &&
      (relativePath.startsWith("docs/") || relativePath.startsWith("assets/"))
    ) {
      differences.push(`${relativePath}: stale generated documentation file`)
    }
  }

  if (differences.length > 0) {
    fail(
      `Documentation pack is not synchronized:\n${differences.map((item) => `- ${item}`).join("\n")}\nRun 'npm run docs:build'.`,
    )
  }
}

async function main() {
  const mode = process.argv.includes("--write")
    ? "write"
    : process.argv.includes("--check")
      ? "check"
      : undefined

  if (!mode || process.argv.includes("--write") && process.argv.includes("--check")) {
    fail("Usage: node scripts/build-documentation-pack.mjs --write|--check")
  }

  const { config, documents, assets, outputs } = await buildExpectedOutputs()

  if (mode === "write") {
    await writeOutputs(outputs)
    console.log(
      `Generated ${documents.length} documentation documents, ${assets.length} assets, and 3 metadata files for source content v${config.sourceContentVersion}.`,
    )
  } else {
    await checkOutputs(outputs)
    console.log(
      `Documentation pack is synchronized: ${documents.length} documents, ${assets.length} assets, source content v${config.sourceContentVersion}.`,
    )
  }
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : error)
  process.exitCode = 1
})
