import { useMutation, useQueryClient } from "@tanstack/react-query"
import { CheckCircle2, KeyRound, ShieldCheck } from "lucide-react"
import { useState, type FormEvent } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { getMemApiProblemCode, getMemApiProblemDetail } from "@/lib/api-problem"
import { connectDiagnosticsSeq } from "../api/diagnostics.api"
import type {
  DiagnosticsSeqConnectionResponse,
  DiagnosticsSeqOverviewResponse,
} from "../api/diagnostics.types"
import { diagnosticsUtcIso, formatDiagnosticsLocalDateTime } from "../diagnostics-time"

export function DiagnosticsSeqConnection({
  data,
  isOwner,
}: {
  data: DiagnosticsSeqOverviewResponse
  isOwner: boolean
}) {
  const { language, t } = useI18n()
  const queryClient = useQueryClient()
  const [password, setPassword] = useState("")
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [stepUpReady, setStepUpReady] = useState(false)
  const [stepUpRequiredByServer, setStepUpRequiredByServer] = useState(false)
  const [outcome, setOutcome] = useState<DiagnosticsSeqConnectionResponse | null>(null)

  const connection = useMutation({
    mutationFn: connectDiagnosticsSeq,
    onSuccess: (result) => {
      queryClient.setQueryData(["diagnostics", "seq"], result.overview)
      setStepUpRequiredByServer(false)
      setOutcome(result)
    },
    onError: (error) => {
      if (getMemApiProblemCode(error) === "step_up_required") {
        setStepUpRequiredByServer(true)
        setStepUpReady(false)
        setStepUpOpen(true)
      }
    },
  })

  const verified = data.connection?.verificationState === "verified"
  const requiresRecentStepUp =
    data.capabilities.requiresRecentStepUp !== false || stepUpRequiredByServer
  const identityReady = !requiresRecentStepUp || stepUpReady
  const runtimeReadyForConnection =
    data.runtime.managed &&
    data.runtime.running &&
    data.runtime.usesApprovedRuntime &&
    data.health.reachable
  const visible =
    data.runtime.managed &&
    data.runtime.present &&
    (verified || runtimeReadyForConnection)
  if (!visible) {
    return null
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!password || connection.isPending) {
      return
    }

    const submittedPassword = password
    setPassword("")
    setOutcome(null)
    connection.reset()
    connection.mutate(submittedPassword)
  }

  return (
    <section aria-labelledby="seq-connection-title">
      <Card>
        <CardHeader>
          <div className="flex items-start justify-between gap-3">
            <div>
              <h2 id="seq-connection-title" className="flex items-center gap-2 text-lg font-medium">
                <KeyRound className="h-5 w-5" />
                {t("diagnostics.seq.connect.title")}
              </h2>
              <CardDescription className="mt-1 max-w-3xl">
                {t("diagnostics.seq.connect.description")}
              </CardDescription>
            </div>
            <Badge variant={verified ? "secondary" : "outline"}>
              {verified
                ? t("diagnostics.seq.connect.statusVerified")
                : t("diagnostics.seq.connect.statusNotConnected")}
            </Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {verified ? (
            <Alert>
              <CheckCircle2 className="h-4 w-4" />
              <AlertTitle>{t("diagnostics.seq.connect.verifiedTitle")}</AlertTitle>
              <AlertDescription className="space-y-3">
                <p>{t("diagnostics.seq.connect.verifiedDescription")}</p>
                <dl className="grid gap-3 text-xs sm:grid-cols-2">
                  <div>
                    <dt className="text-muted-foreground">
                      {t("diagnostics.seq.connect.credential")}
                    </dt>
                    <dd className="font-medium">
                      {t("diagnostics.seq.connect.credentialAvailable")}
                    </dd>
                  </div>
                  <div>
                    <dt className="text-muted-foreground">
                      {t("diagnostics.seq.connect.testEvent")}
                    </dt>
                    <dd className="font-medium">
                      {t("diagnostics.seq.connect.testEventVerified")}
                    </dd>
                  </div>
                  {data.connection?.verifiedAtUtc ? (
                    <div>
                      <dt className="text-muted-foreground">
                        {t("diagnostics.seq.connect.verifiedAt")}
                      </dt>
                      <dd className="font-medium">
                        <span title={`${t("diagnostics.time.utcEvidence")}: ${diagnosticsUtcIso(data.connection.verifiedAtUtc)}`}>
                          {formatDiagnosticsLocalDateTime(data.connection.verifiedAtUtc, language)}
                        </span>
                      </dd>
                    </div>
                  ) : null}
                  {data.connection?.lastVerificationId ? (
                    <div>
                      <dt className="text-muted-foreground">
                        {t("diagnostics.seq.connect.verificationId")}
                      </dt>
                      <dd className="break-all font-mono font-medium">
                        {data.connection.lastVerificationId}
                      </dd>
                    </div>
                  ) : null}
                  {data.connection?.lastEventId ? (
                    <div>
                      <dt className="text-muted-foreground">
                        {t("diagnostics.seq.connect.eventId")}
                      </dt>
                      <dd className="break-all font-mono font-medium">
                        {data.connection.lastEventId}
                      </dd>
                    </div>
                  ) : null}
                </dl>
                <p className="text-xs text-muted-foreground">
                  {t("diagnostics.seq.connect.deliveryStillDisabled")}
                </p>
              </AlertDescription>
            </Alert>
          ) : (
            <>
              <Alert>
                <ShieldCheck className="h-4 w-4" />
                <AlertTitle>{t("diagnostics.seq.connect.securityTitle")}</AlertTitle>
                <AlertDescription>
                  {t("diagnostics.seq.connect.securityDescription")}
                </AlertDescription>
              </Alert>

              {isOwner ? (
                identityReady ? (
                  <form className="max-w-xl space-y-4" onSubmit={submit}>
                    <p className="text-xs text-muted-foreground">
                      {t(requiresRecentStepUp
                        ? "diagnostics.seq.connect.identityVerified"
                        : "diagnostics.seq.connect.identityNotRequired")}
                    </p>
                    <div className="space-y-2">
                      <Label htmlFor="seq-current-administrator-password">
                        {t("diagnostics.seq.connect.passwordLabel")}
                      </Label>
                      <Input
                        id="seq-current-administrator-password"
                        type="password"
                        autoComplete="current-password"
                        value={password}
                        disabled={connection.isPending}
                        onChange={(event) => setPassword(event.target.value)}
                      />
                      <p className="text-xs text-muted-foreground">
                        {t("diagnostics.seq.connect.passwordHelp")}
                      </p>
                    </div>
                    <Button
                      type="submit"
                      disabled={!password || connection.isPending}
                    >
                      <KeyRound className="mr-2 h-4 w-4" />
                      {connection.isPending
                        ? t("diagnostics.seq.connect.working")
                        : t("diagnostics.seq.connect.action")}
                    </Button>
                  </form>
                ) : (
                  <Button type="button" onClick={() => setStepUpOpen(true)}>
                    <ShieldCheck className="mr-2 h-4 w-4" />
                    {t("diagnostics.seq.connect.verifyIdentity")}
                  </Button>
                )
              ) : (
                <p className="text-sm text-muted-foreground">
                  {t("diagnostics.seq.connect.ownerOnly")}
                </p>
              )}
            </>
          )}

          {connection.isError ? (
            <Alert variant="destructive">
              <AlertTitle>{t("diagnostics.seq.connect.failedTitle")}</AlertTitle>
              <AlertDescription>
                {connectionProblem(connection.error, t)}
              </AlertDescription>
            </Alert>
          ) : null}

          {outcome ? (
            <p className="text-xs text-muted-foreground">
              {t("diagnostics.seq.connect.operationCompleted")} {outcome.operationId}
            </p>
          ) : null}
        </CardContent>
      </Card>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={() => setStepUpReady(true)}
      />
    </section>
  )
}

const connectionProblemKeys: Readonly<Record<string, TranslationKey>> = {
  seq_administrator_credentials_rejected:
    "diagnostics.seq.connect.problem.credentialsRejected",
  seq_administrator_access_denied:
    "diagnostics.seq.connect.problem.accessDenied",
  seq_administration_timeout:
    "diagnostics.seq.connect.problem.timeout",
  seq_administration_unreachable:
    "diagnostics.seq.connect.problem.unreachable",
  seq_administration_unavailable:
    "diagnostics.seq.connect.problem.unavailable",
  seq_administration_api_incompatible:
    "diagnostics.seq.connect.problem.incompatible",
}

function connectionProblem(
  error: unknown,
  t: (key: TranslationKey) => string,
) {
  const code = getMemApiProblemCode(error)
  const key = code ? connectionProblemKeys[code] : undefined
  return key
    ? t(key)
    : getMemApiProblemDetail(
        error,
        t("diagnostics.seq.connect.failedDescription"),
      )
}
