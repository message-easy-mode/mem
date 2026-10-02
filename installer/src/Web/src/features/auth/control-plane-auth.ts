export type ControlPlaneSession = {
  authenticated: boolean
  authenticationKind: "operator" | "installer_transition" | null
  displayName: string | null
  roles: string[]
  requiresFirstOwnerBootstrap: boolean
  hasCompletedPlatformOwner: boolean
}

export type BootstrapGrant = {
  expiresAtUtc: string
}

export type FirstOwnerEnrollment = {
  username: string
  manualEntryKey: string
  authenticatorUri: string
}

export type BootstrapCompletion = {
  username: string
  recoveryCodes: string[]
}

export type LoginResult =
  | { status: "authenticated"; session: ControlPlaneSession }
  | { status: "mfa_required"; session?: undefined }
  | { status: "rate_limited"; session?: undefined }
  | { status: "invalid"; session?: undefined }

export type StepUpResult =
  | { status: "verified"; expiresAtUtc: string }
  | { status: "invalid" }
  | { status: "unavailable" }

export type RecoveryCodeRegenerationResult =
  | { status: "regenerated"; recoveryCodes: string[] }
  | { status: "step_up_required" }
  | { status: "unavailable" }

export type PasswordChangeResult =
  | { status: "changed" }
  | { status: "step_up_required" }
  | { status: "confirmation_mismatch" }
  | { status: "must_differ" }
  | { status: "not_accepted" }
  | { status: "unavailable" }

const sensitiveAuthRequest = {
  credentials: "include" as const,
  cache: "no-store" as const,
}

async function readStatus(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { status?: unknown }

    return typeof body.status === "string" ? body.status : null
  } catch {
    return null
  }
}

export async function getControlPlaneSession(): Promise<ControlPlaneSession> {
  const response = await fetch("/api/auth/session", {
    method: "GET",
    ...sensitiveAuthRequest,
  })

  if (!response.ok) {
    throw new Error("control_plane_session_unavailable")
  }

  return response.json() as Promise<ControlPlaneSession>
}

export async function verifyBootstrapCode(token: string): Promise<BootstrapGrant> {
  const response = await fetch("/api/auth/bootstrap/verify", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ token }),
  })

  if (!response.ok) {
    const status = await readStatus(response)
    throw new Error(status ?? "bootstrap_code_not_accepted")
  }

  return response.json() as Promise<BootstrapGrant>
}

export async function prepareFirstOwner(input: {
  username: string
  email: string
  password: string
}): Promise<FirstOwnerEnrollment> {
  const response = await fetch("/api/auth/bootstrap/first-owner", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(input),
  })

  if (!response.ok) {
    const status = await readStatus(response)
    throw new Error(status ?? "bootstrap_owner_unavailable")
  }

  return response.json() as Promise<FirstOwnerEnrollment>
}

export async function verifyBootstrapTotp(code: string): Promise<void> {
  const response = await fetch("/api/auth/bootstrap/totp", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ code }),
  })

  if (!response.ok) {
    const status = await readStatus(response)
    throw new Error(status ?? "invalid_totp")
  }
}

export async function completeBootstrap(): Promise<BootstrapCompletion> {
  const response = await fetch("/api/auth/bootstrap/complete", {
    method: "POST",
    ...sensitiveAuthRequest,
  })

  if (!response.ok) {
    const status = await readStatus(response)
    throw new Error(status ?? "bootstrap_completion_unavailable")
  }

  return response.json() as Promise<BootstrapCompletion>
}

export async function cancelBootstrap(): Promise<void> {
  await fetch("/api/auth/bootstrap/cancel", {
    method: "POST",
    ...sensitiveAuthRequest,
  })
}

