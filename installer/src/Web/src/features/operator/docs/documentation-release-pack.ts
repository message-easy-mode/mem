import manifestJson from "./release-pack/metadata/manifest.json?raw"
import navigationJson from "./release-pack/metadata/navigation.json?raw"

import {
  documentationProductIdentity,
  normalizeDocumentationBrandingMarkdown,
  normalizeDocumentationBrandingText,
  preservesLegacyDocumentationBranding,
} from "@/features/operator/docs/documentation-branding"

import type {
  DocumentationDocument,
  DocumentationHeading,
  DocumentationLocale,
  DocumentationLanguageReleasePack,
  DocumentationNavigationGroup,
  DocumentationReadingContext,
  DocumentationReleasePack,
  DocumentationReleasePackFile,
  DocumentationView,
} from "@/features/operator/docs/docs.types"

type ImportedHeading = Readonly<{
  level: number
  title: string
  anchor: string
}>

type ImportedAsset = Readonly<{
  path: string
  mediaType: string
  bytes: number
  sha256: string
}>

type ImportedDocument = Readonly<{
  id: string
  translationKey: string
  locale: DocumentationLocale
  title: string
  description: string
  section: string
  order: number
  tags: readonly string[]
  path: string
  headings: readonly ImportedHeading[]
  sourceSha256: string
  sha256: string
  bytes: number
  assets?: readonly string[]
}>

type ImportedManifest = Readonly<{
  schemaVersion: number
  sourceContentVersion: string
  generatedAtUtc: string
  status: Readonly<{
    reviewRequired: boolean
    reason: string
  }>
  assets?: readonly ImportedAsset[]
  documents: readonly ImportedDocument[]
}>

type ImportedNavigationItem = Readonly<{
  id: string
  title: string
  description: string
}>

type ImportedNavigationGroup = Readonly<{
  id: string
  key: string
  locale: DocumentationLocale
  title: string
  items: readonly ImportedNavigationItem[]
}>

type ImportedNavigation = Readonly<{
  groups: readonly ImportedNavigationGroup[]
}>

type ImportedSearchPackDocument = Readonly<{
  id: string
  title: string
  description: string
  section: string
  tags: readonly string[]
  path: string
  route: string
  aliases: readonly string[]
  headings: readonly ImportedHeading[]
  body: string
}>

export type DocumentationRouteResolution =
  | Readonly<{
      kind: "document"
      document: DocumentationDocument
    }>
  | Readonly<{
      kind: "redirect"
      documentKey?: string
      reason: "legacy-alias" | "translation-unavailable" | "not-found"
    }>

const importedManifest = JSON.parse(manifestJson) as ImportedManifest
const importedNavigation = JSON.parse(navigationJson) as ImportedNavigation

const rawMarkdownByModulePath = import.meta.glob("./release-pack/docs/**/*.md", {
  eager: true,
  query: "?raw",
  import: "default",
}) as Readonly<Record<string, string>>

const documentationAssetUrlByModulePath = import.meta.glob(
  "./release-pack/assets/**/*.{png,jpg,jpeg,webp}",
  {
    eager: true,
    query: "?inline",
    import: "default",
  },
) as Readonly<Record<string, string>>

