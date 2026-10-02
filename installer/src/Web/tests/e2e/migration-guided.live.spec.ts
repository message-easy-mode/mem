import { readdir, stat } from "node:fs/promises"
import { basename, join, resolve } from "node:path"

import { expect, test, type Download, type Page, type Response } from "@playwright/test"

import {
  completeOpenOperatorStepUp,
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
  verifyCurrentOperatorStepUp,
  type NamedOperatorCredentials,
} from "./support/named-operator-auth"

type MigrationSessionInventorySummary = Readonly<{
  totalSessions: number
  activeCount: number
  needsActionCount: number
  completedCount: number
  closedCount: number
  cancelledCount: number
  archivedCount: number
}>

type MigrationSessionInventoryRow = Readonly<{
  migrationId: string
  displayName: string
  lifecycleStatus: string
  currentStageCode: string
  currentStageState: string
  currentStatusCode: string
  needsAttention: boolean
  primaryAction: Readonly<{
    kind: string
    code: string
  }>
}>

type MigrationSessionInventoryResponse = Readonly<{
  summary: MigrationSessionInventorySummary
  totalSessions: number
  sessions: MigrationSessionInventoryRow[]
}>

type MigrationSessionDetail = Readonly<{
  session: Readonly<{
    migrationId: string
    sourceAdapter: string
    sourceDisplay: string
    stackCount: number
  }>
  package: Readonly<{
    status: string
    encryptedSha256: string | null
    archiveMigrationId: string | null
  }> | null
}>

type MigrationWorkspace = Readonly<{
  migration: Readonly<{
    migrationId: string
    displayName: string
    status: string
  }>
  source: Readonly<{
    product: string | null
    productVersion: string | null
    matrixServerName: string | null
  }>
  target: Readonly<{
    stackSlug: string | null
    matrixHost: string | null
    elementHost: string | null
  }>
  overallStatus: Readonly<{
    currentStageCode: string
    code: string
  }>
}>

type ConversionAttempt = Readonly<{
  conversionAttemptId: string
  status: string
  currentStep: string
  candidateArtifactId: string | null
  failureSummary: string | null
}>

type StagingRun = Readonly<{
  stagingRunId: string
  status: string
  currentStep: string
  privateOnly: boolean
  publicRoutesCreated: boolean
  databaseImportSucceeded: boolean
  synapseHealthPassed: boolean
  elementContainerStarted: boolean
  elementHealthPassed: boolean
  elementSynapseConnectivityPassed: boolean
  elementNetworkAttached: boolean
  failureSummary: string | null
}>

type ProductionAdoptionState = Readonly<{
  status: string
  detail: string
  plan: Readonly<{
    targetStackSlug: string
    matrixServerName: string
    matrixPublicHost: string
    elementPublicHost: string
    materialization: Readonly<{
      status: string
      productionDatabaseImported: boolean
      matrixContainerStarted: boolean
      matrixHealthPassed: boolean
      elementContainerStarted: boolean
      elementHealthPassed: boolean
      runtimeManifestSaved: boolean
      databaseOwnershipSaved: boolean
      runtimeRecordsCreated: boolean
      publicRoutesCreated: boolean
      failureSummary: string | null
    }>
    cutover: Readonly<{
      execution: Readonly<{
        status: string
        publicRoutesCreated: boolean
        runtimePromotionCompleted: boolean
        failureSummary: string | null
      }>
    }>
    productionVerification: Readonly<{
      status: string
      passed: boolean
      verificationId: string | null
      checkCount: number
      failedCheckCount: number
      failureSummary: string | null
    }>
  }> | null
}>

type AcceptanceState = Readonly<{
  accepted: boolean
  acceptanceEligible: boolean
  baselineBackup: Readonly<{
    status: string
    backupId: string | null
    catalogEntryId: string | null
    targetStackSlug: string
    failureSummary: string | null
  }> | null
}>

type CompletionReport = Readonly<{
  schemaVersion: string
  payloadSha256: string
  payload: Readonly<{
    reportId: string
    migrationId: string
    targetStackSlug: string
    baselineBackup: Readonly<{
      backupId: string | null
      catalogEntryId: string | null
      status: string
    }>
  }>
}>

