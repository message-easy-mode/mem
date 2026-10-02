import {
  normalizeDocumentationBrandingText,
  preservesLegacyDocumentationBranding,
} from "@/features/operator/docs/documentation-branding"
import { getDocumentationView } from "@/features/operator/docs/documentation-release-pack"
import type {
  DocumentationDocument,
  DocumentationHeading,
  DocumentationLocale,
} from "@/features/operator/docs/docs.types"

type ImportedSearchHeading = Readonly<{
  level: number
  title: string
  anchor: string
}>

type ImportedSearchDocument = Readonly<{
  id: string
  title: string
  description: string
  section: string
  tags: readonly string[]
  path: string
  route: string
  aliases: readonly string[]
  headings: readonly ImportedSearchHeading[]
  body: string
}>

type ImportedSearchIndex = Readonly<{
  schemaVersion: number
  generatedAtUtc: string
  documents: readonly ImportedSearchDocument[]
}>

export type DocumentationSearchFilter = Readonly<{
  groupId?: string
  tag?: string
}>

export type DocumentationSearchSection = Readonly<{
  id: string
  label: string
}>

export type DocumentationSearchResult = Readonly<{
  document: DocumentationDocument
  href: string
  excerpt: string
  matchedHeading?: DocumentationHeading
  sectionLabel: string
}>

let importedSearchIndexPromise: Promise<ImportedSearchIndex> | undefined

function loadImportedSearchIndex() {
  importedSearchIndexPromise ??= import("./release-pack/metadata/search-index.json?raw").then(
    ({ default: rawSearchIndex }) => JSON.parse(rawSearchIndex) as ImportedSearchIndex,
  )

  return importedSearchIndexPromise
}

export function getDocumentationSearchSections(
  language: DocumentationLocale,
): readonly DocumentationSearchSection[] {
  return getDocumentationView(language).navigationGroups.map((group) => ({
    id: group.key,
    label: group.label,
  }))
}

export function getDocumentationSearchTags(language: DocumentationLocale) {
  return Array.from(
    new Set(getDocumentationView(language).documents.flatMap((document) => document.tags)),
  ).sort((left, right) => left.localeCompare(right, language))
}

function normalizeDocumentationSearchText(value: string) {
  return value
    .normalize("NFD")
    .replace(/\p{M}/gu, "")
    .toLocaleLowerCase("en")
}

