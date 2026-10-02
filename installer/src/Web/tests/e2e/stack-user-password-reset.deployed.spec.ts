import { expect, test, type APIRequestContext, type Page } from "@playwright/test"
import {
  completeOpenOperatorStepUp,
  readNamedOperatorCredentialsFromEnvironment,
  signInNamedOperator,
  signOutNamedOperator,
  type NamedOperatorCredentials,
} from "./support/named-operator-auth"

const stackSlug = process.env.MEM_E2E_STACK_SLUG?.trim()
const matrixBaseUrl = process.env.MEM_E2E_MATRIX_BASE_URL?.trim().replace(/\/+$/, "")
const matrixUserId = process.env.MEM_E2E_MATRIX_RESET_USER_ID?.trim()
const oldPassword = process.env.MEM_E2E_MATRIX_RESET_OLD_PASSWORD ?? ""
const newPassword = process.env.MEM_E2E_MATRIX_RESET_NEW_PASSWORD ?? ""

const prerequisitesPresent = Boolean(
  stackSlug &&
  matrixBaseUrl &&
  matrixUserId &&
  oldPassword &&
  newPassword &&
  oldPassword !== newPassword,
)

type MatrixLoginResult = Readonly<{
  responseStatus: number
  accessToken: string | null
}>

async function matrixPasswordLogin(
  request: APIRequestContext,
  password: string,
): Promise<MatrixLoginResult> {
  const response = await request.post(`${matrixBaseUrl}/_matrix/client/v3/login`, {
    data: {
      type: "m.login.password",
      identifier: {
        type: "m.id.user",
        user: matrixUserId,
      },
      password,
      refresh_token: false,
    },
  })

  if (!response.ok()) {
    return { responseStatus: response.status(), accessToken: null }
  }

  const body = await response.json() as { access_token?: unknown }
  return {
    responseStatus: response.status(),
    accessToken: typeof body.access_token === "string" ? body.access_token : null,
  }
}

async function logoutMatrixDevice(
  request: APIRequestContext,
  accessToken: string | null,
) {
  if (!accessToken) {
    return
  }

  await request.post(`${matrixBaseUrl}/_matrix/client/v3/logout`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  })
}

async function resetPasswordThroughMem(
  page: Page,
  operator: NamedOperatorCredentials,
  replacementPassword: string,
) {
  const row = page.locator(`[data-matrix-user-id="${matrixUserId}"]`)
  await expect(row).toBeVisible()
  await row.getByRole("button", { name: "Reset password" }).click()
  await page.locator("#matrix-reset-password").fill(replacementPassword)
  await page.locator("#matrix-reset-password-confirmation").fill(replacementPassword)
  await page.getByRole("button", { name: "Reset password" }).last().click()

  const stepUpDialog = page.getByRole("dialog", { name: "Verify your identity" })
  const stepUpRequired = await stepUpDialog
    .waitFor({ state: "visible", timeout: 2_500 })
    .then(() => true)
    .catch(() => false)

  if (stepUpRequired) {
    await completeOpenOperatorStepUp(page, operator)
  }

  await expect(page.getByText("Matrix password reset", { exact: true })).toBeVisible({
    timeout: 15_000,
  })
}

test.describe("Matrix user password reset", () => {
  test.skip(
    !prerequisitesPresent,
    "Set MEM_E2E_STACK_SLUG, MEM_E2E_MATRIX_BASE_URL, MEM_E2E_MATRIX_RESET_USER_ID, MEM_E2E_MATRIX_RESET_OLD_PASSWORD, and MEM_E2E_MATRIX_RESET_NEW_PASSWORD for a disposable non-production user.",
  )

  test("changes an existing user's password, rejects the old password, accepts the new password, and restores the original", async ({
    page,
    request,
  }) => {
    test.setTimeout(90_000)
    const operator = readNamedOperatorCredentialsFromEnvironment()
    const originalLogin = await matrixPasswordLogin(request, oldPassword)
    expect(originalLogin.responseStatus).toBe(200)
    await logoutMatrixDevice(request, originalLogin.accessToken)

    await signInNamedOperator(page, operator)
    await page.goto(`/stacks/${encodeURIComponent(stackSlug!)}/users`)
    await expect(page.getByText("Matrix password resets ready", { exact: true })).toBeVisible()

    let passwordChanged = false

    try {
      await resetPasswordThroughMem(page, operator, newPassword)
      passwordChanged = true

      const rejectedOldLogin = await matrixPasswordLogin(request, oldPassword)
      expect(rejectedOldLogin.responseStatus).toBeGreaterThanOrEqual(400)

      const acceptedNewLogin = await matrixPasswordLogin(request, newPassword)
      expect(acceptedNewLogin.responseStatus).toBe(200)
      await logoutMatrixDevice(request, acceptedNewLogin.accessToken)
    } finally {
      if (passwordChanged) {
        await resetPasswordThroughMem(page, operator, oldPassword)
      }
    }

    const restoredLogin = await matrixPasswordLogin(request, oldPassword)
    expect(restoredLogin.responseStatus).toBe(200)
    await logoutMatrixDevice(request, restoredLogin.accessToken)
    await signOutNamedOperator(page)
  })
})
