import { expect, test } from "@playwright/test"

import {
  bootstrapDisposableFirstOwner,
  signOutNamedOperator,
} from "./support/named-operator-auth"

function setupToken(): string {
  const value = process.env.MEM_E2E_SETUP_TOKEN?.trim()
  if (!value) throw new Error("MEM_E2E_SETUP_TOKEN is required for the diagnostics dev proof.")
  return value
}

type FixtureResponse = Readonly<{
  eventId: string
  incidentId: string
  traceId: string | null
  correlationId: string | null
}>

test("proves the Diagnostics command centre, self-test, attention, support, Seq, and Portainer boundaries", async ({ page, context }) => {
  test.setTimeout(120_000)
  await context.grantPermissions(["clipboard-read", "clipboard-write"])
  await bootstrapDisposableFirstOwner(page, setupToken())

  const directHostAgentRequests: string[] = []
  page.on("request", (request) => {
    if (request.url().includes("/internal/host-agent/")) {
      directHostAgentRequests.push(request.url())
    }
  })

  const overviewResponse = page.waitForResponse((response) =>
    response.request().method() === "GET" &&
    response.url().includes("/api/operator/diagnostics/overview") &&
    response.status() === 200,
  )
  await page.goto("/diagnostics")
  expect((await overviewResponse).headers()["cache-control"]).toContain("no-store")

  await expect(page.getByText("Diagnostics health", { exact: true })).toBeVisible()
  for (const label of ["Overall", "Attention", "Local recorder", "Safe store", "Seq"]) {
    await expect(page.getByText(label, { exact: true }).first()).toBeVisible()
  }
  for (const heading of [
    "Incidents and technical events",
    "Logging health",
    "Advanced logging with Seq",
    "Advanced container diagnostics",
    "Verify diagnostics pipeline",
    "Runtime reconciliation",
  ]) {
    await expect(page.getByRole("heading", { name: heading, exact: true })).toBeVisible()
  }
  await expect(page.getByRole("heading", { name: "Support and reports", exact: true })).toHaveCount(0)
  await expect(page.getByRole("heading", { name: "Preflight checks", exact: true })).toHaveCount(0)
  await expect(page.getByRole("link", { name: "Incidents and technical events", exact: true })).toHaveAttribute(
    "href",
    "/diagnostics/logs?tab=incidents",
  )
  await expect(page.getByRole("link", { name: "Logging health", exact: true })).toHaveAttribute(
    "href",
    "/diagnostics/logs?tab=health",
  )

  await page.getByRole("button", { name: "Run verification", exact: true }).click()
  await expect(page.getByText("Verification passed", { exact: true })).toBeVisible()
  await expect(page.getByText(/^diag_verify_/)).toBeVisible()
  await expect(page.getByText("Not configured", { exact: true }).last()).toBeVisible()

  await page.getByRole("link", { name: "Review and set up Seq", exact: true }).click()
  await expect(page).toHaveURL(/\/diagnostics\/seq$/)
  await expect(page.getByRole("navigation", { name: "Breadcrumbs" })).toContainText("Diagnostics")
  await expect(page.getByRole("heading", { name: "Advanced logging with Seq", exact: true })).toBeVisible()
  await expect(page.getByText("How Seq fits with MEM Diagnostics", { exact: true })).toBeVisible()
  const reviewSetup = page.getByRole("button", { name: "Review setup", exact: true })
  if (await reviewSetup.isVisible()) {
    await reviewSetup.click()
    await expect(page.getByText(/Setup is (ready|not ready)/)).toBeVisible()
  }

  await page.goto("/diagnostics/portainer")
  await expect(page.getByRole("navigation", { name: "Breadcrumbs" })).toContainText("Diagnostics")
  await expect(page.getByRole("heading", { name: "Portainer overview", exact: true })).toBeVisible()
  await expect(page.getByText("MEM explains; Portainer inspects", { exact: true })).toBeVisible()

  const fixture = await page.evaluate(async () => {
    const response = await fetch("/api/operator/diagnostics/test-fixture/incident", {
      method: "POST",
      headers: {
        "X-Correlation-ID": "diagnostics-e2e-release-proof",
      },
    })
    const body = await response.json()
    return {
      status: response.status,
      cacheControl: response.headers.get("cache-control"),
      body,
    }
  })

  expect(fixture.status).toBe(201)
  expect(fixture.cacheControl).toContain("no-store")
  const created = fixture.body as FixtureResponse
  expect(created.eventId).toMatch(/^evt_/)
  expect(created.incidentId).toMatch(/^inc_/)
  expect(created.correlationId).toBe("diagnostics-e2e-release-proof")

  await page.goto("/diagnostics")
  const attentionBell = page.locator('[data-attention-state="attention"]')
  await expect(attentionBell).toBeVisible()
  await attentionBell.click()
  await expect(page.getByText("A deterministic diagnostics release-proof incident was created.", { exact: true })).toBeVisible()
  await page.getByText("A deterministic diagnostics release-proof incident was created.", { exact: true }).click()

  await expect(page).toHaveURL(new RegExp(`/diagnostics/logs\\?incident=${created.incidentId}$`))
  await expect(page.getByText(created.incidentId, { exact: true })).toBeVisible()
  await expect(page.getByText("diagnostics.release_proof.failure", { exact: true })).toBeVisible()

  await page.getByRole("button", { name: "Load Docker evidence" }).click()
  await expect(
    page.getByText("Docker evidence is unavailable, but the incident remains usable."),
  ).toBeVisible()

  await page.getByLabel("Include bounded Docker evidence").check()
  await page.getByRole("button", { name: "Copy support JSON" }).click()
  const clipboardText = await page.evaluate(() => navigator.clipboard.readText())
  const copied = JSON.parse(clipboardText)
  expect(copied.incident.incidentId).toBe(created.incidentId)
  expect(copied.redaction.redactionsApplied).toBe(true)
  expect(copied.dockerEvidence?.available).toBe(false)

  const downloadPromise = page.waitForEvent("download")
  await page.getByRole("button", { name: "Download support report" }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toContain(created.incidentId)
  const stream = await download.createReadStream()
  if (!stream) throw new Error("diagnostics_e2e_download_stream_missing")
  const chunks: Buffer[] = []
  for await (const chunk of stream) chunks.push(Buffer.from(chunk))
  const downloaded = JSON.parse(Buffer.concat(chunks).toString("utf8"))
  expect(downloaded.incident.incidentId).toBe(created.incidentId)

  await page.getByRole("button", { name: "Technical events" }).click()
  await expect(page).toHaveURL(/tab=events/)
  await expect(page.getByText("Newest events first", { exact: true })).toBeVisible()
  await expect(page.getByText("A deterministic diagnostics release-proof incident was created.").first()).toBeVisible()

  await page.getByRole("button", { name: "Logging health" }).click()
  await expect(page.getByText("Local black-box recorder", { exact: true })).toBeVisible()
  await expect(page.getByText("Safe diagnostic event store", { exact: true })).toBeVisible()

  await page.getByLabel("Language").selectOption("de")
  await expect(page.getByRole("heading", { name: "Vorfälle und technische Ereignisse", exact: true })).toBeVisible()
  await page.getByLabel("Sprache").selectOption("en")

  expect(directHostAgentRequests).toEqual([])
  await signOutNamedOperator(page)
})
