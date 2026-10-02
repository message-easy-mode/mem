import { expect, test, type Page } from "@playwright/test"

import {
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"

type Scenario =
  | "baseline"
  | "host-reboot"
  | "docker-daemon-restart"
  | "stopped-before-startup"
  | "restart-policy-drift"
  | "restart-loop"
  | "nonzero-exit"
  | "wrong-network"
  | "wrong-mount"
  | "missing-container"
  | "foreign-collision"
  | "functional-allocation-failure"
  | "cooldown"

type RuntimePreflight = Readonly<{
  runtimeMode: string
  controlPlaneInstanceId: string
  apiProcessInstanceId: string
  uiDeliveryMode: string
  validationState: string
}>

type DockerRuntime = Readonly<{
  restartPolicy: string
  expectedRestartPolicy: string
  restartPolicyMatches: boolean
  restartCount: number
  restarting: boolean
  paused: boolean
  exitCode: number
  oomKilled: boolean
  dead: boolean
  stateErrorPresent: boolean
  startedAtUtc: string | null
  finishedAtUtc: string | null
  expectedNetwork: string
  attachedNetworks: string[]
  expectedNetworkAttached: boolean
  networkAliasesMatch: boolean
  configMountPresent: boolean
  configMountReadOnly: boolean
  configMountSourceMatches: boolean
  configMountDestinationMatches: boolean
  commandMatches: boolean
  startupUserMatches: boolean
}>

type CoturnRuntime = Readonly<{
  containerName: string
  imageApproved: boolean
  containerExists: boolean
  running: boolean
  ownershipVerified: boolean
  containerId: string | null
  dockerState: string | null
  operatorStatus: string
  runtimeExact: boolean
  configurationPresent: boolean
  configurationExact: boolean
  dockerRuntime: DockerRuntime | null
  runtimeDrift: string[]
  protectedEvidenceAccess: "available" | "restricted" | "unavailable"
  secretPresent: boolean
  secretFilePermissionsApplied: boolean
  domainDriftDetected: boolean
  relayPortsPublished: boolean
  securityPolicyApplied: boolean
}>

type StartupSupervision = Readonly<{
  status:
    | "disabled"
    | "waiting"
    | "deferred"
    | "not-installed"
    | "verified"
    | "recovering"
    | "recovered"
    | "repair-required"
    | "conflict"
    | "cooldown"
    | "failed"
  runtimeMode: string
  enabled: boolean
  decision: string
  mutationPerformed: boolean
  automaticRestartAttempted: boolean
  cooldownActive: boolean
  cooldownUntilUtc: string | null
  operationId: string | null
  observedAtUtc: string
  detail: string | null
}>

type CheckStatus = "passed" | "failed" | "warning" | "not-run"

type CoturnLatestCheck = Readonly<{
  freshness: "fresh" | "stale" | "runtime-changed" | "not-checked" | "unavailable"
  fresh: boolean
  incidentId: string | null
  result: Readonly<{
    status: CheckStatus
    runtimeContainerId: string | null
    runtimeStartedAtUtc: string | null
    checks: ReadonlyArray<Readonly<{
      key: string
      status: CheckStatus
    }>>
    allocation: Readonly<{
      status: CheckStatus
    }>
    incidentId: string | null
  }> | null
}>

const scenario = requiredEnvironmentValue("MEM_E2E_COTURN_RESILIENCE_SCENARIO") as Scenario
const expectedContainerId = optionalEnvironmentValue("MEM_E2E_COTURN_EXPECTED_CONTAINER_ID")
const expectedStartedAt = optionalEnvironmentValue("MEM_E2E_COTURN_EXPECTED_STARTED_AT")
const expectedControlPlaneInstanceId = optionalEnvironmentValue(
  "MEM_E2E_COTURN_EXPECTED_CONTROL_PLANE_INSTANCE_ID",
)
const previousApiProcessInstanceId = optionalEnvironmentValue(
  "MEM_E2E_COTURN_PREVIOUS_API_PROCESS_INSTANCE_ID",
)

test.describe("Coturn resilience release acceptance", () => {
  test("verifies the selected post-mutation lifecycle state without performing host mutation", async ({
    page,
    request,
  }) => {
    test.setTimeout(3 * 60_000)

    const preflightResponse = await request.get("/health/runtime")
    expect(preflightResponse.status()).toBe(200)
    const preflight = (await preflightResponse.json()) as RuntimePreflight

    // This is a deliberately disruptive release-acceptance harness. It is never
    // allowed to target a real production runtime by accident.
    expect(preflight.runtimeMode).toBe("containerized-development")
    expect(preflight.uiDeliveryMode).toBe("embedded-spa")
    expect(preflight.validationState).toBe("valid")

    if (expectedControlPlaneInstanceId) {
      expect(preflight.controlPlaneInstanceId).toBe(expectedControlPlaneInstanceId)
    }
    if (previousApiProcessInstanceId) {
      expect(preflight.apiProcessInstanceId).not.toBe(previousApiProcessInstanceId)
    }

    const operator = readNamedOperatorCredentialsFromEnvironment()
    await signInNamedOperator(page, operator)

    await page.goto("/services/coturn")
    await expect(page.getByRole("heading", { name: "Coturn TURN server" })).toBeVisible()

    const [runtime, startup, latest] = await Promise.all([
      browserGet<CoturnRuntime>(page, "/internal/host-agent/platform/coturn"),
      browserGet<StartupSupervision>(
        page,
        "/internal/host-agent/platform/coturn/startup-supervision",
      ),
      browserGet<CoturnLatestCheck>(page, "/internal/host-agent/platform/coturn/check/latest"),
    ])

    assertNoProtectedMaterial(runtime, startup, latest)
    expect(startup.enabled).toBe(true)
    expect(startup.runtimeMode).toBe("containerized-development")

    switch (scenario) {
      case "baseline":
        expect(startup.status).toBe("verified")
        expect(startup.decision).toBe("ready")
        expect(startup.mutationPerformed).toBe(false)
        expect(startup.automaticRestartAttempted).toBe(false)
        assertExactRunningRuntime(runtime)
        assertFreshUsableFunctionalEvidence(latest, runtime.containerId)
        break

      case "host-reboot":
      case "docker-daemon-restart":
        expect(["verified", "recovered"]).toContain(startup.status)
        assertExactRunningRuntime(runtime)
        assertExpectedContainer(runtime)
        assertFreshUsableFunctionalEvidence(latest, runtime.containerId)
        break

      case "stopped-before-startup":
        expect(startup.status).toBe("recovered")
        expect(startup.decision).toBe("start-stopped")
        expect(startup.mutationPerformed).toBe(true)
        expect(startup.automaticRestartAttempted).toBe(false)
        assertExactRunningRuntime(runtime)
        assertExpectedContainer(runtime)
        assertStartedAtAdvanced(runtime)
        assertFreshUsableFunctionalEvidence(latest, runtime.containerId)
        break

      case "restart-policy-drift":
        expect(startup.status).toBe("recovered")
        expect(startup.decision).toBe("correct-restart-policy")
        expect(startup.mutationPerformed).toBe(true)
        expect(startup.automaticRestartAttempted).toBe(false)
        assertExactRunningRuntime(runtime)
        assertExpectedContainer(runtime)
        expect(runtime.dockerRuntime?.restartPolicy).toBe("unless-stopped")
        expect(runtime.dockerRuntime?.restartPolicyMatches).toBe(true)
        assertStartedAtUnchanged(runtime)
        assertFreshUsableFunctionalEvidence(latest, runtime.containerId)
        break

      case "restart-loop":
        expect(startup.status).toBe("failed")
        expect(startup.decision).toBe("unavailable")
        expect(startup.mutationPerformed).toBe(false)
        expect(startup.automaticRestartAttempted).toBe(false)
        expect(runtime.dockerRuntime).not.toBeNull()
        expect(
          runtime.dockerRuntime!.restarting ||
            runtime.dockerRuntime!.restartCount > 0 ||
            runtime.dockerRuntime!.stateErrorPresent,
        ).toBe(true)
        break

      case "nonzero-exit":
        expect(startup.status).toBe("failed")
        expect(startup.decision).toBe("unavailable")
        expect(startup.mutationPerformed).toBe(false)
        expect(startup.automaticRestartAttempted).toBe(false)
        expect(runtime.dockerRuntime).not.toBeNull()
        expect(runtime.dockerRuntime!.oomKilled || runtime.dockerRuntime!.exitCode !== 0).toBe(true)
        break

      case "wrong-network":
        assertRepairRequiredWithoutMutation(startup)
        expect(
          runtime.runtimeDrift.includes("gateway-network") ||
            runtime.runtimeDrift.includes("network-aliases"),
        ).toBe(true)
        break

      case "wrong-mount":
        assertRepairRequiredWithoutMutation(startup)
        expect(
          runtime.runtimeDrift.includes("config-mount") || runtime.configurationExact === false,
        ).toBe(true)
        break

      case "missing-container":
        assertRepairRequiredWithoutMutation(startup)
        expect(runtime.containerExists).toBe(false)
        expect(runtime.running).toBe(false)
        expect(runtime.secretPresent || runtime.configurationPresent).toBe(true)
        break

      case "foreign-collision":
        expect(startup.status).toBe("conflict")
        expect(startup.decision).toBe("conflict")
        expect(startup.mutationPerformed).toBe(false)
        expect(startup.automaticRestartAttempted).toBe(false)
        expect(runtime.containerExists).toBe(true)
        expect(runtime.ownershipVerified).toBe(false)
        expect(runtime.operatorStatus).toBe("conflict")
        break

      case "functional-allocation-failure":
        expect(startup.status).toBe("failed")
        expect(startup.automaticRestartAttempted).toBe(true)
        expect(startup.mutationPerformed).toBe(true)
        expect(latest.fresh).toBe(true)
        expect(latest.result?.status).toBe("failed")
        expect(latest.result?.allocation.status).toBe("failed")
        break

      case "cooldown":
        expect(startup.status).toBe("cooldown")
        expect(startup.cooldownActive).toBe(true)
        expect(startup.cooldownUntilUtc).not.toBeNull()
        expect(startup.mutationPerformed).toBe(false)
        break

      default:
        assertNever(scenario)
    }

    console.log(
      JSON.stringify({
        proof: "PLATFORM-SERVICES-01F",
        scenario,
        runtimeMode: preflight.runtimeMode,
        controlPlaneInstanceId: preflight.controlPlaneInstanceId,
        apiProcessInstanceId: preflight.apiProcessInstanceId,
        startupStatus: startup.status,
        startupDecision: startup.decision,
        startupOperationId: startup.operationId,
        mutationPerformed: startup.mutationPerformed,
        automaticRestartAttempted: startup.automaticRestartAttempted,
        containerId: runtime.containerId,
        dockerState: runtime.dockerState,
        operatorStatus: runtime.operatorStatus,
        restartPolicy: runtime.dockerRuntime?.restartPolicy ?? null,
        startedAtUtc: runtime.dockerRuntime?.startedAtUtc ?? null,
        runtimeExact: runtime.runtimeExact,
        latestCheckFreshness: latest.freshness,
        latestCheckStatus: latest.result?.status ?? null,
        allocationStatus: latest.result?.allocation.status ?? null,
      }),
    )

    await signOutNamedOperator(page)
  })
})

function assertExactRunningRuntime(runtime: CoturnRuntime) {
  expect(runtime.containerExists).toBe(true)
  expect(runtime.running).toBe(true)
  expect(runtime.ownershipVerified).toBe(true)
  expect(runtime.operatorStatus).toBe("runtime-ready")
  expect(runtime.runtimeExact).toBe(true)
  expect(runtime.protectedEvidenceAccess).toBe("available")
  expect(runtime.imageApproved).toBe(true)
  expect(runtime.configurationPresent).toBe(true)
  expect(runtime.configurationExact).toBe(true)
  expect(runtime.secretPresent).toBe(true)
  expect(runtime.secretFilePermissionsApplied).toBe(true)
  expect(runtime.domainDriftDetected).toBe(false)
  expect(runtime.relayPortsPublished).toBe(true)
  expect(runtime.securityPolicyApplied).toBe(true)
  expect(runtime.dockerRuntime?.expectedNetworkAttached).toBe(true)
  expect(runtime.dockerRuntime?.networkAliasesMatch).toBe(true)
  expect(runtime.dockerRuntime?.configMountPresent).toBe(true)
  expect(runtime.dockerRuntime?.configMountReadOnly).toBe(true)
  expect(runtime.dockerRuntime?.configMountSourceMatches).toBe(true)
  expect(runtime.dockerRuntime?.configMountDestinationMatches).toBe(true)
  expect(runtime.dockerRuntime?.commandMatches).toBe(true)
  expect(runtime.dockerRuntime?.startupUserMatches).toBe(true)
}

function assertRepairRequiredWithoutMutation(startup: StartupSupervision) {
  expect(startup.status).toBe("repair-required")
  expect(startup.decision).toBe("repair-required")
  expect(startup.mutationPerformed).toBe(false)
  expect(startup.automaticRestartAttempted).toBe(false)
}

function assertFreshUsableFunctionalEvidence(
  latest: CoturnLatestCheck,
  expectedRuntimeContainerId: string | null,
) {
  expect(latest.fresh).toBe(true)
  expect(latest.freshness).toBe("fresh")
  expect(latest.result).not.toBeNull()
  expect(["passed", "warning"]).toContain(latest.result!.status)
  expect(latest.result!.allocation.status).toBe("passed")
  expect(latest.result!.runtimeContainerId).toBe(expectedRuntimeContainerId)

  // Warning-level evidence such as automatic external-IP uncertainty is usable
  // and must not resurrect a previous failure Incident.
  if (latest.result!.status === "warning") {
    expect(latest.incidentId).toBeNull()
    expect(latest.result!.incidentId).toBeNull()
  }
}

function assertExpectedContainer(runtime: CoturnRuntime) {
  if (!expectedContainerId) {
    throw new Error(
      `Scenario ${scenario} requires MEM_E2E_COTURN_EXPECTED_CONTAINER_ID for continuity proof.`,
    )
  }
  expect(runtime.containerId).toBe(expectedContainerId)
}

function assertStartedAtAdvanced(runtime: CoturnRuntime) {
  if (!expectedStartedAt) {
    throw new Error(
      `Scenario ${scenario} requires MEM_E2E_COTURN_EXPECTED_STARTED_AT for start-time proof.`,
    )
  }
  expect(runtime.dockerRuntime?.startedAtUtc).not.toBeNull()
  expect(Date.parse(runtime.dockerRuntime!.startedAtUtc!)).toBeGreaterThan(Date.parse(expectedStartedAt))
}

function assertStartedAtUnchanged(runtime: CoturnRuntime) {
  if (!expectedStartedAt) {
    throw new Error(
      `Scenario ${scenario} requires MEM_E2E_COTURN_EXPECTED_STARTED_AT for in-place proof.`,
    )
  }
  expect(runtime.dockerRuntime?.startedAtUtc).toBe(expectedStartedAt)
}

async function browserGet<T>(page: Page, path: string): Promise<T> {
  return page.evaluate(async (requestPath) => {
    const response = await fetch(requestPath, {
      method: "GET",
      credentials: "include",
      headers: { Accept: "application/json" },
    })

    if (!response.ok) {
      throw new Error(`coturn_resilience_get_failed_${response.status}_${requestPath}`)
    }

    return (await response.json()) as T
  }, path)
}

function assertNoProtectedMaterial(...values: unknown[]) {
  const serialized = JSON.stringify(values)
  expect(serialized).not.toMatch(/coturn-secret\.json/i)
  expect(serialized).not.toMatch(/static-auth-secret\s*[:=]/i)
  expect(serialized).not.toMatch(/sharedSecret\s*[:=]/i)
}

function requiredEnvironmentValue(name: string): string {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`Missing required environment value: ${name}`)
  return value
}

function optionalEnvironmentValue(name: string): string | null {
  return process.env[name]?.trim() || null
}

function assertNever(value: never): never {
  throw new Error(`Unsupported Coturn resilience scenario: ${String(value)}`)
}