const expectedMatrixHost = requiredEnvironmentValue(
  "MEM_E2E_MIGRATION_EXPECTED_MATRIX_HOST",
).toLowerCase()
const targetStackSlug = requiredEnvironmentValue(
  "MEM_E2E_MIGRATION_TARGET_STACK_SLUG",
)
const targetElementHost = requiredEnvironmentValue(
  "MEM_E2E_MIGRATION_ELEMENT_HOST",
).toLowerCase()
const packageDropDirectory = resolve(requiredEnvironmentValue(
  "MEM_E2E_MIGRATION_PACKAGE_DROP_DIR",
))
const retentionDays = parseRetentionDays(
  process.env.MEM_E2E_MIGRATION_RETENTION_DAYS?.trim() || "14",
)
const packageWaitMinutes = parseBoundedInteger(
  process.env.MEM_E2E_MIGRATION_PACKAGE_WAIT_MINUTES?.trim() || "30",
  1,
  120,
  "MEM_E2E_MIGRATION_PACKAGE_WAIT_MINUTES",
)
const displayName = process.env.MEM_E2E_MIGRATION_DISPLAY_NAME?.trim() ||
  `MIG UX E2E ${new Date().toISOString().replaceAll(/[:.]/g, "-")}`

if (
  requiredEnvironmentValue("MEM_E2E_MIGRATION_MUTATION_ACK") !==
  "I_UNDERSTAND_MIGRATION_PROOF_CREATES_AND_PUBLISHES_A_SERVER"
) {
  throw new Error("migration_live_mutation_ack_invalid")
}

if (!/^[a-z0-9][a-z0-9-]{0,62}$/.test(targetStackSlug)) {
  throw new Error("migration_live_target_stack_slug_invalid")
}

if (!isHostname(expectedMatrixHost) || !isHostname(targetElementHost)) {
  throw new Error("migration_live_expected_host_invalid")
}

test.describe("guided live Migration journey", () => {
  // A retry would create a second server or repeat a public mutation. This live
  // proof must stop at the first failure and require operator assessment.
  test.describe.configure({ mode: "serial", retries: 0 })

  test("proves secure intake through accepted baseline Backup Catalog handoff", async ({ page }) => {
    test.setTimeout((packageWaitMinutes + 45) * 60_000)

    const operator = readNamedOperatorCredentialsFromEnvironment()
    const startedAt = Date.now()

    await assertPackageDropIsReady(packageDropDirectory)
    await signInNamedOperator(page, operator)

    const inventoryBefore = await browserGetJson<MigrationSessionInventoryResponse>(
      page,
      migrationInventoryPath(),
    )
    const countsBefore = inventoryBefore.summary
    logCheckpoint("session-baseline", { sessionCountsBefore: countsBefore })

    await openMigrationList(page)
    await expect(page.getByRole("link", { name: "Start migration" })).toBeVisible()

    const migrationId = await createSecureSession(page, operator)
    await expect(page).toHaveURL(
      new RegExp(`/migrations/${escapeRegExp(migrationId)}$`),
      { timeout: 15_000 },
    )

    const packagePath = await createSourcePackageHandoff(
      page,
      migrationId,
      packageDropDirectory,
      startedAt,
      packageWaitMinutes * 60_000,
    )

    await uploadMigrationPackage(page, operator, migrationId, packagePath)
    await proveSourceReview(page, migrationId)
    await proveReentryAndAdvancedBoundary(page, migrationId)

    const conversion = await prepareMigrationData(page, migrationId)
    logCheckpoint("conversion-completed", {
      migrationId,
      conversionAttemptId: conversion.conversionAttemptId,
      candidateArtifactId: conversion.candidateArtifactId,
    })

    const staging = await runPrivateTest(page, operator, migrationId)
    logCheckpoint("private-test-verified", {
      migrationId,
      stagingRunId: staging.stagingRunId,
    })

    await refreshWorkspace(page, migrationId)
    await createPrivateNormalServer(page, operator, migrationId)
    logCheckpoint("private-normal-server-created", { migrationId, targetStackSlug })

    await refreshWorkspace(page, migrationId)
    const production = await makeServerLive(page, migrationId)
    logCheckpoint("public-verification-passed", {
      migrationId,
      productionVerificationId: production.plan?.productionVerification.verificationId,
      productionCheckCount: production.plan?.productionVerification.checkCount,
    })

    await refreshWorkspace(page, migrationId)
    const acceptance = await finishMigration(page, operator, migrationId)
    logCheckpoint("accepted-baseline-created", {
      migrationId,
      baselineBackupId: acceptance.baselineBackup?.backupId,
      catalogEntryId: acceptance.baselineBackup?.catalogEntryId,
    })

    const report = await downloadAndInspectCompletionReport(
      page,
      operator,
      migrationId,
      acceptance,
    )

    await openBaselineBackup(page, acceptance)
    await proveCompletedSessionReentry(page, migrationId)

    const inventoryAfter = await browserGetJson<MigrationSessionInventoryResponse>(
      page,
      migrationInventoryPath(),
    )
    const countsAfter = inventoryAfter.summary
    const completedInventory = await browserGetJson<MigrationSessionInventoryResponse>(
      page,
      migrationInventoryPath(migrationId),
    )
    const completed = completedInventory.sessions.find(
      (session) => session.migrationId === migrationId,
    )

    expect(completed).toMatchObject({
      displayName,
      lifecycleStatus: "completed",
      currentStageCode: "finish-migration",
      currentStageState: "completed",
      currentStatusCode: "migration.workspace.completed",
      needsAttention: false,
      primaryAction: {
        kind: "view",
        code: "open-baseline-backup",
      },
    })

    console.log(JSON.stringify({
      proof: "MIG-UX-E2E-01",
      migrationId,
      displayName,
      sourceMatrixHost: expectedMatrixHost,
      sourcePackageFile: basename(packagePath),
      conversionAttemptId: conversion.conversionAttemptId,
      candidateArtifactId: conversion.candidateArtifactId,
      stagingRunId: staging.stagingRunId,
      targetStackSlug,
      elementHost: targetElementHost,
      productionVerificationId: production.plan?.productionVerification.verificationId,
      productionCheckCount: production.plan?.productionVerification.checkCount,
      baselineBackupId: acceptance.baselineBackup?.backupId,
      catalogEntryId: acceptance.baselineBackup?.catalogEntryId,
      completionReportId: report.payload.reportId,
      completionReportSha256: report.payloadSha256,
      sessionCountsBefore: countsBefore,
      sessionCountsAfter: countsAfter,
    }, null, 2))

    await signOutNamedOperator(page)
  })
})

