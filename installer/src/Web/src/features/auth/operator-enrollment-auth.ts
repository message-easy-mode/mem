export type OperatorEnrollmentState = {
  active: boolean
  username: string | null
  stage: "password" | "totp" | "recovery" | null
  expiresAtUtc: string | null
}

export type OperatorEnrollmentAuthenticator = {
  username: string
  manualEntryKey: string
  authenticatorUri: string
}

export type OperatorEnrollmentCompletion = {
  username: string
  recoveryCodes: string[]
}

export class OperatorEnrollmentProblemError extends Error {
  readonly code: string | null

  constructor(message: string, code: string | null = null) {
    super(message)
    this.name = "OperatorEnrollmentProblemError"
    this.code = code
  }
}

async function readProblemCode(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { status?: unknown }
    return typeof body.status === "string" ? body.status : null
  } catch {
    return null
  }
}

async function throwEnrollmentProblem(
  response: Response,
  fallbackCode: string,
): Promise<never> {
  const code = await readProblemCode(response)
  throw new OperatorEnrollmentProblemError(
    code ?? fallbackCode,
    code ?? fallbackCode,
  )
}

export async function getOperatorEnrollmentState(): Promise<OperatorEnrollmentState> {
  const response = await fetch("/api/auth/enrollment/state", {
    method: "GET",
    credentials: "include",
    cache: "no-store",
  })

  if (!response.ok) {
    return throwEnrollmentProblem(response, "enrollment_state_unavailable")
  }

  return response.json() as Promise<OperatorEnrollmentState>
}

export async function verifyOperatorEnrollmentCode(
  enrollmentCode: string,
): Promise<OperatorEnrollmentState> {
  const response = await fetch("/api/auth/enrollment/verify", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ enrollmentCode }),
    cache: "no-store",
  })

  if (!response.ok) {
    return throwEnrollmentProblem(response, "enrollment_code_invalid")
  }

  return response.json() as Promise<OperatorEnrollmentState>
}

export async function prepareOperatorEnrollment(
  password: string | null,
): Promise<OperatorEnrollmentAuthenticator> {
  const response = await fetch("/api/auth/enrollment/prepare", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ password }),
    cache: "no-store",
  })

  if (!response.ok) {
    return throwEnrollmentProblem(response, "enrollment_prepare_unavailable")
  }

  return response.json() as Promise<OperatorEnrollmentAuthenticator>
}

export async function verifyOperatorEnrollmentTotp(code: string): Promise<void> {
  const response = await fetch("/api/auth/enrollment/totp", {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ code }),
    cache: "no-store",
  })

  if (!response.ok) {
    return throwEnrollmentProblem(response, "invalid_totp")
  }
}

export async function completeOperatorEnrollment(): Promise<OperatorEnrollmentCompletion> {
  const response = await fetch("/api/auth/enrollment/complete", {
    method: "POST",
    credentials: "include",
    cache: "no-store",
  })

  if (!response.ok) {
    return throwEnrollmentProblem(response, "enrollment_completion_unavailable")
  }

  return response.json() as Promise<OperatorEnrollmentCompletion>
}

export async function cancelOperatorEnrollment(): Promise<void> {
  await fetch("/api/auth/enrollment/cancel", {
    method: "POST",
    credentials: "include",
    cache: "no-store",
  })
}
