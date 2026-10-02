import { expect, test } from "@playwright/test"

import {
  bootstrapDisposableFirstOwner,
  openBackupCatalog,
  openDashboardOverview,
  openDiagnosticsWorkspace,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"
import { assertRuntimePreflight, persistE2eAuthState } from "./support/runtime-environment"

function setupToken(): string {
  const value = process.env.MEM_E2E_SETUP_TOKEN?.trim()
  if (!value) throw new Error("MEM_E2E_SETUP_TOKEN is required for disposable environment parity proof.")
  return value
}

test("bootstraps and proves the shared operator journey in the declared runtime", async ({ page, request }) => {
  test.setTimeout(120_000)
  const runtime = await assertRuntimePreflight(request)
  const owner = await bootstrapDisposableFirstOwner(page, setupToken())

  await openDashboardOverview(page)
  await signOutNamedOperator(page)
  await signInNamedOperator(page, owner)
  await openDashboardOverview(page)
  await openDiagnosticsWorkspace(page)

  await page.goto("/diagnostics/seq")
  await expect(page.getByRole("heading", { name: "Advanced logging with Seq", exact: true })).toBeVisible()
  await expect(page.getByText("How Seq fits with MEM Diagnostics", { exact: true })).toBeVisible()

  // Route refresh is a key embedded-SPA proof and is also valid through Vite.
  await page.reload()
  await expect(page.getByRole("heading", { name: "Advanced logging with Seq", exact: true })).toBeVisible()

  await openBackupCatalog(page)
  persistE2eAuthState(owner, runtime)
  await signOutNamedOperator(page)
})