async function openMigrationList(page: Page) {
  const responsePromise = page.waitForResponse(
    (response) => isResponse(response, "GET", "/api/operator/migrations/sessions/inventory"),
    { timeout: 30_000 },
  )

  await page.goto("/migrations")
  await expect(page.getByRole("heading", { name: "Migrations", exact: true })).toBeVisible()
  expect((await responsePromise).status()).toBe(200)
  await expect(page.getByText("Migration sessions", { exact: true })).toBeVisible()
}

async function createSecureSession(
  page: Page,
  operator: NamedOperatorCredentials,
): Promise<string> {
  await page.getByRole("link", { name: "Start migration" }).click()
  await expect(page.getByRole("heading", { name: "Migration intake" })).toBeVisible()
  await expect(page.getByText("Start a secure migration", { exact: true })).toBeVisible()

  await page.getByLabel("Migration name").fill(displayName)

  const response = await performStepUpAwareAction(
    page,
    operator,
    () => page.getByRole("button", { name: "Create secure intake" }).click(),
    (candidate) => isResponse(candidate, "POST", "/api/operator/migrations/secure-intakes"),
    60_000,
  )

  const body = await readJsonResponse<{ intakeId: string; status: string }>(response)
  expect(body.status).toBe("awaiting-package")
  expect(body.intakeId).toMatch(/^mig_[A-Za-z0-9_-]+$/)
  return body.intakeId
}

async function createSourcePackageHandoff(
  page: Page,
  migrationId: string,
  dropDirectory: string,
  startedAt: number,
  timeoutMilliseconds: number,
): Promise<string> {
  const packageStage = await openStage(page, "create-and-upload-package")
  await packageStage.getByRole("button", { name: "Show command" }).click()

  const command = (await packageStage.getByLabel("Generated source command").innerText()).trim()
  expect(command).toContain(`--intake-id '${migrationId}'`)
  expect(command).toContain("--age-recipient 'age1")
  expect(command).toContain("--recipient-fingerprint '")
  expect(command).not.toContain("AGE-SECRET-KEY-")
  expect(command).not.toContain("--archive")
  expect(command).not.toContain("--output-directory")

  console.log(
    "\n[MIG-UX-E2E-01] SOURCE PACKAGE HANDOFF\n" +
    "Run this command on the approved MEM 0.1.0 source server:\n\n" +
    `${command}\n\n` +
    `Copy the generated .memmigration.zip.age file into this empty directory on the browser-test host:\n${dropDirectory}\n` +
    `The proof will wait for up to ${packageWaitMinutes} minute(s).\n`,
  )

  const packagePath = await waitForStablePackage(
    page,
    dropDirectory,
    startedAt,
    timeoutMilliseconds,
  )

  console.log(JSON.stringify({
    proof: "MIG-UX-E2E-01",
    checkpoint: "source-package-received",
    migrationId,
    fileName: basename(packagePath),
  }))

  return packagePath
}

