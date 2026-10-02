import { test } from "@playwright/test"

import {
  inspectFirstCatalogEntryWhenAvailable,
  openBackupCatalog,
  openDashboardOverview,
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"

/**
 * Browser smoke against a rebuilt deployed MEM web bundle.
 *
 * Use a dedicated named test operator on a non-production test environment.
 * The test never uses the retired generic installer `/unlock` browser flow.
 */
test("signs in a named operator and proves the deployed Home overview and Backup Catalog", async ({ page }) => {
  test.setTimeout(60_000)

  const operator = readNamedOperatorCredentialsFromEnvironment()

  await signInNamedOperator(page, operator)
  await openDashboardOverview(page)
  await openBackupCatalog(page)
  await inspectFirstCatalogEntryWhenAvailable(page)
  await signOutNamedOperator(page)
})
