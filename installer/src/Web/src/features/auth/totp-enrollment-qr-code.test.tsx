import { screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import { renderWithProviders } from "@/test/render-with-providers"

import { TotpEnrollmentQrCode } from "./totp-enrollment-qr-code"

vi.mock("qrcode.react", () => ({
  QRCodeSVG: ({
    value,
    title,
  }: {
    value: string
    title?: string
  }) => (
    <svg
      aria-label={title}
      data-testid="totp-enrollment-qr-code"
      data-value={value}
      role="img"
    />
  ),
}))

describe("TotpEnrollmentQrCode", () => {
  it("encodes the exact server-provided authenticator URI locally", () => {
    const authenticatorUri =
      "otpauth://totp/MEM%20Control%20Plane:admin?secret=BASE32&issuer=MEM%20Control%20Plane&digits=6"

    renderWithProviders(
      <TotpEnrollmentQrCode
        authenticatorUri={authenticatorUri}
        title="Scan this QR code"
      />,
    )

    expect(screen.getByRole("img", { name: "Scan this QR code" })).toHaveAttribute(
      "data-value",
      authenticatorUri,
    )
  })
})
