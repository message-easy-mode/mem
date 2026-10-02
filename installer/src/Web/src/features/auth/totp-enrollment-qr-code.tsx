import { QRCodeSVG } from "qrcode.react"

type TotpEnrollmentQrCodeProps = {
  authenticatorUri: string
  title: string
}

export function TotpEnrollmentQrCode({
  authenticatorUri,
  title,
}: TotpEnrollmentQrCodeProps) {
  return (
    <QRCodeSVG
      aria-label={title}
      bgColor="#ffffff"
      className="max-w-full"
      fgColor="#000000"
      level="M"
      marginSize={4}
      size={192}
      title={title}
      value={authenticatorUri}
    />
  )
}
