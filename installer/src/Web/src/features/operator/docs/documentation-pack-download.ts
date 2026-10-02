import type { DocumentationLocale } from "@/features/operator/docs/docs.types"
import {
  getDocumentationReleasePack,
  getDocumentationReleasePackFiles,
} from "@/features/operator/docs/documentation-release-pack"
import { createDocumentationZip } from "@/features/operator/docs/documentation-zip"

export async function createDocumentationPackArchive(language: DocumentationLocale) {
  const documentationReleasePack = getDocumentationReleasePack(language)

  return createDocumentationZip(
    await getDocumentationReleasePackFiles(language),
    new Date(documentationReleasePack.generatedAtUtc),
  )
}

function saveDocumentationPack(blob: Blob, downloadName: string) {
  const objectUrl = URL.createObjectURL(blob)
  const anchor = document.createElement("a")

  anchor.href = objectUrl
  anchor.download = downloadName
  anchor.style.display = "none"

  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()

  window.setTimeout(() => {
    URL.revokeObjectURL(objectUrl)
  }, 1000)
}

export async function downloadDocumentationPack(language: DocumentationLocale) {
  const documentationReleasePack = getDocumentationReleasePack(language)
  const archive = await createDocumentationPackArchive(language)
  const blob = new Blob([archive.buffer], { type: "application/zip" })

  saveDocumentationPack(blob, documentationReleasePack.downloadName)
}
