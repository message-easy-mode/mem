export type CliDeviceAuthorizationReviewResult =
  | {
    status: "authorization_pending"
    deviceLabel: string
    expiresAtUtc: string
  }
  | {
    status:
      | "authorization_expired"
      | "authorization_approved"
      | "authorization_denied"
      | "authorization_consumed"
      | "authorization_unavailable"
      | "unavailable"
  }

export type CliDeviceAuthorizationApproveResult =
  | { status: "authorization_approved" }
  | { status: "step_up_required" }
  | {
    status:
      | "authorization_expired"
      | "authorization_denied"
      | "authorization_consumed"
      | "authorization_unavailable"
      | "unavailable"
  }

export type CliDeviceAuthorizationDenyResult =
  | { status: "authorization_denied" }
  | {
    status:
      | "authorization_expired"
      | "authorization_approved"
      | "authorization_consumed"
      | "authorization_unavailable"
      | "unavailable"
  }

const browserDeviceAuthorizationRequest = {
  credentials: "include" as const,
  cache: "no-store" as const,
  headers: {
    Accept: "application/json",
    "Content-Type": "application/json",
    "X-MEM-Operator-Request": "1",
  },
}

async function readStatus(response: Response): Promise<string | null> {
  try {
    const body = (await response.json()) as { status?: unknown }
    return typeof body.status === "string" ? body.status : null
  } catch {
    return null
  }
}

function isTerminalAuthorizationStatus(value: string): value is
  | "authorization_expired"
  | "authorization_approved"
  | "authorization_denied"
  | "authorization_consumed"
  | "authorization_unavailable" {
  return value === "authorization_expired" ||
    value === "authorization_approved" ||
    value === "authorization_denied" ||
    value === "authorization_consumed" ||
    value === "authorization_unavailable"
}

export async function reviewCliDeviceAuthorization(
  userCode: string,
): Promise<CliDeviceAuthorizationReviewResult> {
  try {
    const response = await fetch("/api/auth/cli-device/authorizations/review", {
      method: "POST",
      ...browserDeviceAuthorizationRequest,
      body: JSON.stringify({ userCode }),
    })

    if (!response.ok) {
      const status = await readStatus(response)
      if (status && isTerminalAuthorizationStatus(status)) {
        return { status }
      }

      return { status: "unavailable" }
    }

    const result = (await response.json()) as {
      status?: unknown
      deviceLabel?: unknown
      expiresAtUtc?: unknown
    }

    if (
      result.status === "authorization_pending" &&
      typeof result.deviceLabel === "string" &&
      result.deviceLabel.length > 0 &&
      typeof result.expiresAtUtc === "string" &&
      result.expiresAtUtc.length > 0
    ) {
      return {
        status: "authorization_pending",
        deviceLabel: result.deviceLabel,
        expiresAtUtc: result.expiresAtUtc,
      }
    }

    const status = typeof result.status === "string" ? result.status : null
    if (status && isTerminalAuthorizationStatus(status)) {
      return { status }
    }

    return { status: "unavailable" }
  } catch {
    return { status: "unavailable" }
  }
}

export async function approveCliDeviceAuthorization(
  userCode: string,
): Promise<CliDeviceAuthorizationApproveResult> {
  try {
    const response = await fetch("/api/auth/cli-device/authorizations/approve", {
      method: "POST",
      ...browserDeviceAuthorizationRequest,
      body: JSON.stringify({ userCode }),
    })

    const status = await readStatus(response)

    if (response.status === 403 && status === "step_up_required") {
      return { status: "step_up_required" }
    }

    if (response.ok && status === "authorization_approved") {
      return { status: "authorization_approved" }
    }

    if (
      status &&
      isTerminalAuthorizationStatus(status) &&
      status !== "authorization_approved"
    ) {
      return { status }
    }

    return { status: "unavailable" }
  } catch {
    return { status: "unavailable" }
  }
}

export async function denyCliDeviceAuthorization(
  userCode: string,
): Promise<CliDeviceAuthorizationDenyResult> {
  try {
    const response = await fetch("/api/auth/cli-device/authorizations/deny", {
      method: "POST",
      ...browserDeviceAuthorizationRequest,
      body: JSON.stringify({ userCode }),
    })

    const status = await readStatus(response)

    if (response.ok && status === "authorization_denied") {
      return { status: "authorization_denied" }
    }

    if (
      status &&
      isTerminalAuthorizationStatus(status) &&
      status !== "authorization_denied"
    ) {
      return { status }
    }

    return { status: "unavailable" }
  } catch {
    return { status: "unavailable" }
  }
}
