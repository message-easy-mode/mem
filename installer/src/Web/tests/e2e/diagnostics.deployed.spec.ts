import { expect, test } from "@playwright/test"

import {
  openDiagnosticsWorkspace,
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"

test("proves the deployed Diagnostics command centre and production safety boundary", async ({ page }) => {
  test.setTimeout(75_000)
  const operator = readNamedOperatorCredentialsFromEnvironment()
  const directHostAgentRequests: string[] = []
  page.on("request", (request) => {
    if (request.url().includes("/internal/host-agent/")) {
      directHostAgentRequests.push(request.url())
    }
  })

  await signInNamedOperator(page, operator)

  const fixtureStatus = await page.evaluate(async () => {
    const response = await fetch("/api/operator/diagnostics/test-fixture/incident", {
      method: "POST",
    })
    return response.status
  })
  expect(fixtureStatus).toBe(404)

  await openDiagnosticsWorkspace(page)
  await expect(page.getByRole("navigation", { name: "Breadcrumbs" })).toContainText("Diagnostics")
  const technicalEvents = page.getByRole("button", { name: "Technical events" })
  if (await technicalEvents.isVisible()) {
    await technicalEvents.click()
    await expect(page.getByText("Newest events first", { exact: true })).toBeVisible()
  }
  await page.getByRole("button", { name: "Logging health" }).click()
  await page.getByText("Local black-box recorder", { exact: true }).waitFor()
  await page.getByText("Safe diagnostic event store", { exact: true }).waitFor()

  await page.getByLabel("Language").selectOption("de")
  await expect(page.getByRole("heading", { name: "Vorfälle und technische Ereignisse", exact: true })).toBeVisible()
  await page.getByLabel("Sprache").selectOption("en")

  expect(directHostAgentRequests).toEqual([])
  await signOutNamedOperator(page)
})