async function uploadMigrationPackage(
  page: Page,
  operator: NamedOperatorCredentials,
  migrationId: string,
  packagePath: string,
) {
  const packageStage = await openStage(page, "create-and-upload-package")
  await packageStage.getByLabel("Encrypted migration package").setInputFiles(packagePath)

  const response = await performStepUpAwareAction(
    page,
    operator,
    () => packageStage.getByRole("button", { name: "Upload package" }).click(),
    (candidate) => isResponse(
      candidate,
      "POST",
      `/api/operator/migrations/secure-intakes/${encodeURIComponent(migrationId)}/package`,
    ),
    10 * 60_000,
  )

  const uploaded = await readJsonResponse<{ status: string; archiveStackCount: number }>(response)
  expect(uploaded.status).toBe("package-validated")
  expect(uploaded.archiveStackCount).toBe(1)

  await expect(
    packageStage.getByText("Migration package received and verified", { exact: true }),
  ).toBeVisible({ timeout: 30_000 })
}

async function proveSourceReview(page: Page, migrationId: string) {
  const reviewStage = await openStage(page, "review-old-server")
  await expect(
    reviewStage.getByText("This server is supported for migration", { exact: true }),
  ).toBeVisible()

  const detail = await browserGetJson<MigrationSessionDetail>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}`,
  )
  expect(detail.session.sourceAdapter).toBe("mem-v010")
  expect(detail.session.stackCount).toBe(1)
  expect(detail.package?.status).toBe("package-validated")
  expect(detail.package?.encryptedSha256).toMatch(/^[A-Fa-f0-9]{64}$/)

  const workspace = await browserGetJson<MigrationWorkspace>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/workspace`,
  )
  expect(workspace.source.matrixServerName?.toLowerCase()).toBe(expectedMatrixHost)
  expect(workspace.source.productVersion).toBe("0.1.0")
}

async function proveReentryAndAdvancedBoundary(page: Page, migrationId: string) {
  await page.reload()
  await expect(page).toHaveURL(
    new RegExp(`/migrations/${escapeRegExp(migrationId)}$`),
    { timeout: 15_000 },
  )
  await expect(page.getByRole("tab", { name: "Guide", exact: true })).toHaveAttribute(
    "aria-selected",
    "true",
  )
  await expect(page.getByText("Advanced and recovery tools", { exact: true })).toHaveCount(0)

  await page.getByRole("tab", { name: "Advanced", exact: true }).click()
  await expect(page.getByText("Advanced and recovery tools", { exact: true })).toBeVisible()
  await expect(page.getByText("Migration lifecycle", { exact: true })).toBeVisible()
  await expect(
    page.getByText(
      "High-assurance option: freeze the source and create a final package",
      { exact: true },
    ),
  ).toBeVisible()
  await expect(page.getByRole("button", { name: "Prepare migration data" })).toHaveCount(0)

  await page.getByRole("tab", { name: "Guide", exact: true }).click()
  await expect(page.getByText("Advanced and recovery tools", { exact: true })).toHaveCount(0)
}

