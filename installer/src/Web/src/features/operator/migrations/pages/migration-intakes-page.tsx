import { useRef, useState } from "react"
import { useQueryClient } from "@tanstack/react-query"
import { KeyRound, LockKeyhole, ShieldCheck } from "lucide-react"
import { useNavigate } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  createSecureMigrationIntake,
  isSecureMigrationStepUpRequired,
} from "@/features/operator/migrations/api/migration-intakes"
import { migrationSessionKeys } from "@/features/operator/migrations/hooks/use-migration-sessions"

export function MigrationIntakesPage() {
  const { t } = useI18n()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [name, setName] = useState("Imported MEM server")
  const [error, setError] = useState<string | null>(null)
  const [isWorking, setIsWorking] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [createPending, setCreatePending] = useState(false)
  const submitting = useRef(false)

  const createSecureIntake = async () => {
    if (submitting.current) return
    submitting.current = true
    try {
      setError(null)
      setIsWorking(true)
      const result = await createSecureMigrationIntake(name)
      setCreatePending(false)
      await queryClient.invalidateQueries({ queryKey: migrationSessionKeys.all })
      navigate(`/migrations/${encodeURIComponent(result.intakeId)}`)
    } catch (caught) {
      if (isSecureMigrationStepUpRequired(caught)) {
        setCreatePending(true)
        setStepUpOpen(true)
        return
      }

      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      submitting.current = false
      setIsWorking(false)
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold">{t("migrationIntake.title")}</h1>
        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("migrationIntake.secureDescription")}
        </p>
      </div>

      {error ? (
        <Alert variant="destructive">
          <AlertTitle>{t("migrationWorkspace.package.actionFailed")}</AlertTitle>
          <AlertDescription>{error}</AlertDescription>
        </Alert>
      ) : null}

      <Card className="max-w-3xl border-primary/50">
        <CardHeader>
          <div className="flex items-start justify-between gap-3">
            <div>
              <CardTitle className="flex items-center gap-2">
                <LockKeyhole className="h-5 w-5" aria-hidden="true" />
                {t("migrationIntake.secureTitle")}
              </CardTitle>
              <CardDescription className="mt-1">
                {t("migrationIntake.secureStartDescription")}
              </CardDescription>
            </div>
            <Badge>{t("migrationWorkspace.package.recommended")}</Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <label className="block space-y-2 text-sm">
            <span className="font-medium">{t("migrationIntake.displayName")}</span>
            <input
              className="w-full rounded-md border bg-background px-3 py-2 text-sm"
              value={name}
              onChange={(event) => setName(event.target.value)}
              aria-label={t("migrationIntake.displayName")}
            />
          </label>

          <div className="rounded-lg border bg-muted/30 p-4 text-sm">
            <div className="flex gap-3">
              <ShieldCheck className="mt-0.5 h-5 w-5 text-primary" aria-hidden="true" />
              <div>
                <p className="font-medium">{t("migrationIntake.targetOnlyKey")}</p>
                <p className="mt-1 text-muted-foreground">
                  {t("migrationIntake.targetOnlyKeyDescription")}
                </p>
              </div>
            </div>
          </div>

          <div className="flex flex-wrap gap-2">
            <Button disabled={isWorking || stepUpOpen || !name.trim()} onClick={() => void createSecureIntake()}>
              <KeyRound className="mr-2 h-4 w-4" aria-hidden="true" />
              {isWorking ? t("migrationWorkspace.package.creating") : t("migrationIntake.createSecure")}
            </Button>
            <Button
              type="button"
              variant="outline"
              disabled={isWorking || stepUpOpen}
              onClick={() => {
                setCreatePending(false)
                navigate("/migrations")
              }}
            >
              {t("migrationIntake.cancel")}
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            {t("migrationWorkspace.package.createThenContinue")}
          </p>
        </CardContent>
      </Card>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => {
          setStepUpOpen(false)
          if (createPending) void createSecureIntake()
        }}
      />
    </div>
  )
}
