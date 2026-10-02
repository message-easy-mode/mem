import type { ManagedFederationMode } from "../api/federation.types"

export type FederationDraftIssueCode =
  | "public_allowlist_not_empty"
  | "local_only_allowlist_not_empty"
  | "restricted_allowlist_required"
  | "invalid_domain"
  | "duplicate_domain"

export type FederationDraftIssue = {
  code: FederationDraftIssueCode
  value?: string
}

export type FederationDraftValidation = {
  valid: boolean
  canonicalAllowlist: string[]
  issues: FederationDraftIssue[]
}

export function validateFederationPolicyDraft(
  mode: ManagedFederationMode,
  domainText: string,
): FederationDraftValidation {
  const submitted = domainText
    .split(/\r?\n/)
    .filter((value) => value.trim().length > 0)

  if (mode === "public" || mode === "local_only") {
    return submitted.length === 0
      ? { valid: true, canonicalAllowlist: [], issues: [] }
      : {
          valid: false,
          canonicalAllowlist: [],
          issues: [{
            code: mode === "public"
              ? "public_allowlist_not_empty"
              : "local_only_allowlist_not_empty",
          }],
        }
  }

  if (submitted.length === 0) {
    return {
      valid: false,
      canonicalAllowlist: [],
      issues: [{ code: "restricted_allowlist_required" }],
    }
  }

  const canonical: string[] = []
  const seen = new Set<string>()
  const issues: FederationDraftIssue[] = []

  for (const value of submitted) {
    const normalized = canonicalizeExactDomain(value)
    if (!normalized) {
      issues.push({ code: "invalid_domain", value })
      continue
    }

    if (seen.has(normalized)) {
      issues.push({ code: "duplicate_domain", value: normalized })
      continue
    }

    seen.add(normalized)
    canonical.push(normalized)
  }

  canonical.sort()

  return {
    valid: issues.length === 0,
    canonicalAllowlist: canonical,
    issues,
  }
}

function canonicalizeExactDomain(value: string): string | null {
  let candidate = value.trim()
  if (!candidate || candidate.length > 254) return null

  if (
    /\s/.test(candidate) ||
    candidate.includes("*") ||
    candidate.includes("://") ||
    candidate.includes("/") ||
    candidate.includes("?") ||
    candidate.includes("#") ||
    candidate.includes("@") ||
    candidate.includes(":")
  ) {
    return null
  }

  candidate = candidate.replace(/\.$/, "").toLowerCase()
  if (!candidate || candidate.length > 253) return null

  let hostname: string
  try {
    const parsed = new URL(`http://${candidate}`)
    hostname = parsed.hostname.replace(/\.$/, "").toLowerCase()
  } catch {
    return null
  }

  if (!hostname || hostname.length > 253 || isIpv4Literal(hostname)) {
    return null
  }

  const labels = hostname.split(".")

  for (const label of labels) {
    if (
      label.length === 0 ||
      label.length > 63 ||
      !/^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/.test(label)
    ) {
      return null
    }
  }

  return hostname
}

function isIpv4Literal(value: string) {
  const labels = value.split(".")
  return labels.length === 4 && labels.every((label) => {
    if (!/^\d{1,3}$/.test(label)) return false
    const number = Number(label)
    return number >= 0 && number <= 255
  })
}
