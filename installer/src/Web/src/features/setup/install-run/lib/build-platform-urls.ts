export type PlatformUrlModel = {
  zone: string | null
  webDomain: string | null
  apiDomain: string | null
  memWebUrl: string | null
  memApiUrl: string | null
  memApiHealthUrl: string | null
  isStagingCertificate: boolean
}

export type PlatformUrlInstallationLike = {
  configJson?: string | null
  frozenConfigJson?: string | null
} | null | undefined

type InstallationConfigLike = {
  ingressTls?: {
    zone?: string | null
    proxyHostDomain?: string | null
    useStaging?: boolean | null
  } | null
} | null | undefined

export function buildPlatformUrlsFromInstallation(
  installation: PlatformUrlInstallationLike,
): PlatformUrlModel {
  const config = parseInstallationConfig(installation)

  return buildPlatformUrls(config)
}

export function buildPlatformUrls(
  config: InstallationConfigLike,
): PlatformUrlModel {
  const ingressTls = config?.ingressTls ?? null

  const zone = normalizeDomainPart(ingressTls?.zone)
  const configuredWebDomain = normalizeDomainPart(ingressTls?.proxyHostDomain)

  const webDomain = configuredWebDomain ?? (zone ? `admin.${zone}` : null)
  const apiDomain = zone ? `api.${zone}` : null

  const memWebUrl = webDomain ? `https://${webDomain}` : null
  const memApiUrl = apiDomain ? `https://${apiDomain}` : null
  const memApiHealthUrl = memApiUrl ? `${memApiUrl}/health/ready` : null

  return {
    zone,
    webDomain,
    apiDomain,
    memWebUrl,
    memApiUrl,
    memApiHealthUrl,
    isStagingCertificate: ingressTls?.useStaging === true,
  }
}

function parseInstallationConfig(
  installation: PlatformUrlInstallationLike,
): InstallationConfigLike {
  const configJson = installation?.frozenConfigJson ?? installation?.configJson

  if (!configJson) {
    return null
  }

  try {
    return JSON.parse(configJson) as InstallationConfigLike
  } catch {
    return null
  }
}

function normalizeDomainPart(value: string | null | undefined): string | null {
  const trimmed = value?.trim()

  if (!trimmed) {
    return null
  }

  return trimmed
    .replace(/^https?:\/\//i, "")
    .replace(/\/+$/g, "")
    .toLowerCase()
}
