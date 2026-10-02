import { describe, expect, it } from "vitest"

import { buildSetupHostSupportCommands } from "./setup-host-support-command"

describe("buildSetupHostSupportCommands", () => {
  it("uses the development Control Plane container and exact installation selector", () => {
    const commands = buildSetupHostSupportCommands(
      "containerized-development",
      "95f7eebb-0e83-4bab-9916-b503c853caca",
    )

    expect(commands.reportCommand).toContain("docker exec mem-control-plane-dev")
    expect(commands.reportCommand).toContain(
      '--installation-id "95f7eebb-0e83-4bab-9916-b503c853caca"',
    )
    expect(commands.rawLogsCommand).toBe(
      "docker logs --timestamps --tail 500 mem-control-plane-dev",
    )
  })

  it("uses the canonical production Control Plane container", () => {
    const commands = buildSetupHostSupportCommands(
      "containerized-production",
      null,
    )

    expect(commands.reportCommand).toContain(
      "sudo docker exec mem-control-plane",
    )
    expect(commands.reportCommand).toContain("--latest")
    expect(commands.rawLogsCommand).toBe(
      "sudo docker logs --timestamps --tail 500 mem-control-plane",
    )
  })

  it("does not invent a Docker command for local development", () => {
    const commands = buildSetupHostSupportCommands(
      "local-development",
      "installation-1",
    )

    expect(commands.reportCommand).toBeNull()
    expect(commands.rawLogsCommand).toBeNull()
    expect(commands.reasonUnavailable).toMatch(/containerized MEM runtimes/i)
  })
})