const documentationAssetUrlByPackPath = new Map(
  Object.entries(documentationAssetUrlByModulePath).map(([modulePath, url]) => [
    modulePath.replace(/^\.\/release-pack\//u, ""),
    url,
  ]),
)

for (const asset of importedManifest.assets ?? []) {
  if (!documentationAssetUrlByPackPath.has(asset.path)) {
    throw new Error(`Documentation pack is missing the declared asset: ${asset.path}`)
  }
}

const documentationReleaseVersion = importedManifest.sourceContentVersion

const localizedPackMetadataFileCount = 4

function serializeDocumentationJson(value: unknown) {
  return `${JSON.stringify(value, null, 2)}\n`
}

function createDocumentationPackReadme(
  language: DocumentationLocale,
  documentCount: number,
  assetCount: number,
) {
  return language === "de"
    ? `# MEM (Message Easy Mode) Offline-Dokumentation\n\n**Quellinhaltsversion:** v${documentationReleaseVersion}\n\nDieses ZIP enthält die ${documentCount} deutschsprachigen Dokumente, die im lokalen MEM-Dokumentationsleser für die ausgewählte Sprache verfügbar sind.\n\nEnthalten sind portables Markdown, ${assetCount} referenzierte Bilddateien sowie sprachbezogene Manifest-, Navigations- und Suchmetadaten. Die Markdown-Dokumente verwenden relative Bildpfade, damit Screenshots und Diagramme nach dem Entpacken lokal funktionieren. Dokumente in anderen Sprachen sind absichtlich nicht enthalten. Historische Produktnamen bleiben nur dort erhalten, wo sie eine frühere veröffentlichte Version korrekt bezeichnen.\n`
    : `# MEM (Message Easy Mode) Offline Documentation\n\n**Source content version:** v${documentationReleaseVersion}\n\nThis ZIP contains the ${documentCount} English-language documents available in the local MEM documentation reader for the selected language.\n\nIt includes portable Markdown, ${assetCount} referenced image assets, plus language-scoped manifest, navigation, and search metadata. Markdown documents use relative image paths so screenshots and diagrams continue to work after the ZIP is extracted locally. Documents in other languages are intentionally excluded. Historical product names are retained only where they accurately identify an earlier released version.\n`
}

const canonicalDocumentKeyByImportedId: Readonly<Record<string, string>> =
  Object.fromEntries(
    importedManifest.documents.map((document) => [document.id, document.translationKey]),
  )

const canonicalGroupKeyByImportedId: Readonly<Record<string, string>> =
  Object.fromEntries(importedNavigation.groups.map((group) => [group.id, group.key]))

const defaultDocumentKeyByLanguage: Readonly<Record<DocumentationLocale, string>> = {
  en: "start/welcome",
  de: "start/welcome",
}

function stripLeadingFrontMatter(markdown: string) {
  const normalized = markdown.replace(/^\uFEFF/, "").replaceAll("\r\n", "\n")

  if (!normalized.startsWith("---\n")) {
    return normalized
  }

  const closingDelimiterIndex = normalized.indexOf("\n---\n", 4)

  if (closingDelimiterIndex < 0) {
    return normalized
  }

  return normalized.slice(closingDelimiterIndex + "\n---\n".length).replace(/^\n+/, "")
}

function stripLeadingDocumentTitle(markdown: string, title: string) {
  const expectedTitle = `# ${title}`

  if (!markdown.startsWith(expectedTitle)) {
    return markdown
  }

  const afterTitle = markdown.slice(expectedTitle.length)

  if (afterTitle.length > 0 && !afterTitle.startsWith("\n")) {
    return markdown
  }

  return afterTitle.replace(/^\n+/, "")
}

function loadMarkdown(document: ImportedDocument) {
  const modulePath = `./release-pack/${document.path}`
  const rawMarkdown = rawMarkdownByModulePath[modulePath]

  if (typeof rawMarkdown !== "string") {
    throw new Error(`Documentation pack is missing the declared document: ${document.path}`)
  }

  return stripLeadingDocumentTitle(stripLeadingFrontMatter(rawMarkdown), document.title)
}

function estimateReadMinutes(markdown: string) {
  const proseOnly = markdown.replace(/```[\s\S]*?```/g, " ")
  const wordCount = proseOnly.match(/[\p{L}\p{N}][\p{L}\p{N}'’-]*/gu)?.length ?? 0

  return Math.max(1, Math.ceil(wordCount / 220))
}

function toDocumentationHeadings(headings: readonly ImportedHeading[]): readonly DocumentationHeading[] {
  return headings
    .filter(
      (heading): heading is ImportedHeading & { level: 2 | 3 } =>
        heading.level === 2 || heading.level === 3,
    )
    .map((heading) => ({
      id: heading.anchor,
      label: heading.title,
      level: heading.level,
    }))
}

function inferDocumentationLocale(document: ImportedDocument): DocumentationLocale {
  return document.locale
}

function inferNavigationLocale(group: ImportedNavigationGroup): DocumentationLocale {
  return group.locale
}

function toCanonicalDocumentKey(documentId: string) {
  return canonicalDocumentKeyByImportedId[documentId] ?? documentId
}

function toCanonicalGroupKey(groupId: string) {
  return canonicalGroupKeyByImportedId[groupId] ?? groupId
}

const groupIdByDocumentId = new Map(
  importedNavigation.groups.flatMap((group) =>
    group.items.map((item) => [item.id, group.id] as const),
  ),
)

export const documentationReleasePack = {
  productName: documentationProductIdentity.shortName,
  productFullName: documentationProductIdentity.fullName,
  controlPlaneName: documentationProductIdentity.controlPlaneName,
  packName: documentationProductIdentity.packName,
  brandingProjectionVersion: documentationProductIdentity.brandingProjectionVersion,
  schemaVersion: importedManifest.schemaVersion,
  version: documentationReleaseVersion,
  generatedAtUtc: importedManifest.generatedAtUtc,
  documentCount: importedManifest.documents.length,
  downloadFileCount:
    importedManifest.documents.length +
    (importedManifest.assets?.length ?? 0) +
    localizedPackMetadataFileCount,
  downloadName: `mem-docs-v${documentationReleaseVersion}.zip`,
  reviewRequired: importedManifest.status.reviewRequired,
  reviewReason: importedManifest.status.reason,
} as const satisfies DocumentationReleasePack

export const documentationNavigationGroups = importedNavigation.groups.map((group) => {
  const locale = inferNavigationLocale(group)

  return {
    id: group.id,
    key: toCanonicalGroupKey(group.id),
    locale,
    label: group.title,
  }
}) satisfies readonly DocumentationNavigationGroup[]

export const documentationDocuments = importedManifest.documents
  .slice()
  .sort((left, right) => left.order - right.order)
  .map((document) => {
    const groupId = groupIdByDocumentId.get(document.id)

    if (!groupId) {
      throw new Error(`Documentation pack navigation is missing document: ${document.id}`)
    }

    const key = toCanonicalDocumentKey(document.id)
    const preserveLegacyBranding = preservesLegacyDocumentationBranding(key)
    const markdown = normalizeDocumentationBrandingMarkdown(
      loadMarkdown(document),
      preserveLegacyBranding,
    )

    return {
      id: document.id,
      key,
      locale: inferDocumentationLocale(document),
      groupId,
      groupKey: toCanonicalGroupKey(groupId),
      title: normalizeDocumentationBrandingText(document.title, preserveLegacyBranding),
      summary: normalizeDocumentationBrandingText(document.description, preserveLegacyBranding),
      tags: document.tags.map((tag) =>
        normalizeDocumentationBrandingText(tag, preserveLegacyBranding),
      ),
      estimatedReadMinutes: estimateReadMinutes(markdown),
      headings: toDocumentationHeadings(document.headings).map((heading) => ({
        ...heading,
        label: normalizeDocumentationBrandingText(heading.label, preserveLegacyBranding),
      })),
      markdown,
      sourcePath: document.path,
    }
  }) satisfies readonly DocumentationDocument[]

const documentationDocumentById = new Map(
  documentationDocuments.map((document) => [document.id, document] as const),
)

const documentationDocumentBySourcePath = new Map(
  documentationDocuments.map((document) => [document.sourcePath, document] as const),
)

const documentationVariantsByKey = new Map<string, Map<DocumentationLocale, DocumentationDocument>>()

for (const document of documentationDocuments) {
  const variants = documentationVariantsByKey.get(document.key) ?? new Map()

  if (variants.has(document.locale)) {
    throw new Error(
      `Documentation pack contains duplicate ${document.locale} variants for: ${document.key}`,
    )
  }

  variants.set(document.locale, document)
  documentationVariantsByKey.set(document.key, variants)
}

function createDocumentationView(language: DocumentationLocale): DocumentationView {
  const documents = documentationDocuments.filter((document) => document.locale === language)
  const availableGroupIds = new Set(documents.map((document) => document.groupId))
  const navigationGroups = documentationNavigationGroups
    .filter((group) => group.locale === language && availableGroupIds.has(group.id))
    .map((group) => ({
      ...group,
      label: language === "de" ? group.label.replace(/\s*\(Deutsch\)\s*$/u, "") : group.label,
    }))
  const documentByKey = new Map(documents.map((document) => [document.key, document] as const))
  const requestedDefaultDocument = documentByKey.get(defaultDocumentKeyByLanguage[language])
  const defaultDocument = requestedDefaultDocument ?? documents[0]

  if (!defaultDocument) {
    throw new Error(`Documentation pack does not contain any ${language} documents.`)
  }

  return {
    language,
    navigationGroups,
    documents,
    defaultDocument,
    documentByKey,
  }
}

const documentationViewByLanguage: Readonly<Record<DocumentationLocale, DocumentationView>> = {
  en: createDocumentationView("en"),
  de: createDocumentationView("de"),
}

function getDocumentationAssetPathsForLanguage(language: DocumentationLocale) {
  const documentIds = new Set(
    documentationViewByLanguage[language].documents.map((document) => document.id),
  )

  return [...new Set(
    importedManifest.documents
      .filter((document) => documentIds.has(document.id))
      .flatMap((document) => document.assets ?? []),
  )].sort((left, right) => left.localeCompare(right))
}

const localizedReleasePackByLanguage: Readonly<
  Record<DocumentationLocale, DocumentationLanguageReleasePack>
> = {
  en: {
    ...documentationReleasePack,
    language: "en",
    documentCount: documentationViewByLanguage.en.documents.length,
    downloadFileCount:
      documentationViewByLanguage.en.documents.length +
      getDocumentationAssetPathsForLanguage("en").length +
      localizedPackMetadataFileCount,
    downloadName: `mem-docs-v${documentationReleaseVersion}-en.zip`,
  },
  de: {
    ...documentationReleasePack,
    language: "de",
    documentCount: documentationViewByLanguage.de.documents.length,
    downloadFileCount:
      documentationViewByLanguage.de.documents.length +
      getDocumentationAssetPathsForLanguage("de").length +
      localizedPackMetadataFileCount,
    downloadName: `mem-docs-v${documentationReleaseVersion}-de.zip`,
  },
}

export function getDocumentationReleasePack(language: DocumentationLocale) {
  return localizedReleasePackByLanguage[language]
}

function createLocalizedManifest(language: DocumentationLocale) {
  const view = getDocumentationView(language)
  const documentIds = new Set(view.documents.map((document) => document.id))

  const assetPaths = new Set(getDocumentationAssetPathsForLanguage(language))

  return {
    ...importedManifest,
    language,
    packVersion: documentationReleaseVersion,
    product: {
      shortName: documentationProductIdentity.shortName,
      fullName: documentationProductIdentity.fullName,
      controlPlaneName: documentationProductIdentity.controlPlaneName,
    },
    brandingProjectionVersion: documentationProductIdentity.brandingProjectionVersion,
    assets: (importedManifest.assets ?? []).filter((asset) => assetPaths.has(asset.path)),
    documents: importedManifest.documents
      .filter((document) => documentIds.has(document.id))
      .map((document) => {
        const key = toCanonicalDocumentKey(document.id)
        const preserveLegacyBranding = preservesLegacyDocumentationBranding(key)
        const { sha256, bytes, ...documentMetadata } = document

        return {
          ...documentMetadata,
          id: key,
          title: normalizeDocumentationBrandingText(document.title, preserveLegacyBranding),
          description: normalizeDocumentationBrandingText(
            document.description,
            preserveLegacyBranding,
          ),
          tags: document.tags.map((tag) =>
            normalizeDocumentationBrandingText(tag, preserveLegacyBranding),
          ),
          headings: document.headings.map((heading) => ({
            ...heading,
            title: normalizeDocumentationBrandingText(
              heading.title,
              preserveLegacyBranding,
            ),
          })),
          route: `/docs/${key}`,
          sourcePackSha256: sha256,
          sourcePackBytes: bytes,
          projectedChecksumIncluded: false,
          brandingNormalized: !preserveLegacyBranding,
        }
      }),
  }
}

function createLocalizedNavigation(language: DocumentationLocale) {
  const view = getDocumentationView(language)
  const documentIds = new Set(view.documents.map((document) => document.id))

  return {
    ...importedNavigation,
    language,
    groups: importedNavigation.groups
      .filter((group) => inferNavigationLocale(group) === language)
      .map((group) => ({
        ...group,
        id: toCanonicalGroupKey(group.id),
        title:
          language === "de" ? group.title.replace(/\s*\(Deutsch\)\s*$/u, "") : group.title,
        items: group.items
          .filter((item) => documentIds.has(item.id))
          .map((item) => {
            const key = toCanonicalDocumentKey(item.id)
            const preserveLegacyBranding = preservesLegacyDocumentationBranding(key)

            return {
              ...item,
              id: key,
              title: normalizeDocumentationBrandingText(item.title, preserveLegacyBranding),
              description: normalizeDocumentationBrandingText(
                item.description,
                preserveLegacyBranding,
              ),
              route: `/docs/${key}`,
            }
          }),
      })),
  }
}

async function createLocalizedSearchIndex(language: DocumentationLocale) {
  const rawSearchIndex = (
    await import("./release-pack/metadata/search-index.json?raw")
  ).default
  const importedSearchIndex = JSON.parse(rawSearchIndex) as {
    readonly schemaVersion: number
    readonly generatedAtUtc: string
    readonly documents: readonly ImportedSearchPackDocument[]
  }
  const view = getDocumentationView(language)
  const documentIds = new Set(view.documents.map((document) => document.id))

  return {
    ...importedSearchIndex,
    language,
    documents: importedSearchIndex.documents
      .filter((document) => documentIds.has(document.id))
      .map((document) => {
        const key = toCanonicalDocumentKey(document.id)
        const preserveLegacyBranding = preservesLegacyDocumentationBranding(key)

        return {
          ...document,
          id: key,
          title: normalizeDocumentationBrandingText(document.title, preserveLegacyBranding),
          description: normalizeDocumentationBrandingText(
            document.description,
            preserveLegacyBranding,
          ),
          tags: document.tags.map((tag) =>
            normalizeDocumentationBrandingText(tag, preserveLegacyBranding),
          ),
          headings: document.headings.map((heading) => ({
            ...heading,
            title: normalizeDocumentationBrandingText(
              heading.title,
              preserveLegacyBranding,
            ),
          })),
          body: normalizeDocumentationBrandingText(document.body, preserveLegacyBranding),
          route: `/docs/${key}`,
        }
      }),
  }
}

async function readDocumentationAssetBytes(assetPath: string) {
  const assetUrl = documentationAssetUrlByPackPath.get(assetPath)

  if (!assetUrl) {
    throw new Error(`Documentation pack is missing the declared asset: ${assetPath}`)
  }

  const response = await fetch(assetUrl)

  if (!response.ok) {
    throw new Error(
      `Documentation asset '${assetPath}' could not be read for offline download (${response.status}).`,
    )
  }

  return new Uint8Array(await response.arrayBuffer())
}

/**
 * Builds a portable pack containing exactly one documentation language.
 * The browser never combines English and German Markdown in one download.
 */
export async function getDocumentationReleasePackFiles(
  language: DocumentationLocale,
): Promise<readonly DocumentationReleasePackFile[]> {
  const view = getDocumentationView(language)
  const assetPaths = getDocumentationAssetPathsForLanguage(language)
  const markdownFiles = view.documents.map((document) => {
    const modulePath = `./release-pack/${document.sourcePath}`
    const content = rawMarkdownByModulePath[modulePath]

    if (typeof content !== "string") {
      throw new Error(`Documentation pack is missing the declared document: ${document.sourcePath}`)
    }

    return {
      path: document.sourcePath,
      content: normalizeDocumentationBrandingMarkdown(
        content,
        preservesLegacyDocumentationBranding(document.key),
      ),
    }
  })

  const assetFiles = await Promise.all(
    assetPaths.map(async (assetPath) => ({
      path: assetPath,
      content: "",
      binaryContent: await readDocumentationAssetBytes(assetPath),
    })),
  )

  return [
    ...markdownFiles,
    ...assetFiles,
    {
      path: "README.md",
      content: createDocumentationPackReadme(
        language,
        view.documents.length,
        assetPaths.length,
      ),
    },
    {
      path: "metadata/manifest.json",
      content: serializeDocumentationJson(createLocalizedManifest(language)),
    },
    {
      path: "metadata/navigation.json",
      content: serializeDocumentationJson(createLocalizedNavigation(language)),
    },
    {
      path: "metadata/search-index.json",
      content: serializeDocumentationJson(await createLocalizedSearchIndex(language)),
    },
  ].sort((left, right) => left.path.localeCompare(right.path))
}

/**
 * Returns the release-pinned documentation catalog projected into exactly one
 * UI language. Components migrate to this view in DOCS-01A/01B rather than
 * independently re-implementing locale inference.
 */
export function getDocumentationView(language: DocumentationLocale) {
  return documentationViewByLanguage[language]
}

function normalizeDocumentationTag(tag: string) {
  return tag.trim().toLocaleLowerCase()
}

/**
 * Builds language-scoped reading navigation for one canonical document key.
 * Related documents favour the same section, then shared topics, while
 * preserving the release-pack order as a stable tie-breaker.
 */
export function getDocumentationReadingContext(
  language: DocumentationLocale,
  documentKey: string,
): DocumentationReadingContext {
  const view = getDocumentationView(language)
  const readingSequence = view.navigationGroups.flatMap((group) =>
    view.documents.filter((document) => document.groupId === group.id),
  )
  const currentIndex = readingSequence.findIndex((document) => document.key === documentKey)

  if (currentIndex < 0) {
    return { relatedDocuments: [] }
  }

  const currentDocument = readingSequence[currentIndex]
  const previousDocument = currentIndex > 0 ? readingSequence[currentIndex - 1] : undefined
  const nextDocument =
    currentIndex < readingSequence.length - 1 ? readingSequence[currentIndex + 1] : undefined
  const excludedKeys = new Set(
    [currentDocument, previousDocument, nextDocument]
      .filter((document): document is DocumentationDocument => Boolean(document))
      .map((document) => document.key),
  )
  const currentTags = new Set(currentDocument.tags.map(normalizeDocumentationTag))

  const relatedDocuments = view.documents
    .map((candidate, candidateIndex) => {
      if (excludedKeys.has(candidate.key)) {
        return { candidate, score: 0, candidateIndex }
      }

      const sharedTagCount = candidate.tags.reduce(
        (count, tag) => count + (currentTags.has(normalizeDocumentationTag(tag)) ? 1 : 0),
        0,
      )
      const sameGroupScore = candidate.groupKey === currentDocument.groupKey ? 100 : 0
      const sharedTagScore = sharedTagCount * 10

      return {
        candidate,
        score: sameGroupScore + sharedTagScore,
        candidateIndex,
      }
    })
    .filter((result) => result.score > 0)
    .sort((left, right) => right.score - left.score || left.candidateIndex - right.candidateIndex)
    .slice(0, 3)
    .map((result) => result.candidate)

  return {
    previousDocument,
    nextDocument,
    relatedDocuments,
  }
}

/**
 * Compatibility lookup by the imported pack ID. New reader routes should use
 * the language-neutral document key through getDocumentationView().
 */
export function findDocumentationDocument(documentId: string | undefined) {
  return documentId ? documentationDocumentById.get(documentId) : undefined
}

export function resolveDocumentationRoute(
  language: DocumentationLocale,
  documentId: string | undefined,
): DocumentationRouteResolution {
  const view = getDocumentationView(language)

  if (!documentId) {
    return {
      kind: "redirect",
      reason: "not-found",
    }
  }

  const importedVariant = documentationDocumentById.get(documentId)
  const documentKey = importedVariant?.key ?? documentId
  const activeDocument = view.documentByKey.get(documentKey)

  if (activeDocument) {
    if (documentId !== documentKey) {
      return {
        kind: "redirect",
        documentKey,
        reason: "legacy-alias",
      }
    }

    return { kind: "document", document: activeDocument }
  }

  return {
    kind: "redirect",
    reason: documentationVariantsByKey.has(documentKey)
      ? "translation-unavailable"
      : "not-found",
  }
}

function splitDocumentHref(href: string) {
  const fragmentIndex = href.indexOf("#")
  const fragment = fragmentIndex >= 0 ? href.slice(fragmentIndex) : ""
  const hrefWithoutFragment = fragmentIndex >= 0 ? href.slice(0, fragmentIndex) : href
  const queryIndex = hrefWithoutFragment.indexOf("?")
  const query = queryIndex >= 0 ? hrefWithoutFragment.slice(queryIndex) : ""
  const path = queryIndex >= 0 ? hrefWithoutFragment.slice(0, queryIndex) : hrefWithoutFragment

  return { fragment, path, query }
}

function resolveDocumentationPath(currentSourcePath: string, relativePath: string) {
  const pathSegments = currentSourcePath.split("/").slice(0, -1)

  for (const segment of relativePath.split("/")) {
    if (!segment || segment === ".") {
      continue
    }

    if (segment === "..") {
      if (pathSegments.length === 0) {
        return undefined
      }

      pathSegments.pop()
      continue
    }

    pathSegments.push(segment)
  }

  return pathSegments.join("/")
}

export function resolveDocumentationAssetSource(
  document: DocumentationDocument,
  source: string,
) {
  const trimmedSource = source.trim()

  if (
    !trimmedSource ||
    /^(?:[a-z][a-z0-9+.-]*:|\/\/|\/|#)/iu.test(trimmedSource)
  ) {
    return undefined
  }

  const resolvedPath = resolveDocumentationPath(document.sourcePath, trimmedSource)

  if (!resolvedPath || !resolvedPath.startsWith("assets/")) {
    return undefined
  }

  if (!(importedManifest.assets ?? []).some((asset) => asset.path === resolvedPath)) {
    return undefined
  }

  return documentationAssetUrlByPackPath.get(resolvedPath)
}

function resolveAbsoluteDocumentationHref(href: string) {
  const { fragment, path, query } = splitDocumentHref(href)
  const documentId = path.replace(/^\/docs\//u, "")
  const importedVariant = documentationDocumentById.get(documentId)

  return importedVariant ? `/docs/${importedVariant.key}${query}${fragment}` : href
}

/**
 * Resolves only known local Markdown documents into authenticated reader routes.
 * External, fragment-only, and unknown paths are left to the renderer's normal
 * safe-link handling.
 */
export function resolveDocumentationHref(document: DocumentationDocument, href: string) {
  const trimmedHref = href.trim()

  if (!trimmedHref || trimmedHref.startsWith("#") || /^([a-z][a-z0-9+.-]*:|\/\/)/i.test(trimmedHref)) {
    return undefined
  }

  if (trimmedHref.startsWith("/docs/")) {
    return resolveAbsoluteDocumentationHref(trimmedHref)
  }

  const { fragment, path, query } = splitDocumentHref(trimmedHref)

  if (!path.endsWith(".md")) {
    return undefined
  }

  const targetSourcePath = resolveDocumentationPath(document.sourcePath, path)
  const targetDocument = targetSourcePath
    ? documentationDocumentBySourcePath.get(targetSourcePath)
    : undefined

  return targetDocument ? `/docs/${targetDocument.key}${query}${fragment}` : undefined
}
