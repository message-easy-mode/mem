import { useEffect, useMemo, useState } from "react"
import { AlertTriangle, CheckCircle2, RefreshCw, ShieldCheck } from "lucide-react"

import type { TranslationKey } from "@/app/i18n/messages"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Label } from "@/components/ui/label"
import { Select } from "@/components/ui/select"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import { SettingsSectionNavigation } from "@/features/operator/settings/components/settings-section-navigation"
import {
  isStepUpRequiredSecuritySettingsProblem,
  SecuritySettingsProblemError,
  type UpdateHighRiskStepUpSettingsInput,
} from "@/features/operator/settings/api/security-settings.api"
import {
  useSecuritySettings,
  useUpdateHighRiskStepUpSettings,
} from "@/features/operator/settings/hooks/use-security-settings"

type PendingSave = UpdateHighRiskStepUpSettingsInput

function problemMessage(error: unknown, translate: (key: TranslationKey) => string) {
  if (!(error instanceof SecuritySettingsProblemError)) {
    return translate("settings.security.problem.default")
  }

  const keyByCode: Record<string, TranslationKey> = {
    high_risk_step_up_window_invalid: "settings.security.problem.windowInvalid",
    security_settings_validation_failed: "settings.security.problem.validation",
    security_settings_persistence_unavailable: "settings.security.problem.persistence",
  }

  return error.code && keyByCode[error.code]
    ? translate(keyByCode[error.code])
    : translate("settings.security.problem.default")
}

