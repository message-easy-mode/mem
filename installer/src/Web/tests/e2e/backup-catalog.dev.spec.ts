import { expect, test } from "@playwright/test"

import {
  bootstrapDisposableFirstOwner,
  inspectFirstCatalogEntryWhenAvailable,
  openBackupCatalog,
  openDashboardOverview,
  signInNamedOperator,
  signOutNamedOperator,
  verifyCurrentOperatorStepUp,
} from "./support/named-operator-auth"

function setupToken(): string {
  const value = process.env.MEM_E2E_SETUP_TOKEN?.trim()

  if (!value) {
    throw new Error("MEM_E2E_SETUP_TOKEN is required for the disposable dev browser smoke.")
  }

  return value
}

/**
 * Full browser proof against the current Vite source tree.
 *
 * This intentionally replaces the retired generic browser `/unlock` flow.
 * It must run only against a new disposable no-owner database. The test
 * creates a random first Platform Owner through the real bootstrap UI, proves
 * the authenticated Home overview through Vite's real `/api` proxy, signs out,
 * signs in with password and calculated TOTP, proves the Home overview again,
 * verifies a fresh password-plus-TOTP step-up in that exact browser session,
 * then reaches protected Platform Owner and Backup Catalog routes.
 */
test("bootstraps a named owner, proves the Vite Home overview, verifies step-up, and opens the Backup Catalog", async ({ page }) => {
  test.setTimeout(90_000)

  const owner = await bootstrapDisposableFirstOwner(page, setupToken())

  await openDashboardOverview(page)
  await signOutNamedOperator(page)
  await signInNamedOperator(page, owner)
  await openDashboardOverview(page)
  await verifyCurrentOperatorStepUp(page, owner)

  await page.goto("/security/operators")
  await expect(page).toHaveURL(/\/security\/operators$/, { timeout: 10_000 })
  await expect(
    page.getByRole("heading", { name: "Operator access" }),
  ).toBeVisible()
  await expect(page.getByText(owner.username, { exact: true })).toBeVisible()
  await expect(page.getByText("Current session", { exact: true })).toBeVisible()

  await openBackupCatalog(page)
  await inspectFirstCatalogEntryWhenAvailable(page)
})
