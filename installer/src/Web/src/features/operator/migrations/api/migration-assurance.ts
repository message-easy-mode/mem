export type MigrationAssuranceSummary = {
  authorityType: "final-frozen" | "operator-attested-snapshot" | string
  productionAuthorityId: string | null
  authorityEvidenceSha256: string | null
  packageRevisionId: string
  captureKind: string
  sourceFrozen: boolean
  rehearsalOnly: boolean
  formalSourceFreezeEvidenceCollected: boolean
  finalRecapturePerformed: boolean
  postCaptureWritesIndependentlyExcluded: boolean
  rollbackAssurance: string
  twoServerQualificationRequired: boolean
  detail: string
}

export const isSimplifiedMigrationAssurance = (
  assurance: MigrationAssuranceSummary | null | undefined,
): assurance is MigrationAssuranceSummary =>
  assurance?.authorityType === "operator-attested-snapshot"
