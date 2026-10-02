import type { DockerPortBinding } from "@/features/operator/services/api/runtime.types"

export function formatServicePorts(
  ports: DockerPortBinding[] | null | undefined,
): string[] {
  if (!ports?.length) {
    return []
  }

  const formatted = ports
    .filter((port) => port.privatePort && port.privatePort > 0)
    .map((port) => {
      const publicPart =
        port.publicPort && port.publicPort > 0 ? String(port.publicPort) : "-"

      return `${port.privatePort}→${publicPart}`
    })

  return Array.from(new Set(formatted))
}
