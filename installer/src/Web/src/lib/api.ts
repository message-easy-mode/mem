import {
  MemApiProblemError,
  parseMemApiProblemText,
} from "@/lib/api-problem"

async function parseJsonResponse<T>(
  response: Response,
  method: string,
  path: string,
): Promise<T> {
  const contentType = response.headers.get("content-type") ?? ""
  const text = await response.text().catch(() => "")

  if (!response.ok) {
    throw new MemApiProblemError({
      method,
      path,
      status: response.status,
      problem: parseMemApiProblemText(text),
    })
  }

  if (!contentType.includes("application/json")) {
    throw new Error(
      `${method} ${path} returned non-JSON content (${contentType || "not reported"}).`,
    )
  }

  try {
    return JSON.parse(text) as T
  } catch {
    throw new Error(`${method} ${path} returned invalid JSON content.`)
  }
}

export async function getJson<T>(path: string): Promise<T> {
  const response = await fetch(path, {
    method: "GET",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  })

  return parseJsonResponse<T>(response, "GET", path)
}

export async function delJson<TResponse>(path: string): Promise<TResponse> {
  const response = await fetch(path, {
    method: "DELETE",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
  })

  return parseJsonResponse<TResponse>(response, "DELETE", path)
}

export async function postJson<TRequest, TResponse>(
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
  })

  return parseJsonResponse<TResponse>(response, "POST", path)
}

export async function putJson<TRequest, TResponse>(
  path: string,
  body: TRequest,
): Promise<TResponse> {
  const response = await fetch(path, {
    method: "PUT",
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  })

  return parseJsonResponse<TResponse>(response, "PUT", path)
}
