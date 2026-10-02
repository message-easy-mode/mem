import type { UseFormReturn } from "react-hook-form"

import type {
  CertificateOperationResult,
  NpmCertificateProbeResult,
  NpmCertificateTestProxyHostResult,
} from "@/features/shared/domains/api/domains.types"

export type IssueFormValues = {
  domain: string
  zone: string
  email: string
  providerToken: string
  storageName: string
  useStaging: boolean
}

export type ProxyHostFormValues = {
  domain: string
  forwardHost: string
  forwardPort: number
  forwardScheme: string
}

export type LastResult =
  | CertificateOperationResult
  | NpmCertificateProbeResult
  | NpmCertificateTestProxyHostResult
  | null

export type IssueCertificateForm = UseFormReturn<IssueFormValues>
export type ProxyHostForm = UseFormReturn<ProxyHostFormValues>