async function prepareMigrationData(
  page: Page,
  migrationId: string,
): Promise<ConversionAttempt> {
  const stage = await openStage(page, "prepare-and-test")
  const responsePromise = page.waitForResponse(
    (response) => isResponse(
      response,
      "POST",
      `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/conversion-attempts`,
    ),
    { timeout: 60_000 },
  )

  await stage.getByRole("button", { name: "Prepare migration data" }).click()
  expectSuccessfulResponse(await responsePromise)

  const conversion = await pollJson<ConversionAttempt[], ConversionAttempt>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/conversion-attempts`,
    20 * 60_000,
    (attempts) => {
      const latest = attempts[0]
      if (!latest) return { state: "waiting" }
      if (["failed", "cancelled"].includes(latest.status)) {
        return {
          state: "failed",
          detail: latest.failureSummary ?? `${latest.status}: ${latest.currentStep}`,
        }
      }
      if (
        ["completed", "completed-with-warnings"].includes(latest.status) &&
        latest.candidateArtifactId
      ) {
        return { state: "complete", value: latest }
      }
      return { state: "waiting" }
    },
  )

  await expect(stage.getByText("Migration data prepared", { exact: true })).toBeVisible({
    timeout: 30_000,
  })
  return conversion
}

async function runPrivateTest(
  page: Page,
  operator: NamedOperatorCredentials,
  migrationId: string,
): Promise<StagingRun> {
  const stage = await openStage(page, "prepare-and-test")
  const response = await performStepUpAwareAction(
    page,
    operator,
    () => stage.getByRole("button", { name: "Run private test" }).click(),
    (candidate) => isResponse(
      candidate,
      "POST",
      `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/staging-runs`,
    ),
    90_000,
  )
  expectSuccessfulResponse(response)

  const staging = await pollJson<StagingRun[], StagingRun>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/staging-runs`,
    20 * 60_000,
    (runs) => {
      const latest = runs[0]
      if (!latest) return { state: "waiting" }
      if (["failed", "failed-cleaned"].includes(latest.status)) {
        return {
          state: "failed",
          detail: latest.failureSummary ?? `${latest.status}: ${latest.currentStep}`,
        }
      }
      if (latest.status === "verified") return { state: "complete", value: latest }
      return { state: "waiting" }
    },
  )

  expect(staging.privateOnly).toBe(true)
  expect(staging.publicRoutesCreated).toBe(false)
  expect(staging.databaseImportSucceeded).toBe(true)
  expect(staging.synapseHealthPassed).toBe(true)
  expect(staging.elementContainerStarted).toBe(true)
  expect(staging.elementHealthPassed).toBe(true)
  expect(staging.elementSynapseConnectivityPassed).toBe(true)
  expect(staging.elementNetworkAttached).toBe(true)

  await expect(stage.getByText("Private test passed", { exact: true })).toBeVisible({
    timeout: 30_000,
  })
  return staging
}

async function createPrivateNormalServer(
  page: Page,
  operator: NamedOperatorCredentials,
  migrationId: string,
) {
  const stage = await openStage(page, "create-new-server")

  await stage.locator("#migration-stack-name").fill(targetStackSlug)
  await stage.locator("#migration-element-address").fill(targetElementHost)
  await stage.getByRole("button", { name: "Check target availability" }).click()
  await expect(stage.getByRole("button", { name: "Create new server" })).toBeEnabled()
  await stage.getByRole("button", { name: "Create new server" }).click()

  const dialog = page.getByRole("alertdialog", { name: "Create the new server privately?" })
  await expect(dialog).toBeVisible()

  await performStepUpAwareAction(
    page,
    operator,
    () => dialog.getByRole("button", { name: "Create new server" }).click(),
    (candidate) => isResponse(
      candidate,
      "POST",
      `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/production-adoption/private-server`,
    ),
    20 * 60_000,
  )

  const adoption = await pollJson<ProductionAdoptionState, ProductionAdoptionState>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/production-adoption`,
    20 * 60_000,
    (state) => {
      const materialization = state.plan?.materialization
      if (state.status === "materialization-failed" || materialization?.status === "failed") {
        return {
          state: "failed",
          detail: materialization?.failureSummary ?? state.detail,
        }
      }
      if (state.plan && hasPrivateRuntime(state) && !state.plan.materialization.publicRoutesCreated) {
        return { state: "complete", value: state }
      }
      return { state: "waiting" }
    },
  )

  expect(adoption.plan?.targetStackSlug).toBe(targetStackSlug)
  expect(adoption.plan?.matrixServerName.toLowerCase()).toBe(expectedMatrixHost)
  expect(adoption.plan?.elementPublicHost.toLowerCase()).toBe(targetElementHost)
  await expect(stage.getByText("Private server is ready", { exact: true })).toBeVisible({
    timeout: 30_000,
  })
}

async function makeServerLive(
  page: Page,
  migrationId: string,
): Promise<ProductionAdoptionState> {
  const stage = await openStage(page, "make-new-server-live")
  const makeLiveButton = stage.getByRole("button", { name: "Make server live" })
  await expect(makeLiveButton).toBeEnabled({ timeout: 2 * 60_000 })
  await makeLiveButton.click()

  const dialog = page.getByRole("alertdialog", { name: "Make the new server live?" })
  await expect(dialog).toBeVisible()

  const responsePromise = page.waitForResponse(
    (response) => isResponse(
      response,
      "POST",
      `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/production-adoption/go-live`,
    ),
    { timeout: 20 * 60_000 },
  )
  await dialog.getByRole("button", { name: "Make server live" }).click()
  expectSuccessfulResponse(await responsePromise)

  const adoption = await pollJson<ProductionAdoptionState, ProductionAdoptionState>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/production-adoption`,
    20 * 60_000,
    (state) => {
      const plan = state.plan
      const execution = plan?.cutover.execution
      const verification = plan?.productionVerification
      if (execution?.status === "failed") {
        return {
          state: "failed",
          detail: execution.failureSummary ?? state.detail,
        }
      }
      if (verification?.status === "failed") {
        return {
          state: "failed",
          detail: verification.failureSummary ?? state.detail,
        }
      }
      if (
        plan &&
        execution?.publicRoutesCreated &&
        execution.runtimePromotionCompleted &&
        verification?.status === "passed" &&
        verification.passed
      ) {
        return { state: "complete", value: state }
      }
      return { state: "waiting" }
    },
  )

  expect(adoption.plan?.productionVerification.failedCheckCount).toBe(0)
  await expect(stage.getByText("Live verification passed", { exact: true })).toBeVisible({
    timeout: 30_000,
  })
  return adoption
}

