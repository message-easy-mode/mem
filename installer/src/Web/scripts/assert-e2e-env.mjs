const target = process.argv[2]

const supportedTargets = [
  "dev",
  "deployed",
  "diagnostics-dev",
  "diagnostics-deployed",
  "turn-live",
  "coturn-resilience-live",
  "migration-live",
  "local",
  "local-restart",
  "container",
  "container-restart",
]

if (!supportedTargets.includes(target)) {
  console.error(`Usage: node ./scripts/assert-e2e-env.mjs <${supportedTargets.join("|")}>`)
  process.exit(1)
}

const namedOperatorVariables = [
  "MEM_E2E_BASE_URL",
  "MEM_E2E_OPERATOR_USERNAME",
  "MEM_E2E_OPERATOR_PASSWORD",
  "MEM_E2E_OPERATOR_TOTP_SECRET",
]

const parityVariables = [
  "MEM_E2E_BASE_URL",
  "MEM_E2E_AUTH_FILE",
  "MEM_E2E_EXPECTED_RUNTIME_MODE",
  "MEM_E2E_EXPECTED_UI_DELIVERY_MODE",
]

const requiredByTarget = {
  dev: ["MEM_E2E_BASE_URL", "MEM_E2E_SETUP_TOKEN"],
  deployed: namedOperatorVariables,
  "diagnostics-dev": ["MEM_E2E_BASE_URL", "MEM_E2E_SETUP_TOKEN"],
  "diagnostics-deployed": namedOperatorVariables,
  "coturn-resilience-live": [
    ...namedOperatorVariables,
    "MEM_E2E_COTURN_RESILIENCE_SCENARIO",
    "MEM_E2E_COTURN_RESILIENCE_ACK",
  ],
  "turn-live": [
    ...namedOperatorVariables,
    "MEM_E2E_TURN_STACK_SLUG",
    "MEM_E2E_TURN_BASELINE_BACKUP_ID",
    "MEM_E2E_TURN_FINAL_STATE",
    "MEM_E2E_TURN_MUTATION_ACK",
  ],
  "migration-live": [
    ...namedOperatorVariables,
    "MEM_E2E_MIGRATION_EXPECTED_MATRIX_HOST",
    "MEM_E2E_MIGRATION_TARGET_STACK_SLUG",
    "MEM_E2E_MIGRATION_ELEMENT_HOST",
    "MEM_E2E_MIGRATION_PACKAGE_DROP_DIR",
    "MEM_E2E_MIGRATION_MUTATION_ACK",
  ],
  local: [...parityVariables, "MEM_E2E_SETUP_TOKEN"],
  "local-restart": parityVariables,
  container: [...parityVariables, "MEM_E2E_SETUP_TOKEN"],
  "container-restart": parityVariables,
}

const missing = requiredByTarget[target].filter((name) => !process.env[name]?.trim())
if (missing.length > 0) {
  console.error(`MEM ${target} browser smoke tests require: ${missing.join(", ")}.`)
  process.exit(1)
}

const baseUrl = new URL(process.env.MEM_E2E_BASE_URL)

if ((target === "dev" || target === "diagnostics-dev") && baseUrl.port !== "5173") {
  console.error("Dev smoke must target the Vite development server, normally http://localhost:5173.")
  process.exit(1)
}
if ((target === "deployed" || target === "diagnostics-deployed") && baseUrl.port === "5173") {
  console.error("Deployed smoke must target the installed control-plane origin, not Vite on port 5173.")
  process.exit(1)
}

if (target === "local" || target === "local-restart") {
  assertParityTarget("local-development", "vite", "5173")
}
if (target === "container" || target === "container-restart") {
  assertParityTarget("containerized-development", "embedded-spa", "8443")
  if (baseUrl.protocol !== "https:") {
    console.error("Container E2E must target the HTTPS production-shaped Control Plane origin.")
    process.exit(1)
  }
}

