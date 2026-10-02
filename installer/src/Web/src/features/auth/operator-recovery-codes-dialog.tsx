import { useEffect, useState } from "react"
import { createPortal } from "react-dom"
import { KeyRound } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

type OperatorRecoveryCodesDialogProps = {
  recoveryCodes: string[] | null
  onAcknowledge: () => void
}

/**
 * Shows newly generated raw recovery codes exactly in the in-memory browser
 * response path. This component deliberately has no dismiss backdrop, Escape
 * handler, browser persistence, download, or telemetry integration. The codes
 * are cleared by the parent immediately after explicit acknowledgement.
 */
export function OperatorRecoveryCodesDialog({
  recoveryCodes,
  onAcknowledge,
}: OperatorRecoveryCodesDialogProps) {
  const { t } = useI18n()
  const [hasStoredCodes, setHasStoredCodes] = useState(false)

  useEffect(() => {
    if (!recoveryCodes) {
      setHasStoredCodes(false)
    }
  }, [recoveryCodes])

  if (!recoveryCodes || recoveryCodes.length === 0) {
    return null
  }

  return createPortal(
    <div className="fixed inset-0 z-[60] overflow-y-auto overscroll-contain px-4 py-6 sm:px-6 sm:py-8">
      <div className="fixed inset-0 bg-background/85 backdrop-blur-sm" />

      <div className="relative z-10 flex min-h-full items-start justify-center [@media(min-height:42rem)]:items-center">
        <Card
          role="dialog"
          aria-modal="true"
          aria-labelledby="operator-recovery-codes-title"
          className="w-full max-w-lg shadow-xl"
        >
          <CardHeader>
            <div className="space-y-3">
              <div className="flex h-11 w-11 items-center justify-center rounded-2xl border border-border bg-muted">
                <KeyRound className="h-5 w-5" />
              </div>
              <div>
                <CardTitle id="operator-recovery-codes-title" className="text-xl">
                  {t("recoveryCodes.displayTitle")}
                </CardTitle>
                <CardDescription className="mt-2">
                  {t("recoveryCodes.displayDescription")}
                </CardDescription>
              </div>
            </div>
          </CardHeader>

          <CardContent className="space-y-4">
            <Alert>
              <AlertTitle>{t("recoveryCodes.oneTimeAlertTitle")}</AlertTitle>
              <AlertDescription>{t("recoveryCodes.oneTimeAlertDescription")}</AlertDescription>
            </Alert>

            <pre
              className="max-h-56 overflow-auto rounded-lg border border-border bg-muted p-4 text-sm"
              aria-label={t("recoveryCodes.codesLabel")}
            >
              {recoveryCodes.join("\n")}
            </pre>

            <label className="flex items-start gap-3 rounded-lg border border-border p-3 text-sm">
              <input
                type="checkbox"
                checked={hasStoredCodes}
                onChange={(event) => setHasStoredCodes(event.target.checked)}
                className="mt-0.5"
              />
              <span>{t("recoveryCodes.acknowledgement")}</span>
            </label>

            <div className="flex justify-end">
              <Button
                type="button"
                disabled={!hasStoredCodes}
                onClick={onAcknowledge}
              >
                {t("recoveryCodes.finish")}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>,
    document.body,
  )
}