async function finishMigration(
  page: Page,
  operator: NamedOperatorCredentials,
  migrationId: string,
): Promise<AcceptanceState> {
  const stage = await openStage(page, "finish-migration")
  await expect(stage.getByText("New server is live and verified", { exact: true })).toBeVisible()
  await stage.locator("#migration-finish-retention-days").selectOption(String(retentionDays))
  await expect(stage.getByText("The verified new server becomes the authoritative server.")).toBeVisible()
  await expect(stage.getByText("New changes are not synchronized back to the old server.")).toBeVisible()
  await expect(stage.getByText("MEM will retain the old server for the selected period and will not delete it automatically.")).toBeVisible()

  await performStepUpAwareAction(
    page,
    operator,
    () => stage.getByRole("button", { name: "Finish migration" }).click(),
    (candidate) => isResponse(
      candidate,
      "POST",
      `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/acceptance/finish`,
    ),
    10 * 60_000,
  )

  const acceptance = await pollJson<AcceptanceState, AcceptanceState>(
    page,
    `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/acceptance`,
    15 * 60_000,
    (state) => {
      if (state.accepted && state.baselineBackup?.status === "failed") {
        return {
          state: "failed",
          detail: state.baselineBackup.failureSummary ?? "baseline backup failed",
        }
      }
      if (
        state.accepted &&
        state.baselineBackup?.status === "created" &&
        state.baselineBackup.backupId &&
        state.baselineBackup.catalogEntryId
      ) {
        return { state: "complete", value: state }
      }
      return { state: "waiting" }
    },
  )

  expect(acceptance.baselineBackup?.targetStackSlug).toBe(targetStackSlug)
  await expect(stage.getByText("Migration handoff completed", { exact: true })).toBeVisible({
    timeout: 30_000,
  })
  return acceptance
}

async function downloadAndInspectCompletionReport(
  page: Page,
  operator: NamedOperatorCredentials,
  migrationId: string,
  acceptance: AcceptanceState,
): Promise<CompletionReport> {
  // Report generation is a high-risk evidence export. Refresh the operator's
  // short-lived step-up grant immediately before the browser download.
  await verifyCurrentOperatorStepUp(page, operator)

  const downloadPromise = page.waitForEvent("download", { timeout: 60_000 })
  await page.getByRole("button", { name: "Download completion report" }).click()
  const download = await downloadPromise

  expect(download.suggestedFilename()).toMatch(/migration.*completion.*\.json$/i)
  const report = JSON.parse(await readDownloadText(download)) as CompletionReport

  expect(report.schemaVersion).toBe("mem.migration.acceptance-completion.v1")
  expect(report.payloadSha256).toMatch(/^[A-Fa-f0-9]{64}$/)
  expect(report.payload.migrationId).toBe(migrationId)
  expect(report.payload.targetStackSlug).toBe(targetStackSlug)
  expect(report.payload.baselineBackup.status).toBe("created")
  expect(report.payload.baselineBackup.backupId).toBe(acceptance.baselineBackup?.backupId)
  expect(report.payload.baselineBackup.catalogEntryId).toBe(
    acceptance.baselineBackup?.catalogEntryId,
  )

  return report
}

