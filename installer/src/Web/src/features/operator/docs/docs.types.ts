import type { UiLanguage } from "@/app/i18n/messages"

export type DocumentationLocale = UiLanguage

export type DocumentationNavigationGroup = Readonly<{
  id: string
  key: string
  locale: DocumentationLocale
  label: string
}>

export type DocumentationHeading = Readonly<{
  id: string
  label: string
  level: 2 | 3
}>

export type DocumentationDocument = Readonly<{
  id: string
  key: string
  locale: DocumentationLocale
  groupId: string
  groupKey: string
  title: string
  summary: string
  tags: readonly string[]
  estimatedReadMinutes: number
  headings: readonly DocumentationHeading[]
  markdown: string
  sourcePath: string
}>

export type DocumentationView = Readonly<{
  language: DocumentationLocale
  navigationGroups: readonly DocumentationNavigationGroup[]
  documents: readonly DocumentationDocument[]
  defaultDocument: DocumentationDocument
  documentByKey: ReadonlyMap<string, DocumentationDocument>
}>

export type DocumentationReadingContext = Readonly<{
  previousDocument?: DocumentationDocument
  nextDocument?: DocumentationDocument
  relatedDocuments: readonly DocumentationDocument[]
}>

export type DocumentationReleasePack = Readonly<{
  productName: string
  productFullName: string
  controlPlaneName: string
  packName: string
  brandingProjectionVersion: number
  schemaVersion: number
  version: string
  generatedAtUtc: string
  documentCount: number
  downloadFileCount: number
  downloadName: string
  reviewRequired: boolean
  reviewReason: string
}>

export type DocumentationLanguageReleasePack = DocumentationReleasePack & Readonly<{
  language: DocumentationLocale
}>

export type DocumentationReleasePackFile = Readonly<{
  path: string
  content: string
  binaryContent?: Uint8Array
}>
