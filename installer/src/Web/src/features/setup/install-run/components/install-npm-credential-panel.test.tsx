import { http, HttpResponse } from "msw"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { InstallNpmCredentialPanel } from "./install-npm-credential-panel"

describe("InstallNpmCredentialPanel", () => {
  it("stores a replacement protected credential, discards the password, and resumes the same installation", async () => {
    const user = userEvent.setup()
    const onContinue = vi.fn()
    let submittedPassword: string | null = null

    server.use(
      http.get("/api/setup/npm-administrator/", () =>
        HttpResponse.json({
          installationId: "95f7eebb-0e83-4bab-9916-b503c853caca",
          status: "Stored",
          message: "Stored protected",
          errorCode: null,
          administratorEmail: "admin@deltabox.dev",
          credentialStored: true,
          verifiedAtUtc: null,
        }),
      ),
      http.post("/api/setup/npm-administrator/", async ({ request }) => {
        const body = await request.json() as { email: string; password: string }
        submittedPassword = body.password

        return HttpResponse.json({
          installationId: "95f7eebb-0e83-4bab-9916-b503c853caca",
          status: "Stored",
          message: "Stored protected",
          errorCode: null,
          administratorEmail: body.email,
          credentialStored: true,
          verifiedAtUtc: null,
        })
      }),
    )

    renderWithProviders(
      <InstallNpmCredentialPanel
        errorCode="NpmCredentialsRejected"
        onContinue={onContinue}
        isContinuePending={false}
      />,
    )

    expect(await screen.findByLabelText("Administrator email")).toHaveValue(
      "admin@deltabox.dev",
    )

    await user.type(
      screen.getByLabelText("Administrator password"),
      "replacement-npm-secret",
    )
    await user.click(
      screen.getByRole("button", { name: "Save credential and continue" }),
    )

    await waitFor(() =>
      expect(submittedPassword).toBe("replacement-npm-secret"),
    )
    await waitFor(() => expect(onContinue).toHaveBeenCalledTimes(1))
    expect(
      screen.queryByDisplayValue("replacement-npm-secret"),
    ).not.toBeInTheDocument()
  })
})
