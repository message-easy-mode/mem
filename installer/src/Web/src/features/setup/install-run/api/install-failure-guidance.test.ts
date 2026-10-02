import { describe, expect, it } from "vitest"

import { getInstallFailureGuidance } from "./install-failure-guidance"
import type { WorkflowStep } from "./install.types"

describe("install failure guidance", () => {
  it("explains NPM import failure without implying another certificate request", () => {
    const guidance = getInstallFailureGuidance(
      failedStep(
        "Issue and import platform certificate",
        "Platform certificate preparation failed.",
        "NpmCertificateImportFailed: NPM API authority was unavailable.",
      ),
      JSON.stringify({
        publicAccess: {
          certificateId: "cert-01e-existing",
          npmCertificateId: null,
          importedToNpm: false,
        },
      }),
    )

    expect(guidance).not.toBeNull()
    expect(guidance?.title).toBe("Certificate ready; NPM import failed")
    expect(guidance?.failedPhase).toBe("Certificate → NPM import")
    expect(guidance?.technicalReason).toBe("NPM API authority was unavailable.")
    expect(guidance?.retryBehavior).toContain("reuse the existing stored certificate")
    expect(guidance?.retryBehavior).toContain("initialize or verify NPM")
    expect(guidance?.retryBehavior).toContain("not request another certificate")
    expect(guidance?.completedPhases.join(" ")).toContain("stored by MEM")
  })

  it("keeps a useful generic persisted-step explanation for other failures", () => {
    const guidance = getInstallFailureGuidance(
      failedStep(
        "Create persistent volumes",
        "Failed to create persistent volumes.",
        "DockerVolumeCreateFailed: volume creation was rejected.",
      ),
      null,
    )

    expect(guidance?.title).toBe("Setup stopped at Create persistent volumes")
    expect(guidance?.failedPhase).toBe("Create persistent volumes")
    expect(guidance?.retryBehavior).toContain("Completed steps remain complete")
  })
})

function failedStep(
  title: string,
  message: string,
  errorMessage: string,
): WorkflowStep {
  return {
    order: 8,
    name: title,
    title,
    kind: "server-step",
    tags: [],
    requiresHumanAction: false,
    isCheckpoint: false,
    status: "Failed",
    message,
    errorMessage,
    attemptCount: 1,
  }
}
