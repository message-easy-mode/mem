import {
  MemApiProblemError,
  parseMemApiProblemText,
} from "@/lib/api-problem"

export type SetupSupportReportFormat = "json" | "text"

export type SetupSupportReportDownload = {
  blob: Blob
  fileName: string
  contentType: string
}

export async function downloadInstallationSupportReport(
  installationId: string,
  options?: {
    includeDockerEvidence?: boolean
    format?: SetupSupportReportFormat
  },
): Promise<SetupSupportReportDownload> {
  const path = `/api/setup/installations/${installationId}/support-report`
  const format = options?.format ?? "json"
  const response = await fetch(path, {
    method: "POST",
    credentials: "include",
    headers: {
      Accept: format === "text" ? "text/plain" : "application/json",
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      includeDockerEvidence: options?.includeDockerEvidence ?? true,
      format,
    }),
  })

  if (!response.ok) {
    const text = await response.text().catch(() => "")
    throw new MemApiProblemError({
      method: "POST",
      path,
      status: response.status,
      problem: parseMemApiProblemText(text),
    })
  }

  const contentType = response.headers.get("content-type") ??
    (format === "text" ? "text/plain" : "application/json")
  const disposition = response.headers.get("content-disposition") ?? ""
  return {
    blob: await response.blob(),
    fileName:
      readFileName(disposition) ??
      `mem-install-report-${installationId}.${format === "text" ? "txt" : "json"}`,
    contentType,
  }
}

export function saveSetupSupportReportDownload(
  download: SetupSupportReportDownload,
) {
  const url = URL.createObjectURL(download.blob)
  const anchor = document.createElement("a")
  anchor.href = url
  anchor.download = download.fileName
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
}

function readFileName(value: string) {
  if (!value) return null

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(value)
  if (utf8Match?.[1]) {
    return decodeURIComponent(utf8Match[1].trim().replace(/^"|"$/g, ""))
  }

  const normalMatch = /filename="?([^";]+)"?/i.exec(value)
  return normalMatch?.[1]?.trim() ?? null
}
