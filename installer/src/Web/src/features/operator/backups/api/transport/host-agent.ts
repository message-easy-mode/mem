import type { RuntimeStackBackupExportDownloadResult } from "../types/backups.types";

export const BACKUPS_BASE = "/internal/host-agent/backups";

export const backupsApiRoutes = {
  artifacts: {
    localBackups: `${BACKUPS_BASE}/artifacts/local-backups`,
    portableExports: `${BACKUPS_BASE}/artifacts/portable-exports`,
    validatedImports: `${BACKUPS_BASE}/artifacts/validated-imports`,
  },
  restores: `${BACKUPS_BASE}/restores`,
  verification: {
    privateStaging: `${BACKUPS_BASE}/verification/private-runtime/private-staging`,
  },
  standardRecreate: `${BACKUPS_BASE}/standard-recreate`,
  advancedCutover: {
    retirement: `${BACKUPS_BASE}/advanced-cutover/retirement`,
  },
  catalog: `${BACKUPS_BASE}/catalog`,
} as const;

export type HostAgentProblemArgument = string | number | boolean | null;

export type HostAgentStructuredMessage = Readonly<{
  code: string;
  arguments?: Readonly<Record<string, HostAgentProblemArgument>>;
}>;

export type HostAgentProblem = Readonly<{
  error?: string;
  detail?: string;
  message?: HostAgentStructuredMessage;
}>;

/**
 * Preserves the established transport error string while exposing a validated,
 * optional HostAgent problem descriptor to feature boundaries that opt in.
 */
export class HostAgentProblemError extends Error {
  readonly problem?: HostAgentProblem;

  constructor(message: string, problem?: HostAgentProblem) {
    super(message);
    this.name = "HostAgentProblemError";
    this.problem = problem;
  }
}

export function isHostAgentProblemError(
  value: unknown,
): value is HostAgentProblemError {
  return value instanceof HostAgentProblemError;
}

/**
 * High-risk routes return this stable server-side problem code when the
 * ordinary named session is valid but its short-lived step-up grant is absent
 * or expired. Feature UI can then open the password-plus-current-TOTP dialog
 * and retry only the action the operator already confirmed.
 */
export function isStepUpRequiredHostAgentProblem(value: unknown): boolean {
  return isHostAgentProblemError(value) &&
    value.problem?.error === "step_up_required";
}

async function parseJsonResponse<T>(
  response: Response,
  method: string,
  path: string,
): Promise<T> {
  const contentType = response.headers.get("content-type") ?? "";

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw createHostAgentResponseError(response.status, method, path, text);
  }

  if (!contentType.includes("application/json")) {
    const text = await response.text().catch(() => "");
    throw new Error(
      `${method} ${path} returned non-JSON content (${contentType}). Response started with: ${text.slice(0, 160)}`,
    );
  }

  return (await response.json()) as T;
}

function createHostAgentResponseError(
  status: number,
  method: string,
  path: string,
  text: string,
) {
  return new HostAgentProblemError(
    `${method} ${path} failed with status ${status}${text ? `: ${text}` : ""}`,
    parseHostAgentProblem(text),
  );
}

function parseHostAgentProblem(text: string): HostAgentProblem | undefined {
  if (!text) {
    return undefined;
  }

  try {
    return parseHostAgentProblemValue(JSON.parse(text));
  } catch {
    return undefined;
  }
}

function parseHostAgentProblemValue(value: unknown): HostAgentProblem | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const error = getNonBlankString(value.error);
  const detail = getNonBlankString(value.detail);
  const message = parseHostAgentStructuredMessage(value.message);

  if (!error && !detail && !message) {
    return undefined;
  }

  return {
    ...(error ? { error } : {}),
    ...(detail ? { detail } : {}),
    ...(message ? { message } : {}),
  };
}

function parseHostAgentStructuredMessage(
  value: unknown,
): HostAgentStructuredMessage | undefined {
  if (!isRecord(value)) {
    return undefined;
  }

  const code = getNonBlankString(value.code);
  if (!code) {
    return undefined;
  }

  if (value.arguments === undefined) {
    return { code };
  }

  if (!isRecord(value.arguments)) {
    return undefined;
  }

  const argumentsByName: Record<string, HostAgentProblemArgument> = {};
  for (const [name, argument] of Object.entries(value.arguments)) {
    if (!isHostAgentProblemArgument(argument)) {
      return undefined;
    }

    argumentsByName[name] = argument;
  }

  return {
    code,
    arguments: argumentsByName,
  };
}

function getNonBlankString(value: unknown): string | undefined {
  return typeof value === "string" && value.trim().length > 0
    ? value
    : undefined;
}

function isHostAgentProblemArgument(
  value: unknown,
): value is HostAgentProblemArgument {
  return (
    value === null ||
    typeof value === "string" ||
    typeof value === "number" ||
    typeof value === "boolean"
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

export async function controlPlaneGet<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    method: "GET",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  });

  return parseJsonResponse<T>(response, "GET", path);
}

export async function controlPlaneDelete<TResponse, TRequest = never>(
  path: string,
  body?: TRequest,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "DELETE",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  return parseJsonResponse<TResponse>(response, "DELETE", path);
}

export async function controlPlanePost<TRequest, TResponse>(
  path: string,
  body?: TRequest,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  return parseJsonResponse<TResponse>(response, "POST", path);
}

export async function controlPlaneUpload<TResponse>(
  path: string,
  formData: FormData,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    body: formData,
  });

  return parseJsonResponse<TResponse>(response, "POST", path);
}

export async function controlPlaneDownload(
  path: string,
): Promise<RuntimeStackBackupExportDownloadResult> {
  const response = await fetch(path, {
    method: "GET",
    credentials: "include",
  });

  if (!response.ok) {
    const text = await response.text().catch(() => "");
    throw createHostAgentResponseError(response.status, "GET", path, text);
  }

  const contentType = response.headers.get("content-type") ?? "application/zip";
  const contentDisposition = response.headers.get("content-disposition") ?? "";
  const blob = await response.blob();

  return {
    blob,
    downloadName:
      getDownloadNameFromContentDisposition(contentDisposition) ??
      "mem-stack-export.zip",
    contentType,
  };
}

function getDownloadNameFromContentDisposition(value: string) {
  if (!value) {
    return null;
  }

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(value);
  if (utf8Match?.[1]) {
    return decodeURIComponent(utf8Match[1].trim().replace(/^"|"$/g, ""));
  }

  const normalMatch = /filename="?([^";]+)"?/i.exec(value);
  return normalMatch?.[1]?.trim() ?? null;
}
