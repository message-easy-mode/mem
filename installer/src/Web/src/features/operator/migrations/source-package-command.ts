export interface SourcePackageCommandOptions {
  intakeId: string
  ageRecipient: string
  recipientFingerprint: string
  packageRevisionId?: string
  requireFinalFrozen?: boolean
}

export function buildSourcePackageCommand(
  options: SourcePackageCommandOptions,
): string {
  const intakeId = requireValue(options.intakeId, "intakeId")
  const ageRecipient = requireValue(options.ageRecipient, "ageRecipient")
  const recipientFingerprint = requireValue(
    options.recipientFingerprint,
    "recipientFingerprint",
  )
  const requireFinalFrozen = options.requireFinalFrozen === true
  const packageRevisionId = options.packageRevisionId?.trim() ?? ""

  if (requireFinalFrozen && !packageRevisionId) {
    throw new Error("packageRevisionId is required for final frozen packaging")
  }

  const lines = [
    "mem-migrate source package-for-intake \\",
    `  --intake-id ${shellQuote(intakeId)} \\`,
  ]

  if (packageRevisionId) {
    lines.push(`  --package-revision-id ${shellQuote(packageRevisionId)} \\`)
  }

  lines.push(
    `  --age-recipient ${shellQuote(ageRecipient)} \\`,
    `  --recipient-fingerprint ${shellQuote(recipientFingerprint)}${requireFinalFrozen ? " \\" : ""}`,
  )

  if (requireFinalFrozen) {
    lines.push("  --require-final-frozen")
  }

  return lines.join("\n")
}

function requireValue(value: string, name: string): string {
  const trimmed = value.trim()
  if (!trimmed) {
    throw new Error(`${name} is required`)
  }

  return trimmed
}

function shellQuote(value: string): string {
  return `'${value.replaceAll("'", `'"'"'`)}'`
}
