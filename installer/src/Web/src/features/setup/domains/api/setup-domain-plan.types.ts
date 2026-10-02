import type { CertificateOperationEvidence } from "@/features/shared/domains/api/domains.types"

export type SetupDomainPlanRequest = {
  baseDomain: string
  acmeEmail: string
  dnsProvider: "desec"
  providerToken: string
  useStaging: boolean
}

export type SetupDomainPlanResponse = {
  installationId: string | null
  configured: boolean
  succeeded: boolean
  status: string
  message: string
  errorCode: string | null
  errorDetail: string | null
  domain: string
  zone: string
  acmeEmail: string
  dnsProvider: "desec"
  useStaging: boolean
  providerAccessConfirmed: boolean
  providerCredentialStored: boolean
  validatedAtUtc: string | null
  evidence: CertificateOperationEvidence[]
}