if (target === "coturn-resilience-live") {
  const scenarios = new Set([
    "baseline",
    "host-reboot",
    "docker-daemon-restart",
    "stopped-before-startup",
    "restart-policy-drift",
    "restart-loop",
    "nonzero-exit",
    "wrong-network",
    "wrong-mount",
    "missing-container",
    "foreign-collision",
    "functional-allocation-failure",
    "cooldown",
  ])
  const scenario = process.env.MEM_E2E_COTURN_RESILIENCE_SCENARIO.trim()
  if (!scenarios.has(scenario)) {
    console.error(`Unsupported MEM_E2E_COTURN_RESILIENCE_SCENARIO: ${scenario}.`)
    process.exit(1)
  }
  if (process.env.MEM_E2E_COTURN_RESILIENCE_ACK.trim() !== "I_UNDERSTAND_COTURN_RESILIENCE_PROOF_MUTATES_LOCAL_DOCKER") {
    console.error(
      "MEM_E2E_COTURN_RESILIENCE_ACK must be exactly I_UNDERSTAND_COTURN_RESILIENCE_PROOF_MUTATES_LOCAL_DOCKER.",
    )
    process.exit(1)
  }
  if (baseUrl.protocol !== "https:" || baseUrl.port !== "8443") {
    console.error("Coturn resilience acceptance must target the containerized-development HTTPS origin on port 8443.")
    process.exit(1)
  }

  const identityScenarios = new Set([
    "host-reboot",
    "docker-daemon-restart",
    "stopped-before-startup",
    "restart-policy-drift",
  ])
  if (identityScenarios.has(scenario) && !process.env.MEM_E2E_COTURN_EXPECTED_CONTAINER_ID?.trim()) {
    console.error(`${scenario} requires MEM_E2E_COTURN_EXPECTED_CONTAINER_ID.`)
    process.exit(1)
  }

  const startedAtScenarios = new Set(["stopped-before-startup", "restart-policy-drift"])
  if (startedAtScenarios.has(scenario) && !process.env.MEM_E2E_COTURN_EXPECTED_STARTED_AT?.trim()) {
    console.error(`${scenario} requires MEM_E2E_COTURN_EXPECTED_STARTED_AT.`)
    process.exit(1)
  }
}

if (target === "turn-live") {
  const finalState = process.env.MEM_E2E_TURN_FINAL_STATE.trim()
  if (finalState !== "connected" && finalState !== "not-connected") {
    console.error("MEM_E2E_TURN_FINAL_STATE must be exactly connected or not-connected.")
    process.exit(1)
  }
  const acknowledgement = process.env.MEM_E2E_TURN_MUTATION_ACK.trim()
  if (acknowledgement !== "I_UNDERSTAND_TURN_PROOF_MUTATES_STACK") {
    console.error("MEM_E2E_TURN_MUTATION_ACK must be exactly I_UNDERSTAND_TURN_PROOF_MUTATES_STACK.")
    process.exit(1)
  }
}

if (target === "migration-live") {
  const acknowledgement = process.env.MEM_E2E_MIGRATION_MUTATION_ACK.trim()
  if (acknowledgement !== "I_UNDERSTAND_MIGRATION_PROOF_CREATES_AND_PUBLISHES_A_SERVER") {
    console.error("MEM_E2E_MIGRATION_MUTATION_ACK has the wrong acknowledgement value.")
    process.exit(1)
  }
  const stackSlug = process.env.MEM_E2E_MIGRATION_TARGET_STACK_SLUG.trim()
  if (!/^[a-z0-9][a-z0-9-]{0,62}$/.test(stackSlug)) {
    console.error("MEM_E2E_MIGRATION_TARGET_STACK_SLUG must contain only lowercase letters, numbers, and hyphens.")
    process.exit(1)
  }
  for (const name of ["MEM_E2E_MIGRATION_EXPECTED_MATRIX_HOST", "MEM_E2E_MIGRATION_ELEMENT_HOST"]) {
    if (!isHostname(process.env[name].trim().toLowerCase())) {
      console.error(`${name} must be a valid hostname without a scheme or path.`)
      process.exit(1)
    }
  }
  const retentionDays = process.env.MEM_E2E_MIGRATION_RETENTION_DAYS?.trim()
  if (retentionDays && !["7", "14", "21", "30"].includes(retentionDays)) {
    console.error("MEM_E2E_MIGRATION_RETENTION_DAYS must be 7, 14, 21, or 30.")
    process.exit(1)
  }
  const waitMinutes = process.env.MEM_E2E_MIGRATION_PACKAGE_WAIT_MINUTES?.trim()
  if (waitMinutes && !isBoundedInteger(waitMinutes, 1, 120)) {
    console.error("MEM_E2E_MIGRATION_PACKAGE_WAIT_MINUTES must be an integer from 1 to 120.")
    process.exit(1)
  }
}

function assertParityTarget(expectedMode, expectedUi, expectedPort) {
  if (process.env.MEM_E2E_EXPECTED_RUNTIME_MODE.trim() !== expectedMode) {
    console.error(`Expected runtime declaration must be ${expectedMode}.`)
    process.exit(1)
  }
  if (process.env.MEM_E2E_EXPECTED_UI_DELIVERY_MODE.trim() !== expectedUi) {
    console.error(`Expected UI declaration must be ${expectedUi}.`)
    process.exit(1)
  }
  if (baseUrl.port !== expectedPort) {
    console.error(`E2E ${expectedMode} must target port ${expectedPort}.`)
    process.exit(1)
  }
}

function isBoundedInteger(value, minimum, maximum) {
  const parsed = Number(value)
  return Number.isInteger(parsed) && parsed >= minimum && parsed <= maximum
}
function isHostname(value) {
  return value.length <= 253 && value.split(".").every((label) =>
    /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(label),
  )
}
