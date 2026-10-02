export type SetupRuntimeMode =
  | "local-development"
  | "containerized-development"
  | "containerized-production"
  | "automated-test"
  | string

export type SetupHostSupportCommands = {
  reportCommand: string | null
  rawLogsCommand: string | null
  containerName: string | null
  reasonUnavailable: string | null
}

export function buildSetupHostSupportCommands(
  runtimeMode: SetupRuntimeMode | null | undefined,
  installationId: string | null | undefined,
): SetupHostSupportCommands {
  const id = installationId?.trim()
  const selector = id
    ? `--installation-id "${id}"`
    : "--latest"

  if (runtimeMode === "containerized-development") {
    return {
      reportCommand:
        `docker exec mem-control-plane-dev \\\n` +
        `  dotnet Api.dll support install-report ${selector} \\\n` +
        `  --format text`,
      rawLogsCommand:
        "docker logs --timestamps --tail 500 mem-control-plane-dev",
      containerName: "mem-control-plane-dev",
      reasonUnavailable: null,
    }
  }

  if (runtimeMode === "containerized-production") {
    return {
      reportCommand:
        `sudo docker exec mem-control-plane \\\n` +
        `  dotnet Api.dll support install-report ${selector} \\\n` +
        `  --format text`,
      rawLogsCommand:
        "sudo docker logs --timestamps --tail 500 mem-control-plane",
      containerName: "mem-control-plane",
      reasonUnavailable: null,
    }
  }

  return {
    reportCommand: null,
    rawLogsCommand: null,
    containerName: null,
    reasonUnavailable:
      "The host-only Docker command is available for containerized MEM runtimes. Use the Setup support-report download while working in this runtime.",
  }
}
