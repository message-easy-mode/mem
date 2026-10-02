import { describe, expect, it } from "vitest"

import { validateFederationPolicyDraft } from "./federation-policy-draft"

describe("federation policy draft validation", () => {
  it("canonicalises, de-duplicates by policy, and sorts exact domains", () => {
    expect(validateFederationPolicyDraft(
      "restricted",
      "Partner.Example.\nmatrix.example.org\n",
    )).toEqual({
      valid: true,
      canonicalAllowlist: ["matrix.example.org", "partner.example"],
      issues: [],
    })
  })

  it.each([
    "https://partner.example",
    "*.partner.example",
    "partner.example:8448",
    "partner.example/path",
    "192.0.2.10",
  ])("rejects an unsafe exact-domain value: %s", (value) => {
    const result = validateFederationPolicyDraft("restricted", value)

    expect(result.valid).toBe(false)
    expect(result.issues[0]?.code).toBe("invalid_domain")
  })

  it("rejects duplicates after canonicalisation", () => {
    const result = validateFederationPolicyDraft(
      "restricted",
      "Partner.Example\npartner.example.\n",
    )

    expect(result.valid).toBe(false)
    expect(result.issues).toContainEqual({
      code: "duplicate_domain",
      value: "partner.example",
    })
  })

  it("requires at least one Restricted domain and empty Public or Local-only lists", () => {
    expect(validateFederationPolicyDraft("restricted", "").issues[0]?.code)
      .toBe("restricted_allowlist_required")
    expect(validateFederationPolicyDraft("public", "partner.example").issues[0]?.code)
      .toBe("public_allowlist_not_empty")
    expect(validateFederationPolicyDraft("local_only", "partner.example").issues[0]?.code)
      .toBe("local_only_allowlist_not_empty")
    expect(validateFederationPolicyDraft("local_only", "")).toEqual({
      valid: true,
      canonicalAllowlist: [],
      issues: [],
    })
  })
})
