import type { WorkflowStep } from "./install.types"

export type InstallFailureGuidance = {
  title: string
  failedPhase: string
  summary: string
  completedPhases: string[]
  technicalReason: string
  nextAction: string
  retryBehavior: string
  retryable: boolean
}

type PublicAccessOutcome = {
  certificateId?: string | null
  npmCertificateId?: number | null
  importedToNpm?: boolean
}

export function getInstallFailureGuidance(
  step: WorkflowStep | null,
  configJson: string | null | undefined,
): InstallFailureGuidance | null {
  if (!step || step.status !== "Failed") {
    return null
  }

  const errorCode = getErrorCode(step.errorMessage)
  const publicAccess = readPublicAccess(configJson)

  if (errorCode === "NpmCertificateImportFailed") {
    const certificateReusable = Boolean(publicAccess?.certificateId)

    return {
      title: certificateReusable
        ? "Certificate ready; NPM import failed"
        : "Certificate import into NPM failed",
      failedPhase: "Certificate → NPM import",
      summary: certificateReusable
        ? "MEM reached the NPM import phase. A reusable wildcard certificate is already stored by MEM, but NPM could not import it."
        : "MEM reached the NPM import phase, but NPM could not accept the platform certificate.",
      completedPhases: certificateReusable
        ? [
            "Certificate acquisition completed before the NPM import failure.",
            "The wildcard certificate is stored by MEM and remains available for retry.",
          ]
        : ["The certificate pipeline reached the NPM import phase."],
      technicalReason:
        stripErrorCode(step.errorMessage, errorCode) ??
        step.message ??
        "NPM certificate import did not complete.",
      nextAction:
        "Retry Setup. MEM will establish or verify the protected NPM administrator credential before retrying the certificate import.",
      retryBehavior: certificateReusable
        ? "MEM will initialize or verify NPM as needed, reuse the existing stored certificate, and retry the NPM import. It will not request another certificate."
        : "Completed installation steps remain complete; MEM will retry the failed certificate step after the issue is corrected.",
      retryable: true,
    }
  }

  return {
    title: `Setup stopped at ${step.title}`,
    failedPhase: step.title,
    summary:
      step.message ??
      "MEM stopped at a persisted installation step before continuing with later platform changes.",
    completedPhases: [],
    technicalReason:
      stripErrorCode(step.errorMessage, errorCode) ??
      step.errorMessage ??
      "No additional technical reason was recorded.",
    nextAction:
      "Review the technical reason, correct the underlying issue, then retry Setup.",
    retryBehavior:
      "Completed steps remain complete. MEM retries the failed step rather than starting the installation from the beginning.",
    retryable: true,
  }
}

function getErrorCode(errorMessage: string | null | undefined) {
  if (!errorMessage) return null

  const separator = errorMessage.indexOf(":")
  if (separator <= 0) return null

  const candidate = errorMessage.slice(0, separator).trim()
  return /^[A-Za-z][A-Za-z0-9_.-]+$/.test(candidate) ? candidate : null
}

function stripErrorCode(
  errorMessage: string | null | undefined,
  errorCode: string | null,
) {
  if (!errorMessage) return null
  if (!errorCode) return errorMessage.trim()

  const prefix = `${errorCode}:`
  return errorMessage.startsWith(prefix)
    ? errorMessage.slice(prefix.length).trim()
    : errorMessage.trim()
}

function readPublicAccess(
  configJson: string | null | undefined,
): PublicAccessOutcome | null {
  if (!configJson) return null

  try {
    const parsed = JSON.parse(configJson) as {
      publicAccess?: PublicAccessOutcome
    }
    return parsed.publicAccess ?? null
  } catch {
    return null
  }
}
