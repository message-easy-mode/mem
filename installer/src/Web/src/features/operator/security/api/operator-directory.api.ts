export type ManagedOperator = {
  operatorId: string
  username: string
  email: string | null
  isEnabled: boolean
  isBootstrapProvisioning: boolean
  hasPassword: boolean
  hasTotp: boolean
  roles: string[]
  createdAtUtc: string
  lastLoginAtUtc: string | null
  enrollmentGrantExpiresAtUtc: string | null
  isCurrentOperator: boolean
}

export type OperatorEnrollmentGrant = {
  operatorId: string
  username: string
  enrollmentCode: string
  expiresAtUtc: string
}

export type CreatePendingManagedOperatorInput = {
  username: string
  email: string
  roles: string[]
}

const operatorDirectoryRequest = {
  credentials: "include" as const,
  cache: "no-store" as const,
}

export class OperatorDirectoryProblemError extends Error {
  readonly status: number
  readonly code: string | null

  constructor(message: string, status: number, code: string | null = null) {
    super(message)
    this.name = "OperatorDirectoryProblemError"
    this.status = status
    this.code = code
  }
}

/**
 * A high-risk endpoint uses this stable status only to ask the browser for
 * fresh password-plus-TOTP verification. It does not expose which grant,
 * action details, or policy check caused the refusal.
 */
export function isStepUpRequiredOperatorDirectoryProblem(value: unknown): boolean {
  return value instanceof OperatorDirectoryProblemError &&
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

async function throwDirectoryProblem(
  response: Response,
  operation: string,
): Promise<never> {
  const code = await readProblemCode(response)

  throw new OperatorDirectoryProblemError(
    `${operation} failed with status ${response.status}${code ? `: ${code}` : ""}`,
    response.status,
    code,
  )
}

export async function getManagedOperators(): Promise<ManagedOperator[]> {
  const response = await fetch("/api/security/operators", {
    method: "GET",
    ...operatorDirectoryRequest,
    headers: {
      Accept: "application/json",
    },
  })

  if (!response.ok) {
    return throwDirectoryProblem(response, "GET /api/security/operators")
  }

  return response.json() as Promise<ManagedOperator[]>
}

export async function createPendingManagedOperator(
  input: CreatePendingManagedOperatorInput,
): Promise<ManagedOperator> {
  const response = await fetch("/api/security/operators", {
    method: "POST",
    ...operatorDirectoryRequest,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      username: input.username,
      email: input.email,
      roles: input.roles,
    }),
  })

  if (!response.ok) {
    return throwDirectoryProblem(response, "POST /api/security/operators")
  }

  return response.json() as Promise<ManagedOperator>
}

export async function setManagedOperatorEnabled(
  operatorId: string,
  isEnabled: boolean,
): Promise<ManagedOperator> {
  const response = await fetch(`/api/security/operators/${encodeURIComponent(operatorId)}/enabled`, {
    method: "PUT",
    ...operatorDirectoryRequest,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ isEnabled }),
  })

  if (!response.ok) {
    return throwDirectoryProblem(response, "PUT /api/security/operators/{operatorId}/enabled")
  }

  return response.json() as Promise<ManagedOperator>
}

export async function setManagedOperatorRoles(
  operatorId: string,
  roles: string[],
): Promise<ManagedOperator> {
  const response = await fetch(`/api/security/operators/${encodeURIComponent(operatorId)}/roles`, {
    method: "PUT",
    ...operatorDirectoryRequest,
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({ roles }),
  })

  if (!response.ok) {
    return throwDirectoryProblem(response, "PUT /api/security/operators/{operatorId}/roles")
  }

  return response.json() as Promise<ManagedOperator>
}

export async function revokeManagedOperatorSessions(
  operatorId: string,
): Promise<ManagedOperator> {
  const response = await fetch(
    `/api/security/operators/${encodeURIComponent(operatorId)}/revoke-sessions`,
    {
      method: "POST",
      ...operatorDirectoryRequest,
      headers: {
        Accept: "application/json",
      },
    },
  )

  if (!response.ok) {
    return throwDirectoryProblem(
      response,
      "POST /api/security/operators/{operatorId}/revoke-sessions",
    )
  }

  return response.json() as Promise<ManagedOperator>
}
export async function issueManagedOperatorEnrollmentGrant(
  operatorId: string,
): Promise<OperatorEnrollmentGrant> {
  const response = await fetch(
    `/api/security/operators/${encodeURIComponent(operatorId)}/enrollment-grants`,
    {
      method: "POST",
      ...operatorDirectoryRequest,
      headers: {
        Accept: "application/json",
      },
    },
  )

  if (!response.ok) {
    return throwDirectoryProblem(
      response,
      "POST /api/security/operators/{operatorId}/enrollment-grants",
    )
  }

  return response.json() as Promise<OperatorEnrollmentGrant>
}
