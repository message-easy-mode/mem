import { expect, test } from "@playwright/test"

import {
  openDashboardOverview,
  openDiagnosticsWorkspace,
  signInNamedOperator,
  signOutNamedOperator,
} from "./support/named-operator-auth"
import { assertRuntimePreflight, readE2eAuthState } from "./support/runtime-environment"

test("preserves the Control Plane identity and named operator across an API restart", async ({ page, request }) => {
  test.setTimeout(60_000)
  const before = readE2eAuthState()
  const after = await assertRuntimePreflight(request)

  expect(after.controlPlaneInstanceId).toBe(before.runtime.controlPlaneInstanceId)
  expect(after.apiProcessInstanceId).not.toBe(before.runtime.apiProcessInstanceId)

  await signInNamedOperator(page, before.credentials)
  await openDashboardOverview(page)
  await openDiagnosticsWorkspace(page)
  await signOutNamedOperator(page)
})
