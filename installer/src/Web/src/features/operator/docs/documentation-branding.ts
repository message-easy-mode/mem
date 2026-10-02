export const documentationProductIdentity = {
  shortName: "MEM",
  fullName: "Message Easy Mode",
  controlPlaneName: "MEM Control Plane",
  packName: "MEM Documentation",
  brandingProjectionVersion: 1,
} as const

const legacyBrandingDocumentKeys = new Set(["releases/0.1.0"])

export function preservesLegacyDocumentationBranding(documentKey: string) {
  return legacyBrandingDocumentKeys.has(documentKey)
}

export function normalizeDocumentationBrandingText(
  value: string,
  preserveLegacyBranding = false,
) {
  if (preserveLegacyBranding) {
    return value
  }

  return value
    .replace(/\bMatrixEasyMode\b/gu, documentationProductIdentity.shortName)
    .replace(/\bMatrix Easy Mode\b/gu, documentationProductIdentity.fullName)
}

function normalizeMarkdownLine(line: string) {
  const segments = line.split(/(`+[^`]*`+)/gu)

  return segments
    .map((segment, index) =>
      index % 2 === 1 ? segment : normalizeDocumentationBrandingText(segment),
    )
    .join("")
}

/**
 * Updates human-facing product wording while preserving fenced commands,
 * identifiers, inline code, URLs, and the historical MEM 0.1.0 release page.
 */
export function normalizeDocumentationBrandingMarkdown(
  markdown: string,
  preserveLegacyBranding = false,
) {
  if (preserveLegacyBranding) {
    return markdown
  }

  let activeFence: "```" | "~~~" | undefined

  return markdown
    .split("\n")
    .map((line) => {
      const fence = line.trimStart().match(/^(```|~~~)/u)?.[1] as "```" | "~~~" | undefined

      if (fence) {
        if (!activeFence) {
          activeFence = fence
        } else if (activeFence === fence) {
          activeFence = undefined
        }

        return line
      }

      return activeFence ? line : normalizeMarkdownLine(line)
    })
    .join("\n")
}
