import { getOperatorDomain, listOperatorDomains } from "./domains.api"

import type { OperatorCertificateSummary } from "./domains.types"

type ReadAuthoritativeImportState = (
  certificateId: string,
  baseDomain: string,
) => Promise<OperatorCertificateSummary | null>

type ReconcileCertificateImportOptions = {
  attempts?: number
  delayMs?: number
  readImportState?: ReadAuthoritativeImportState
  wait?: (milliseconds: number) => Promise<void>
}

const DEFAULT_ATTEMPTS = 12
const DEFAULT_DELAY_MS = 1_000

export async function readAuthoritativeCertificateImportState(
  certificateId: string,
  baseDomain: string,
): Promise<OperatorCertificateSummary | null> {
  const normalizedBaseDomain = normalizeDomain(baseDomain)
  const domains = await listOperatorDomains()
  const domain = domains.find(
    (item) => normalizeDomain(item.baseDomain) === normalizedBaseDomain,
  )

  if (!domain) {
    return null
  }

  const detail = await getOperatorDomain(domain.id)
  return (
    detail.certificates.find((item) => item.certificateId === certificateId) ?? null
  )
}

export async function reconcileCertificateImportOutcome(
  certificateId: string,
  baseDomain: string,
  previous: OperatorCertificateSummary | null,
  options: ReconcileCertificateImportOptions = {},
): Promise<OperatorCertificateSummary | null> {
  if (!previous) {
    return null
  }

  const attempts = Math.max(1, options.attempts ?? DEFAULT_ATTEMPTS)
  const delayMs = Math.max(0, options.delayMs ?? DEFAULT_DELAY_MS)
  const readImportState =
    options.readImportState ?? readAuthoritativeCertificateImportState
  const wait = options.wait ?? delay

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    try {
      const observed = await readImportState(certificateId, baseDomain)

      if (observed && isNewCompletedImport(previous, observed)) {
        return observed
      }
    } catch {
      // Browser transport can remain temporarily inconclusive while the accepted
      // server-owned import finishes. Keep reconciliation bounded and read-only.
    }

    if (attempt < attempts - 1) {
      await wait(delayMs)
    }
  }

  return null
}

export function isNewCompletedImport(
  previous: OperatorCertificateSummary,
  observed: OperatorCertificateSummary,
) {
  if (
    observed.certificateId !== previous.certificateId ||
    observed.importedToNpm !== true ||
    (observed.npmCertificateId ?? 0) <= 0 ||
    !observed.lastImportedToNpmAtUtc
  ) {
    return false
  }

  if (!previous.lastImportedToNpmAtUtc) {
    return true
  }

  return observed.lastImportedToNpmAtUtc !== previous.lastImportedToNpmAtUtc
}

function normalizeDomain(value: string) {
  return value.trim().toLowerCase().replace(/^\*\./, "").replace(/\.$/, "")
}

function delay(milliseconds: number) {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, milliseconds)
  })
}