async function openBaselineBackup(page: Page, acceptance: AcceptanceState) {
  const catalogEntryId = acceptance.baselineBackup?.catalogEntryId
  if (!catalogEntryId) throw new Error("migration_live_catalog_entry_missing")

  const link = page.getByRole("link", { name: "Open baseline backup" })
  await expect(link).toHaveAttribute(
    "href",
    `/backups/catalog/${encodeURIComponent(catalogEntryId)}`,
  )
  await link.click()

  await expect(page).toHaveURL(
    new RegExp(`/backups/catalog/${escapeRegExp(catalogEntryId)}$`),
    { timeout: 15_000 },
  )
  await expect(page.getByText("Backup identity", { exact: true })).toBeVisible()
  await expect(page.getByText("Restore readiness", { exact: true })).toBeVisible()
}

async function proveCompletedSessionReentry(page: Page, migrationId: string) {
  await openMigrationList(page)
  const row = page.locator("tbody tr").filter({ hasText: displayName })
  await expect(row).toHaveCount(1)
  await expect(row.getByText("Migration completed", { exact: true })).toBeVisible()
  await expect(row.getByText("No action required", { exact: true })).toBeVisible()

  await row.getByRole("link", { name: "Open migration" }).click()
  await expect(page).toHaveURL(
    new RegExp(`/migrations/${escapeRegExp(migrationId)}$`),
    { timeout: 15_000 },
  )
  await expect(page.getByText("Migration handoff completed", { exact: true })).toBeVisible()
}

async function refreshWorkspace(page: Page, migrationId: string) {
  const responsePromise = page.waitForResponse(
    (response) => isResponse(
      response,
      "GET",
      `/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/workspace`,
    ),
    { timeout: 30_000 },
  )
  const button = page.getByRole("button", { name: "Refresh", exact: true }).first()
  await button.click()
  expectSuccessfulResponse(await responsePromise)
  await expect(button).toBeEnabled({ timeout: 30_000 })
}

async function openStage(page: Page, stageCode: string) {
  const button = page.locator(`button[aria-controls="migration-stage-${stageCode}"]`)
  await expect(button).toBeVisible({ timeout: 30_000 })
  await expect(button).toBeEnabled()

  if ((await button.getAttribute("aria-expanded")) !== "true") {
    await button.click()
  }

  const content = page.locator(`#migration-stage-${stageCode}`)
  await expect(content).toBeVisible()
  return content
}

async function performStepUpAwareAction(
  page: Page,
  operator: NamedOperatorCredentials,
  action: () => Promise<unknown>,
  predicate: (response: Response) => boolean,
  timeout: number,
): Promise<Response> {
  const firstResponsePromise = page.waitForResponse(predicate, { timeout })
  await action()
  let response = await firstResponsePromise

  if (response.status() !== 403) {
    expectSuccessfulResponse(response)
    return response
  }

  const problem = await readProblem(response)
  expect(problem).toBe("step_up_required")

  const retryResponsePromise = page.waitForResponse(predicate, { timeout })
  await completeOpenOperatorStepUp(page, operator)
  response = await retryResponsePromise
  expectSuccessfulResponse(response)
  return response
}

async function browserGetJson<T>(page: Page, path: string): Promise<T> {
  return await page.evaluate(async (requestPath) => {
    const response = await fetch(requestPath, {
      method: "GET",
      credentials: "include",
      cache: "no-store",
      headers: { Accept: "application/json" },
    })

    const body = await response.text()
    if (!response.ok) {
      throw new Error(`GET ${requestPath} failed with status ${response.status}: ${body}`)
    }

    return JSON.parse(body) as T
  }, path)
}

type PollResult<T> =
  | Readonly<{ state: "waiting" }>
  | Readonly<{ state: "failed"; detail: string }>
  | Readonly<{ state: "complete"; value: T }>

async function pollJson<TResponse, TResult>(
  page: Page,
  path: string,
  timeout: number,
  inspect: (response: TResponse) => PollResult<TResult>,
): Promise<TResult> {
  const deadline = Date.now() + timeout
  let lastState = "no response"

  while (Date.now() < deadline) {
    const response = await browserGetJson<TResponse>(page, path)
    const result = inspect(response)

    if (result.state === "complete") return result.value
    if (result.state === "failed") throw new Error(result.detail)

    lastState = JSON.stringify(response).slice(0, 500)
    await page.waitForTimeout(2_000)
  }

  throw new Error(`Timed out waiting for ${path}. Last state: ${lastState}`)
}

async function assertPackageDropIsReady(directory: string) {
  const directoryStat = await stat(directory)
  if (!directoryStat.isDirectory()) {
    throw new Error("migration_live_package_drop_not_directory")
  }

  const packages = (await readdir(directory)).filter(isMigrationPackageName)
  if (packages.length > 0) {
    throw new Error(
      `migration_live_package_drop_not_empty: ${packages.join(", ")}`,
    )
  }
}