export async function loginOperator(
  username: string,
  password: string,
): Promise<LoginResult> {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ username, password }),
  })

  if (!response.ok) {
    const status = await readStatus(response)

    return status === "rate_limited"
      ? { status: "rate_limited" }
      : { status: "invalid" }
  }

  const result = (await response.json()) as {
    status?: unknown
    session?: ControlPlaneSession
  }

  if (result.status === "mfa_required") {
    return { status: "mfa_required" }
  }

  if (result.status === "authenticated" && result.session) {
    return { status: "authenticated", session: result.session }
  }

  return { status: "invalid" }
}

export async function verifyOperatorTotp(code: string): Promise<LoginResult> {
  const response = await fetch("/api/auth/login/totp", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ code }),
  })

  return readMfaCompletionResult(response)
}

export async function verifyOperatorRecoveryCode(code: string): Promise<LoginResult> {
  const response = await fetch("/api/auth/login/recovery-code", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ code }),
  })

  return readMfaCompletionResult(response)
}

async function readMfaCompletionResult(response: Response): Promise<LoginResult> {
  if (!response.ok) {
    const status = await readStatus(response)

    return status === "rate_limited"
      ? { status: "rate_limited" }
      : { status: "invalid" }
  }

  const result = (await response.json()) as {
    status?: unknown
    session?: ControlPlaneSession
  }

  if (result.status === "authenticated" && result.session) {
    return { status: "authenticated", session: result.session }
  }

  return { status: "invalid" }
}

export async function regenerateOperatorRecoveryCodes(): Promise<RecoveryCodeRegenerationResult> {
  const response = await fetch("/api/auth/recovery-codes/regenerate", {
    method: "POST",
    ...sensitiveAuthRequest,
  })

  if (!response.ok) {
    const status = await readStatus(response)

    return status === "step_up_required"
      ? { status: "step_up_required" }
      : { status: "unavailable" }
  }

  const result = (await response.json()) as {
    status?: unknown
    recoveryCodes?: unknown
  }

  if (
    result.status === "recovery_codes_regenerated" &&
    Array.isArray(result.recoveryCodes) &&
    result.recoveryCodes.length > 0 &&
    result.recoveryCodes.every((code) => typeof code === "string" && code.length > 0)
  ) {
    return {
      status: "regenerated",
      recoveryCodes: result.recoveryCodes,
    }
  }

  return { status: "unavailable" }
}

export async function changeOperatorPassword(
  newPassword: string,
  confirmPassword: string,
): Promise<PasswordChangeResult> {
  const response = await fetch("/api/auth/password", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ newPassword, confirmPassword }),
  })

  const status = await readStatus(response)

  if (response.ok && status === "password_changed") {
    return { status: "changed" }
  }

  switch (status) {
    case "step_up_required":
      return { status: "step_up_required" }
    case "password_confirmation_mismatch":
      return { status: "confirmation_mismatch" }
    case "new_password_must_differ":
      return { status: "must_differ" }
    case "new_password_required":
    case "password_not_accepted":
      return { status: "not_accepted" }
    default:
      return { status: "unavailable" }
  }
}

export async function verifyOperatorStepUp(
  password: string,
  code: string,
): Promise<StepUpResult> {
  const response = await fetch("/api/auth/step-up", {
    method: "POST",
    ...sensitiveAuthRequest,
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password, code }),
  })

  if (response.status === 503) {
    return { status: "unavailable" }
  }

  if (!response.ok) {
    return { status: "invalid" }
  }

  const result = (await response.json()) as {
    status?: unknown
    expiresAtUtc?: unknown
  }

  if (
    result.status === "step_up_authenticated" &&
    typeof result.expiresAtUtc === "string"
  ) {
    return {
      status: "verified",
      expiresAtUtc: result.expiresAtUtc,
    }
  }

  return { status: "invalid" }
}

export async function logoutControlPlane(): Promise<void> {
  const response = await fetch("/api/auth/logout", {
    method: "POST",
    ...sensitiveAuthRequest,
  })

  if (!response.ok) {
    throw new Error("logout_unavailable")
  }
}
