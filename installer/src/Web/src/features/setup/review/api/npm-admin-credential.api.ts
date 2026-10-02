import { getJson, postJson } from "@/lib/api"

export type NpmAdminCredentialRequest = {
  email: string
  password: string
}

export type NpmAdminCredentialProjection = {
  installationId: string | null
  status: string
  message: string
  errorCode: string | null
  administratorEmail: string | null
  credentialStored: boolean
  verifiedAtUtc: string | null
}

const basePath = "/api/setup/npm-administrator"

export function getNpmAdminCredential() {
  return getJson<NpmAdminCredentialProjection>(`${basePath}/`)
}

export function saveNpmAdminCredential(request: NpmAdminCredentialRequest) {
  return postJson<NpmAdminCredentialRequest, NpmAdminCredentialProjection>(
    `${basePath}/`,
    request,
  )
}