export function toDocumentationSearchTerms(query: string) {
  return Array.from(
    new Set(
      normalizeDocumentationSearchText(query).match(/[\p{L}\p{N}][\p{L}\p{N}.'’/_-]*/gu) ?? [],
    ),
  )
}

function countOccurrences(value: string, term: string) {
  let count = 0
  let startIndex = 0

  while (startIndex < value.length) {
    const foundIndex = value.indexOf(term, startIndex)

    if (foundIndex < 0) {
      return count
    }

    count += 1
    startIndex = foundIndex + term.length
  }

  return count
}

function scoreField(value: string, term: string, weight: number) {
  const occurrences = countOccurrences(normalizeDocumentationSearchText(value), term)
  return Math.min(occurrences, 4) * weight
}

function findMatchedHeading(
  document: DocumentationDocument,
  searchDocument: ImportedSearchDocument,
  terms: readonly string[],
) {
  const headingsByAnchor = new Map(
    document.headings.map((heading) => [heading.id, heading] as const),
  )

  const candidates = searchDocument.headings
    .map((heading) => {
      const headingText = normalizeDocumentationSearchText(heading.title)
      const score = terms.reduce(
        (total, term) => total + (headingText.includes(term) ? 1 : 0),
        0,
      )

      return { heading, score }
    })
    .filter((candidate) => candidate.score > 0)
    .sort((left, right) => right.score - left.score)

  return candidates.length > 0
    ? headingsByAnchor.get(candidates[0].heading.anchor)
    : undefined
}

/**
 * The imported index intentionally preserves source Markdown for portability.
 * Search results are browser UI, not source previews, so remove Markdown syntax
 * before deriving an excerpt. This keeps link labels and prose while avoiding
 * raw constructs such as `[!IMPORTANT]`, `**bold**`, and `[title](path.md)`.
 */
export function toDocumentationSearchSnippetText(value: string) {
  return value
    .replace(/!\[([^\]]*)\]\([^)]*\)/gu, "$1")
    .replace(/\[([^\]]+)\]\([^)]*\)/gu, "$1")
    .replace(/\[![A-Z][A-Z _-]*\]/gu, " ")
    .replace(/^#{1,6}\s+/gmu, "")
    .replace(/(^|\s)(?:[-+*]|\d+\.)\s+/gu, "$1")
    .replace(/[`*_~]/gu, "")
    .replace(/<[^>]+>/gu, " ")
    .replace(/\s+/gu, " ")
    .trim()
}

function findExcerpt(body: string, description: string, terms: readonly string[]) {
  const originalBody = toDocumentationSearchSnippetText(body)
  const normalizedBody = normalizeDocumentationSearchText(originalBody)
  const matchIndex = terms
    .map((term) => normalizedBody.indexOf(term))
    .filter((index) => index >= 0)
    .sort((left, right) => left - right)[0]

  if (matchIndex === undefined || originalBody.length === 0) {
    return toDocumentationSearchSnippetText(description)
  }

  const radius = 118
  const start = Math.max(0, matchIndex - radius)
  const end = Math.min(originalBody.length, matchIndex + radius)
  const startBoundary = start > 0 ? originalBody.indexOf(" ", start) + 1 : 0
  const endBoundary = end < originalBody.length ? originalBody.lastIndexOf(" ", end) : originalBody.length
  const excerpt = originalBody.slice(startBoundary, endBoundary > startBoundary ? endBoundary : end).trim()

  return `${startBoundary > 0 ? "…" : ""}${excerpt}${endBoundary < originalBody.length ? "…" : ""}`
}

function getSectionLabel(
  language: DocumentationLocale,
  document: DocumentationDocument,
) {
  return (
    getDocumentationView(language).navigationGroups.find(
      (group) => group.key === document.groupKey,
    )?.label ?? document.groupKey
  )
}

/**
 * Searches only the release-pinned index bundled with the offline reader.
 * The index is loaded lazily after an operator searches, keeping it out of the
 * control-plane shell and ensuring no operator query leaves the browser.
 */
export async function searchDocumentation(
  language: DocumentationLocale,
  query: string,
  filter: DocumentationSearchFilter = {},
): Promise<readonly DocumentationSearchResult[]> {
  const terms = toDocumentationSearchTerms(query)

  if (terms.length === 0) {
    return []
  }

  const importedSearchIndex = await loadImportedSearchIndex()
  const documentationSearchDocumentById = new Map(
    importedSearchIndex.documents.map((document) => [document.id, document] as const),
  )
  const normalizedPhrase = normalizeDocumentationSearchText(query.trim())

  return getDocumentationView(language).documents
    .flatMap((document) => {
      if (filter.groupId && document.groupKey !== filter.groupId) {
        return []
      }

      if (filter.tag && !document.tags.includes(filter.tag)) {
        return []
      }

      const importedSearchDocument = documentationSearchDocumentById.get(document.id)

      if (!importedSearchDocument) {
        return []
      }

      const preserveLegacyBranding = preservesLegacyDocumentationBranding(document.key)
      const searchDocument = {
        ...importedSearchDocument,
        title: normalizeDocumentationBrandingText(
          importedSearchDocument.title,
          preserveLegacyBranding,
        ),
        description: normalizeDocumentationBrandingText(
          importedSearchDocument.description,
          preserveLegacyBranding,
        ),
        tags: importedSearchDocument.tags.map((tag) =>
          normalizeDocumentationBrandingText(tag, preserveLegacyBranding),
        ),
        headings: importedSearchDocument.headings.map((heading) => ({
          ...heading,
          title: normalizeDocumentationBrandingText(
            heading.title,
            preserveLegacyBranding,
          ),
        })),
        body: normalizeDocumentationBrandingText(
          importedSearchDocument.body,
          preserveLegacyBranding,
        ),
      }

      const searchableText = [
        searchDocument.title,
        searchDocument.description,
        searchDocument.tags.join(" "),
        searchDocument.headings.map((heading) => heading.title).join(" "),
        searchDocument.body,
      ]
        .map(normalizeDocumentationSearchText)
        .join("\n")

      if (!terms.every((term) => searchableText.includes(term))) {
        return []
      }

      const headingText = searchDocument.headings.map((heading) => heading.title).join(" ")
      const score = terms.reduce(
        (total, term) =>
          total +
          scoreField(searchDocument.title, term, 120) +
          scoreField(searchDocument.tags.join(" "), term, 72) +
          scoreField(headingText, term, 54) +
          scoreField(searchDocument.description, term, 36) +
          scoreField(searchDocument.body, term, 5),
        0,
      ) +
        (normalizedPhrase && normalizeDocumentationSearchText(searchDocument.title).includes(normalizedPhrase)
          ? 180
          : 0) +
        (normalizedPhrase && normalizeDocumentationSearchText(headingText).includes(normalizedPhrase)
          ? 720
          : 0)

      const matchedHeading = findMatchedHeading(document, searchDocument, terms)

      return [
        {
          document,
          href: `/docs/${document.key}${matchedHeading ? `#${matchedHeading.id}` : ""}`,
          excerpt: findExcerpt(searchDocument.body, searchDocument.description, terms),
          matchedHeading,
          sectionLabel: getSectionLabel(language, document),
          score,
        },
      ]
    })
    .sort((left, right) => {
      if (right.score !== left.score) {
        return right.score - left.score
      }

      return left.document.title.localeCompare(right.document.title, language)
    })
}
