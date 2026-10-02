import { useEffect, useState } from "react"
import { Loader2, LockKeyhole } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  getNpmAdminCredential,
  saveNpmAdminCredential,
} from "@/features/setup/review/api/npm-admin-credential.api"

export function InstallNpmCredentialPanel({
  errorCode,
  onContinue,
  isContinuePending,
}: {
  errorCode: "NpmAdminCredentialRequired" | "NpmCredentialsRejected"
  onContinue: () => void
  isContinuePending: boolean
}) {
  const { t } = useI18n()
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let active = true

    void getNpmAdminCredential()
      .then((projection) => {
        if (!active) return
        setEmail(projection.administratorEmail ?? "")
      })
      .catch((cause) => {
        if (!active) return
        setError(messageFrom(cause, t("setup.activity.npmLoadFailed")))
      })
      .finally(() => {
        if (active) setLoading(false)
      })

    return () => {
      active = false
    }
  }, [])

  async function saveAndContinue() {
    if (!email.trim() || !password || saving || isContinuePending) return

    setSaving(true)
    setError(null)

    const request = {
      email: email.trim(),
      password,
    }

    // Once submitted, do not retain plaintext in component state even if the
    // subsequent install-resume request fails or takes longer than expected.
    setPassword("")

    try {
      const stored = await saveNpmAdminCredential(request)
      setEmail(stored.administratorEmail ?? request.email)
      onContinue()
    } catch (cause) {
      setError(messageFrom(cause, t("setup.activity.npmStoreFailed")))
    } finally {
      setSaving(false)
    }
  }

  const rejected = errorCode === "NpmCredentialsRejected"
  const busy = loading || saving || isContinuePending

  return (
    <Card className="border-amber-500/30 bg-amber-500/5">
      <CardHeader>
        <CardTitle>{t("setup.activity.npmRequired")}</CardTitle>
        <CardDescription>
          {rejected
            ? t("setup.activity.npmRejectedDescription")
            : t("setup.activity.npmRequiredDescription")}
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        {error ? (
          <div className="rounded-lg border border-destructive/40 bg-destructive/5 p-3 text-sm text-destructive">
            {error}
          </div>
        ) : null}

        <div className="grid gap-4 sm:max-w-xl">
          <div className="space-y-2">
            <Label htmlFor="installNpmAdminEmail">{t("setup.review.adminEmail")}</Label>
            <Input
              id="installNpmAdminEmail"
              type="email"
              autoComplete="email"
              value={email}
              disabled={busy}
              onChange={(event) => setEmail(event.target.value)}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="installNpmAdminPassword">{t("setup.review.adminPassword")}</Label>
            <Input
              id="installNpmAdminPassword"
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={busy}
              onChange={(event) => setPassword(event.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              {t("setup.activity.npmDescription")}
            </p>
          </div>

          <div>
            <Button
              type="button"
              disabled={busy || !email.trim() || !password}
              onClick={saveAndContinue}
            >
              {busy ? (
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <LockKeyhole className="mr-2 h-4 w-4" />
              )}
              {t("setup.activity.npmSaveContinue")}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function messageFrom(cause: unknown, fallback: string) {
  return cause instanceof Error && cause.message.trim()
    ? cause.message
    : fallback
}