async function waitForStablePackage(
  page: Page,
  directory: string,
  startedAt: number,
  timeout: number,
): Promise<string> {
  const deadline = Date.now() + timeout

  while (Date.now() < deadline) {
    const candidates = await findNewPackages(directory, startedAt)
    if (candidates.length > 1) {
      throw new Error(
        `migration_live_multiple_packages_found: ${candidates.map(basename).join(", ")}`,
      )
    }

    if (candidates.length === 1) {
      const candidate = candidates[0]!
      const first = await stat(candidate)
      await page.waitForTimeout(2_000)
      const second = await stat(candidate)

      if (first.size > 0 && first.size === second.size && second.mtimeMs >= startedAt - 1_000) {
        return candidate
      }
    }

    await page.waitForTimeout(2_000)
  }

  throw new Error(`migration_live_package_wait_timed_out: ${directory}`)
}

async function findNewPackages(directory: string, startedAt: number) {
  const entries = await readdir(directory)
  const candidates: string[] = []

  for (const entry of entries.filter(isMigrationPackageName)) {
    const candidate = join(directory, entry)
    const candidateStat = await stat(candidate)
    if (candidateStat.isFile() && candidateStat.mtimeMs >= startedAt - 1_000) {
      candidates.push(candidate)
    }
  }

  return candidates.sort()
}

function isMigrationPackageName(value: string) {
  return value.endsWith(".memmigration.zip.age")
}

function hasPrivateRuntime(state: ProductionAdoptionState) {
  const evidence = state.plan?.materialization
  return Boolean(
    evidence?.productionDatabaseImported &&
    evidence.matrixContainerStarted &&
    evidence.matrixHealthPassed &&
    evidence.elementContainerStarted &&
    evidence.elementHealthPassed &&
    evidence.runtimeManifestSaved &&
    evidence.databaseOwnershipSaved &&
    evidence.runtimeRecordsCreated,
  )
}

function logCheckpoint(checkpoint: string, evidence: Record<string, unknown>) {
  console.log(JSON.stringify({
    proof: "MIG-UX-E2E-01",
    checkpoint,
    ...evidence,
  }))
}

function migrationInventoryPath(search?: string) {
  const query = new URLSearchParams({
    page: "1",
    pageSize: search ? "10" : "1",
    includeArchived: "true",
  })
  if (search) query.set("search", search)
  return `/api/operator/migrations/sessions/inventory?${query.toString()}`
}

function isResponse(response: Response, method: string, path: string) {
  return response.request().method() === method &&
    new URL(response.url()).pathname === path
}

function expectSuccessfulResponse(response: Response) {
  expect(response.status(), `${response.request().method()} ${response.url()}`).toBeGreaterThanOrEqual(200)
  expect(response.status(), `${response.request().method()} ${response.url()}`).toBeLessThan(300)
}

async function readJsonResponse<T>(response: Response): Promise<T> {
  const contentType = response.headers()["content-type"] ?? ""
  expect(contentType).toContain("application/json")
  return await response.json() as T
}

async function readProblem(response: Response) {
  try {
    const body = await response.json() as {
      code?: unknown
      status?: unknown
      error?: unknown
    }
    if (typeof body.code === "string") return body.code
    if (typeof body.status === "string") return body.status
    if (typeof body.error === "string") return body.error
  } catch {
    // The assertion below will show the missing stable problem code.
  }
  return null
}

async function readDownloadText(download: Download) {
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk))
  }
  return Buffer.concat(chunks).toString("utf8")
}

function parseRetentionDays(value: string) {
  const days = parseBoundedInteger(
    value,
    7,
    30,
    "MEM_E2E_MIGRATION_RETENTION_DAYS",
  )
  if (![7, 14, 21, 30].includes(days)) {
    throw new Error("migration_live_retention_days_invalid")
  }
  return days
}

function parseBoundedInteger(
  value: string,
  minimum: number,
  maximum: number,
  name: string,
) {
  const parsed = Number(value)
  if (!Number.isInteger(parsed) || parsed < minimum || parsed > maximum) {
    throw new Error(`${name} must be an integer from ${minimum} to ${maximum}`)
  }
  return parsed
}

function requiredEnvironmentValue(name: string) {
  const value = process.env[name]?.trim()
  if (!value) throw new Error(`Missing required environment value: ${name}`)
  return value
}

function isHostname(value: string) {
  return value.length <= 253 &&
    value.split(".").every((label) =>
      /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(label),
    )
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")
}
