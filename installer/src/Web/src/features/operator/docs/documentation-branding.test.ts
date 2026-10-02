import { describe, expect, it } from "vitest"

import {
  documentationProductIdentity,
  normalizeDocumentationBrandingMarkdown,
  normalizeDocumentationBrandingText,
  preservesLegacyDocumentationBranding,
} from "./documentation-branding"

describe("documentation branding", () => {
  it("defines the current MEM product identity", () => {
    expect(documentationProductIdentity).toMatchObject({
      shortName: "MEM",
      fullName: "Message Easy Mode",
      controlPlaneName: "MEM Control Plane",
    })
  })

  it("normalizes current human-facing product names", () => {
    expect(normalizeDocumentationBrandingText("MatrixEasyMode API for Matrix Easy Mode"))
      .toBe("MEM API for Message Easy Mode")
  })

  it("preserves fenced commands and inline technical identifiers", () => {
    const markdown = [
      "MatrixEasyMode is the current product name in this imported prose.",
      "Use `MatrixEasyMode.Legacy.Namespace` only when the identifier still exists.",
      "```text",
      "MatrixEasyMode.Legacy.Namespace",
      "```",
    ].join("\n")

    expect(normalizeDocumentationBrandingMarkdown(markdown)).toBe([
      "MEM is the current product name in this imported prose.",
      "Use `MatrixEasyMode.Legacy.Namespace` only when the identifier still exists.",
      "```text",
      "MatrixEasyMode.Legacy.Namespace",
      "```",
    ].join("\n"))
  })

  it("retains historical branding for the legacy 0.1.0 release page", () => {
    expect(preservesLegacyDocumentationBranding("releases/0.1.0")).toBe(true)
    expect(normalizeDocumentationBrandingText("MatrixEasyMode v0.1.0", true))
      .toBe("MatrixEasyMode v0.1.0")
  })
})
