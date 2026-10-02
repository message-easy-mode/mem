import { describe, expect, it, vi } from "vitest"

import {
  isNewCompletedImport,
  reconcileCertificateImportOutcome,
} from "./certificate-import-outcome"

import type { OperatorCertificateSummary } from "./domains.types"

function certificate(
  lastImportedToNpmAtUtc: string | null,
  overrides: Partial<OperatorCertificateSummary> = {},
): OperatorCertificateSummary {
  return {
    id: "registry-cert-production",
    domainId: "domain-deltabox",
    certificateId: "cert-production",
    commonName: "*.deltabox.dev",
    provider: "desec",
    isWildcard: true,
    isStaging: false,
    isMainPlatformCertificate: true,
    isActive: true,
    status: "Succeeded",
    createdAtUtc: "2026-08-19T04:45:03Z",
    expiresAtUtc: "2026-11-17T04:45:03Z",
    thumbprint: "thumbprint",
    npmCertificateId: 1,
    importedToNpm: true,
    lastValidatedAtUtc: null,
    lastImportedToNpmAtUtc,
    lastError: null,
    ...overrides,
  }
}

describe("certificate import outcome reconciliation", () => {
  it("requires a newer authoritative completed import", () => {
    const previous = certificate("2026-08-20T00:00:01Z")

    expect(
      isNewCompletedImport(previous, certificate("2026-08-20T00:00:02Z")),
    ).toBe(true)
    expect(
      isNewCompletedImport(previous, certificate("2026-08-20T00:00:01Z")),
    ).toBe(false)
    expect(
      isNewCompletedImport(
        previous,
        certificate("2026-08-20T00:00:02Z", { importedToNpm: false }),
      ),
    ).toBe(false)
  })

  it("confirms success when later authoritative registry state records the import", async () => {
    const previous = certificate("2026-08-20T00:00:01Z")
    const readImportState = vi
      .fn()
      .mockResolvedValueOnce(previous)
      .mockResolvedValueOnce(certificate("2026-08-20T00:00:12Z"))

    const result = await reconcileCertificateImportOutcome(
      "cert-production",
      "deltabox.dev",
      previous,
      {
        attempts: 2,
        delayMs: 0,
        readImportState,
        wait: async () => undefined,
      },
    )

    expect(result?.certificateId).toBe("cert-production")
    expect(result?.lastImportedToNpmAtUtc).toBe("2026-08-20T00:00:12Z")
    expect(readImportState).toHaveBeenCalledTimes(2)
  })

  it("does not claim success without a pre-import authoritative baseline", async () => {
    const readImportState = vi.fn()

    const result = await reconcileCertificateImportOutcome(
      "cert-production",
      "deltabox.dev",
      null,
      { readImportState },
    )

    expect(result).toBeNull()
    expect(readImportState).not.toHaveBeenCalled()
  })

  it("tolerates transient read failures while waiting for authoritative state", async () => {
    const previous = certificate("2026-08-20T00:00:01Z")
    const readImportState = vi
      .fn()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(certificate("2026-08-20T00:00:12Z"))

    const result = await reconcileCertificateImportOutcome(
      "cert-production",
      "deltabox.dev",
      previous,
      {
        attempts: 2,
        delayMs: 0,
        readImportState,
        wait: async () => undefined,
      },
    )

    expect(result?.npmCertificateId).toBe(1)
  })
})
