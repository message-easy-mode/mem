import { expect, test } from "@playwright/test"
import {
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"

const stackSlug = process.env.MEM_E2E_STACK_SLUG?.trim()
const expectedMatrixUserId = process.env.MEM_E2E_EXPECTED_MATRIX_USER_ID?.trim()

test.describe("restored Matrix user inventory", () => {
  test.skip(!stackSlug || !expectedMatrixUserId, "Set MEM_E2E_STACK_SLUG and MEM_E2E_EXPECTED_MATRIX_USER_ID for a non-production restored-stack proof.")

  test("shows a synchronized restored user without a false first-admin prompt", async ({ page }) => {
    test.setTimeout(60_000)
    const operator = readNamedOperatorCredentialsFromEnvironment()
    await signInNamedOperator(page, operator)
    await page.goto(`/stacks/${encodeURIComponent(stackSlug!)}/users`)
    await expect(page.locator('[data-slot="card-title"]').filter({ hasText: /^Matrix users$/ })).toBeVisible()
    await expect(page.getByText("User inventory synchronized", { exact: true })).toBeVisible()
    await expect(page.getByText(expectedMatrixUserId!, { exact: true })).toBeVisible()
    await expect(page.getByText("Recovered from Synapse", { exact: true })).toBeVisible()
    await expect(page.getByRole("button", { name: "Create first Matrix admin" })).toHaveCount(0)
    await signOutNamedOperator(page)
  })
})
