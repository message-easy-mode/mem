export type HighRiskStepUpSettings = {
  required: boolean
  reuseVerificationMinutes: number
  allowedReuseVerificationMinutes: number[]
  isDefaulted: boolean
  updatedAtUtc: string | null
  updatedByOperatorId: string | null
}

export type SecuritySettings = {
  highRiskStepUp: HighRiskStepUpSettings
}

export type UpdateHighRiskStepUpSettingsInput = {
  required: boolean
  reuseVerificationMinutes: number
}

const settingsRequest = {
  credentials: "include" as const,
  cache: "no-store" as const,
}

export class SecuritySettingsProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "SecuritySettingsProblemError"
    this.status = status
    this.code = code
  }
}

export function isStepUpRequiredSecuritySettingsProblem(value: unknown): boolean {
  return value instanceof SecuritySettingsProblemError &&
    value.status === 403 &&
    value.code === "step_up_required"
}

async function readProblemCode(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { status?: unknown }

    return typeof body.status === "string" ? body.status : null
  } catch {
    return null
  }
}

async function throwSettingsProblem(
  response: Response,
  operation: string,
): Promise<never> {
  const code = await readProblemCode(response)

  throw new SecuritySettingsProblemError(
    `${operation} failed with status ${response.status}${code ? `: ${code}` : ""}`,
    response.status,
    code,
  )
}

function parseSecuritySettings(value: unknown): SecuritySettings {
  const candidate = value as Partial<SecuritySettings>
  const highRiskStepUp = candidate.highRiskStepUp as Partial<HighRiskStepUpSettings> | undefined

  if (
    typeof highRiskStepUp?.required !== "boolean" ||
    typeof highRiskStepUp.reuseVerificationMinutes !== "number" ||
    !Array.isArray(highRiskStepUp.allowedReuseVerificationMinutes)
  ) {
    throw new SecuritySettingsProblemError(
      "GET /api/security/settings returned an invalid response",
      502,
      "security_settings_invalid_response",
    )
  }

  return {
    highRiskStepUp: {
      required: highRiskStepUp.required,
      reuseVerificationMinutes: highRiskStepUp.reuseVerificationMinutes,
      allowedReuseVerificationMinutes: highRiskStepUp.allowedReuseVerificationMinutes.filter(
        (entry): entry is number => typeof entry === "number",
      ),
      isDefaulted: highRiskStepUp.isDefaulted === true,
      updatedAtUtc: typeof highRiskStepUp.updatedAtUtc === "string"
        ? highRiskStepUp.updatedAtUtc
        : null,
      updatedByOperatorId: typeof highRiskStepUp.updatedByOperatorId === "string"
        ? highRiskStepUp.updatedByOperatorId
        : null,
    },
  }
}

export async function getSecuritySettings(): Promise<SecuritySettings> {
  const response = await fetch("/api/security/settings", {
    method: "GET",
    ...settingsRequest,
    headers: {
      Accept: "application/json",
    },
  })

  if (!response.ok) {
    return throwSettingsProblem(response, "GET /api/security/settings")
  }

  return parseSecuritySettings(await response.json())
}

export async function updateHighRiskStepUpSettings(
  input: UpdateHighRiskStepUpSettingsInput,
): Promise<SecuritySettings> {
  const response = await fetch("/api/security/settings/high-risk-step-up", {
    method: "PATCH",
    ...settingsRequest,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      required: input.required,
      reuseVerificationMinutes: input.reuseVerificationMinutes,
    }),
  })

  if (!response.ok) {
    return throwSettingsProblem(
      response,
      "PATCH /api/security/settings/high-risk-step-up",
    )
  }

  return parseSecuritySettings(await response.json())
}
