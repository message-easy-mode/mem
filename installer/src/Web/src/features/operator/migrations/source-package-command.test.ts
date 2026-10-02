import { describe, expect, it } from "vitest"

import { buildSourcePackageCommand } from "./source-package-command"

describe("buildSourcePackageCommand", () => {
  it("builds the path-free installed mem-migrate command", () => {
    const command = buildSourcePackageCommand({
      intakeId: "mig_20260715-secure",
      ageRecipient:
        "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
      recipientFingerprint: "A1B2-C3D4-E5F6-0718",
    })

    expect(command.split("\n")[0]).toBe(
      "mem-migrate source package-for-intake \\",
    )
    expect(command).toContain("--intake-id 'mig_20260715-secure'")
    expect(command).toContain(
      "--recipient-fingerprint 'A1B2-C3D4-E5F6-0718'",
    )
    expect(command).not.toContain("--archive")
    expect(command).not.toContain("--output-directory")
    expect(command).not.toContain("dotnet run")
    expect(command).not.toContain("set -euo pipefail")
    expect(command).not.toContain("AGE-SECRET-KEY-")
  })


  it("builds a revision-bound final frozen package command", () => {
    const command = buildSourcePackageCommand({
      intakeId: "mig_20260715-secure",
      packageRevisionId: "mpr_20260718-final",
      ageRecipient:
        "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
      recipientFingerprint: "A1B2-C3D4-E5F6-0718",
      requireFinalFrozen: true,
    })

    expect(command).toContain("--package-revision-id 'mpr_20260718-final'")
    expect(command).toContain("--require-final-frozen")
    expect(command).not.toContain("--archive")
    expect(command).not.toContain("AGE-SECRET-KEY-")
  })

  it("requires a package revision for final frozen packaging", () => {
    expect(() =>
      buildSourcePackageCommand({
        intakeId: "mig_1",
        ageRecipient:
          "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
        recipientFingerprint: "0000-0000-0000-0000",
        requireFinalFrozen: true,
      }),
    ).toThrow("packageRevisionId is required")
  })

  it("rejects an incomplete target binding", () => {
    expect(() =>
      buildSourcePackageCommand({
        intakeId: "mig_1",
        ageRecipient: " ",
        recipientFingerprint: "0000-0000-0000-0000",
      }),
    ).toThrow("ageRecipient is required")
  })
})