export function SecuritySettingsPage() {
  const { t } = useI18n()
  const { session } = useOperatorSession()
  const isPlatformOwner = session.roles.includes("platform_owner")
  const settings = useSecuritySettings(isPlatformOwner)
  const updateSettings = useUpdateHighRiskStepUpSettings()

  const [required, setRequired] = useState(true)
  const [reuseMinutes, setReuseMinutes] = useState(15)
  const [isDisableConfirmationOpen, setIsDisableConfirmationOpen] = useState(false)
  const [isStepUpOpen, setIsStepUpOpen] = useState(false)
  const [pendingSave, setPendingSave] = useState<PendingSave | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)

  const current = settings.data?.highRiskStepUp
  const allowedMinutes = useMemo(
    () => current?.allowedReuseVerificationMinutes.length
      ? current.allowedReuseVerificationMinutes
      : [5, 15, 30, 60],
    [current?.allowedReuseVerificationMinutes],
  )

  useEffect(() => {
    if (!current) {
      return
    }

    setRequired(current.required)
    setReuseMinutes(current.reuseVerificationMinutes)
  }, [current])

  if (!isPlatformOwner) {
    return (
      <Card className="mx-auto max-w-2xl">
        <CardHeader>
          <CardTitle>{t("settings.security.accessDeniedTitle")}</CardTitle>
          <CardDescription>{t("settings.security.accessDeniedDescription")}</CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const hasChanges = current
    ? required !== current.required || reuseMinutes !== current.reuseVerificationMinutes
    : false

  const requestSave = () => {
    if (!hasChanges) {
      return
    }

    const next = {
      required,
      reuseVerificationMinutes: reuseMinutes,
    }

    setSuccessMessage(null)
    updateSettings.reset()

    if (current?.required === true && !next.required) {
      setPendingSave(next)
      setIsDisableConfirmationOpen(true)
      return
    }

    runSave(next)
  }

  const runSave = (input: PendingSave) => {
    setPendingSave(input)
    updateSettings.mutate(input, {
      onSuccess: () => {
        setPendingSave(null)
        setSuccessMessage(t("settings.security.saved"))
      },
      onError: (error) => {
        if (isStepUpRequiredSecuritySettingsProblem(error)) {
          setIsStepUpOpen(true)
        }
      },
    })
  }

  const resumeAfterStepUp = () => {
    if (pendingSave) {
      runSave(pendingSave)
    }
  }

  const resetForm = () => {
    if (!current) {
      return
    }

    setRequired(current.required)
    setReuseMinutes(current.reuseVerificationMinutes)
    setSuccessMessage(null)
    updateSettings.reset()
  }

  const error = updateSettings.error &&
    !isStepUpRequiredSecuritySettingsProblem(updateSettings.error)
    ? problemMessage(updateSettings.error, t)
    : null

  return (
    <div className="space-y-6">
      <div className="space-y-2">
        <div className="text-sm font-medium text-muted-foreground">
          {t("settings.breadcrumb")}
        </div>
        <h1 className="text-3xl font-semibold tracking-tight">
          {t("settings.security.title")}
        </h1>
        <p className="max-w-3xl text-muted-foreground">
          {t("settings.security.description")}
        </p>
      </div>

      <SettingsSectionNavigation />

      {settings.isLoading ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("settings.security.loading")}</CardTitle>
            <CardDescription>{t("settings.security.loadingDescription")}</CardDescription>
          </CardHeader>
        </Card>
      ) : null}

      {settings.isError ? (
        <Alert variant="destructive">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("settings.security.loadErrorTitle")}</AlertTitle>
          <AlertDescription>{t("settings.security.loadErrorDescription")}</AlertDescription>
        </Alert>
      ) : null}

      {current ? (
        <Card>
          <CardHeader className="space-y-3">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div className="space-y-1">
                <CardTitle className="flex items-center gap-2">
                  <ShieldCheck className="h-5 w-5" />
                  {t("settings.security.highRisk.title")}
                </CardTitle>
                <CardDescription>
                  {t("settings.security.highRisk.description")}
                </CardDescription>
              </div>
              <Badge variant={current.required ? "default" : "outline"}>
                {current.required
                  ? t("settings.security.highRisk.current.required")
                  : t("settings.security.highRisk.current.notRequired")}
              </Badge>
            </div>

            {current.isDefaulted ? (
              <Alert>
                <CheckCircle2 className="h-4 w-4" />
                <AlertTitle>{t("settings.security.defaultedTitle")}</AlertTitle>
                <AlertDescription>{t("settings.security.defaultedDescription")}</AlertDescription>
              </Alert>
            ) : null}
          </CardHeader>

          <CardContent className="space-y-6">
            <div className="grid gap-3 lg:grid-cols-2">
              <label className="rounded-xl border border-border bg-card p-4">
                <div className="flex items-start gap-3">
                  <input
                    type="radio"
                    name="high-risk-step-up"
                    className="mt-1"
                    checked={required}
                    onChange={() => setRequired(true)}
                  />
                  <div className="space-y-1">
                    <div className="font-medium">
                      {t("settings.security.highRisk.requiredTitle")}
                    </div>
                    <p className="text-sm text-muted-foreground">
                      {t("settings.security.highRisk.requiredDescription")}
                    </p>
                  </div>
                </div>
              </label>

              <label className="rounded-xl border border-destructive/40 bg-card p-4">
                <div className="flex items-start gap-3">
                  <input
                    type="radio"
                    name="high-risk-step-up"
                    className="mt-1"
                    checked={!required}
                    onChange={() => setRequired(false)}
                  />
                  <div className="space-y-1">
                    <div className="font-medium">
                      {t("settings.security.highRisk.notRequiredTitle")}
                    </div>
                    <p className="text-sm text-muted-foreground">
                      {t("settings.security.highRisk.notRequiredDescription")}
                    </p>
                  </div>
                </div>
              </label>
            </div>

            {!required ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("settings.security.highRisk.warningTitle")}</AlertTitle>
                <AlertDescription>{t("settings.security.highRisk.warningDescription")}</AlertDescription>
              </Alert>
            ) : null}

            <div className="space-y-2 rounded-xl border border-border p-4">
              <Label htmlFor="step-up-reuse-minutes">
                {t("settings.security.reuseWindow.label")}
              </Label>
              <p className="text-sm text-muted-foreground">
                {t("settings.security.reuseWindow.description")}
              </p>
              <Select
                id="step-up-reuse-minutes"
                value={String(reuseMinutes)}
                onChange={(event) => setReuseMinutes(Number(event.target.value))}
                disabled={!required}
              >
                {allowedMinutes.map((minutes) => (
                  <option key={minutes} value={minutes}>
                    {minutes === 15
                      ? t("settings.security.reuseWindow.optionRecommended", { minutes })
                      : t("settings.security.reuseWindow.option", { minutes })}
                  </option>
                ))}
              </Select>
            </div>

            {error ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("settings.security.saveErrorTitle")}</AlertTitle>
                <AlertDescription>{error}</AlertDescription>
              </Alert>
            ) : null}

            {successMessage ? (
              <Alert>
                <CheckCircle2 className="h-4 w-4" />
                <AlertTitle>{successMessage}</AlertTitle>
                <AlertDescription>{t("settings.security.savedDescription")}</AlertDescription>
              </Alert>
            ) : null}

            <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button
                type="button"
                variant="outline"
                onClick={resetForm}
                disabled={!hasChanges || updateSettings.isPending}
              >
                {t("settings.security.reset")}
              </Button>
              <Button
                type="button"
                onClick={requestSave}
                disabled={!hasChanges || updateSettings.isPending}
              >
                {updateSettings.isPending ? (
                  <>
                    <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
                    {t("settings.security.saving")}
                  </>
                ) : (
                  t("settings.security.save")
                )}
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : null}

      <ConfirmationDialog
        open={isDisableConfirmationOpen}
        onOpenChange={setIsDisableConfirmationOpen}
        title={t("settings.security.disableConfirmTitle")}
        description={t("settings.security.disableConfirmDescription")}
        confirmLabel={t("settings.security.disableConfirmAction")}
        cancelLabel={t("settings.security.disableConfirmCancel")}
        confirmVariant="destructive"
        onConfirm={() => {
          const input = pendingSave
          setIsDisableConfirmationOpen(false)
          if (input) {
            runSave(input)
          }
        }}
      />

      <OperatorStepUpDialog
        open={isStepUpOpen}
        onOpenChange={setIsStepUpOpen}
        onVerified={resumeAfterStepUp}
      />
    </div>
  )
}
