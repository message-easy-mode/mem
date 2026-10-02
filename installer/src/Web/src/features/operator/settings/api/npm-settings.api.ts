export type OperatorNpmSettings = {
  runtimeStatus: string
  runtimeExists: boolean
  running: boolean
  runtimeState: string | null
  runtimeImage: string | null
  approvedRuntimeImage: string
  approvedRuntimeVersion: string
  runtimeImageAligned: boolean
  browserUrl: string | null
  administratorEmail: string | null
  credentialStored: boolean
  credentialStatus: string
  lastVerifiedAtUtc: string | null
  warning: string | null
}

export type OperatorNpmCredentialReveal = {
  administratorEmail: string
  password: string
}

export type UpdateOperatorNpmCredentialInput = {
  email: string
  password: string
}

const npmSettingsRequest = {
  credentials: "include" as const,
  cache: "no-store" as const,
}

export class NpmSettingsProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "NpmSettingsProblemError"
    this.status = status
    this.code = code
  }
}

export function isStepUpRequiredNpmSettingsProblem(value: unknown): boolean {
  return value instanceof NpmSettingsProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

async function readProblem(response: Response): Promise<{ code: string | null; message: string | null }> {
  try {
    const body = (await response.json()) as { status?: unknown; message?: unknown }
    return {
      code: typeof body.status === "string" ? body.status : null,
      message: typeof body.message === "string" ? body.message : null,
    }
  } catch {
    return { code: null, message: null }
  }
}

async function throwProblem(response: Response, operation: string): Promise<never> {
  const problem = await readProblem(response)
  throw new NpmSettingsProblemError(
    problem.message ?? `${operation} failed with status ${response.status}`,
    response.status,
    problem.code,
  )
}

function parseSettings(value: unknown): OperatorNpmSettings {
  const candidate = value as Partial<OperatorNpmSettings>

  if (
    typeof candidate.runtimeStatus !== "string" ||
    typeof candidate.runtimeExists !== "boolean" ||
    typeof candidate.running !== "boolean" ||
    typeof candidate.approvedRuntimeImage !== "string" ||
    typeof candidate.approvedRuntimeVersion !== "string" ||
    typeof candidate.runtimeImageAligned !== "boolean" ||
    typeof candidate.credentialStored !== "boolean" ||
    typeof candidate.credentialStatus !== "string"
  ) {
    throw new NpmSettingsProblemError(
      "GET /api/operator/npm returned an invalid response",
      502,
      "npm_settings_invalid_response",
    )
  }

  return {
    runtimeStatus: candidate.runtimeStatus,
    runtimeExists: candidate.runtimeExists,
    running: candidate.running,
    runtimeState: typeof candidate.runtimeState === "string" ? candidate.runtimeState : null,
    runtimeImage: typeof candidate.runtimeImage === "string" ? candidate.runtimeImage : null,
    approvedRuntimeImage: candidate.approvedRuntimeImage,
    approvedRuntimeVersion: candidate.approvedRuntimeVersion,
    runtimeImageAligned: candidate.runtimeImageAligned,
    browserUrl: typeof candidate.browserUrl === "string" ? candidate.browserUrl : null,
    administratorEmail: typeof candidate.administratorEmail === "string"
      ? candidate.administratorEmail
      : null,
    credentialStored: candidate.credentialStored,
    credentialStatus: candidate.credentialStatus,
    lastVerifiedAtUtc: typeof candidate.lastVerifiedAtUtc === "string"
      ? candidate.lastVerifiedAtUtc
      : null,
    warning: typeof candidate.warning === "string" ? candidate.warning : null,
  }
}

export async function getOperatorNpmSettings(): Promise<OperatorNpmSettings> {
  const response = await fetch("/api/operator/npm", {
    method: "GET",
    ...npmSettingsRequest,
    headers: { Accept: "application/json" },
  })

  if (!response.ok) {
    return throwProblem(response, "GET /api/operator/npm")
  }

  return parseSettings(await response.json())
}

export async function revealOperatorNpmCredential(): Promise<OperatorNpmCredentialReveal> {
  const response = await fetch("/api/operator/npm/credential/reveal", {
    method: "POST",
    ...npmSettingsRequest,
    headers: { Accept: "application/json" },
  })

  if (!response.ok) {
    return throwProblem(response, "POST /api/operator/npm/credential/reveal")
  }

  const body = (await response.json()) as Partial<OperatorNpmCredentialReveal>
  if (typeof body.administratorEmail !== "string" || typeof body.password !== "string") {
    throw new NpmSettingsProblemError(
      "NPM credential reveal returned an invalid response",
      502,
      "npm_credential_reveal_invalid_response",
    )
  }

  return {
    administratorEmail: body.administratorEmail,
    password: body.password,
  }
}

export async function updateOperatorNpmCredential(
  input: UpdateOperatorNpmCredentialInput,
): Promise<void> {
  const response = await fetch("/api/operator/npm/credential", {
    method: "PUT",
    ...npmSettingsRequest,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify(input),
  })

  if (!response.ok) {
    return throwProblem(response, "PUT /api/operator/npm/credential")
  }
}
