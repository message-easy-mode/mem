import { useEffect, useState } from "react"
import { KeyRound, UserCheck, UserX } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { isStepUpRequiredRuntimeStackProblem } from "../api/stacks.api"
import type {
  ReactivateRuntimeStackUserRequest,
  RuntimeStackUserResponse,
} from "../api/stacks.types"
import {
  useDeactivateRuntimeStackUser,
  useReactivateRuntimeStackUser,
} from "../hooks/use-runtime-stacks"

type PendingLifecycleAction =
  | Readonly<{ kind: "deactivate" }>
  | Readonly<{ kind: "reactivate"; request: ReactivateRuntimeStackUserRequest }>

export function StackUserLifecycleActions({
  slugOrId,
  user,
  activeAdminCount,
  onResetPassword,
}: {
  slugOrId: string
  user: RuntimeStackUserResponse
  activeAdminCount: number
  onResetPassword: () => void
}) {
  const { t } = useI18n()
  const deactivateUser = useDeactivateRuntimeStackUser(slugOrId)
  const reactivateUser = useReactivateRuntimeStackUser(slugOrId)
  const [deactivateOpen, setDeactivateOpen] = useState(false)
  const [reactivateOpen, setReactivateOpen] = useState(false)
  const [newPassword, setNewPassword] = useState("")
  const [confirmation, setConfirmation] = useState("")
  const [pendingAction, setPendingAction] = useState<PendingLifecycleAction | null>(null)
  const [stepUpPending, setStepUpPending] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)

  useEffect(() => {
    if (!stepUpPending || deactivateOpen || reactivateOpen) {
      return
    }

    const timer = window.setTimeout(() => setStepUpOpen(true), 0)
    return () => window.clearTimeout(timer)
  }, [deactivateOpen, reactivateOpen, stepUpPending])

  const active = user.status.trim().toLowerCase() === "active" && Boolean(user.matrixUserId)
  const deactivated = user.status.trim().toLowerCase() === "deactivated" && Boolean(user.matrixUserId)
  const lastActiveAdmin = active && user.isAdmin && activeAdminCount <= 1
  const passwordsMatch = newPassword.length >= 8 && newPassword === confirmation

  function clearLifecycleState() {
    setDeactivateOpen(false)
    setReactivateOpen(false)
    setNewPassword("")
    setConfirmation("")
    setPendingAction(null)
    setStepUpPending(false)
    setStepUpOpen(false)
  }

  function performDeactivate() {
    deactivateUser.mutate(
      { userId: user.id, request: { erase: false } },
      {
        onSuccess: clearLifecycleState,
        onError: (error) => {
          if (!isStepUpRequiredRuntimeStackProblem(error)) {
            return
          }
          deactivateUser.reset()
          setDeactivateOpen(false)
          setPendingAction({ kind: "deactivate" })
          setStepUpPending(true)
        },
      },
    )
  }

  function performReactivate(request: ReactivateRuntimeStackUserRequest) {
    reactivateUser.mutate(
      { userId: user.id, request },
      {
        onSuccess: clearLifecycleState,
        onError: (error) => {
          if (!isStepUpRequiredRuntimeStackProblem(error)) {
            return
          }
          reactivateUser.reset()
          setReactivateOpen(false)
          setNewPassword("")
          setConfirmation("")
          setPendingAction({ kind: "reactivate", request })
          setStepUpPending(true)
        },
      },
    )
  }

  function resumeAfterStepUp() {
    const pending = pendingAction
    setPendingAction(null)
    setStepUpPending(false)
    setStepUpOpen(false)

    if (pending?.kind === "deactivate") {
      performDeactivate()
    } else if (pending?.kind === "reactivate") {
      performReactivate(pending.request)
    }
  }

  return (
    <>
      <div className="flex flex-wrap justify-end gap-2">
        {active ? (
          <>
            <Button type="button" variant="outline" size="sm" onClick={onResetPassword}>
              <KeyRound className="h-4 w-4" />
              {t("stacks.users.passwordResetAction")}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={lastActiveAdmin}
              title={lastActiveAdmin ? t("stacks.users.deactivateLastAdminBlocked") : undefined}
              onClick={() => {
                deactivateUser.reset()
                setDeactivateOpen(true)
              }}
            >
              <UserX className="h-4 w-4" />
              {t("stacks.users.deactivateAction")}
            </Button>
          </>
        ) : deactivated ? (
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() => {
              reactivateUser.reset()
              setReactivateOpen(true)
            }}
          >
            <UserCheck className="h-4 w-4" />
            {t("stacks.users.reactivateAction")}
          </Button>
        ) : (
          <span className="text-xs text-muted-foreground">
            {t("stacks.users.passwordResetUnavailable")}
          </span>
        )}
      </div>

      <ConfirmationDialog
        open={deactivateOpen}
        onOpenChange={setDeactivateOpen}
        title={t("stacks.users.deactivateTitle")}
        description={t("stacks.users.deactivateDescription", {
          matrixUserId: user.matrixUserId ?? user.username,
        })}
        confirmLabel={t("stacks.users.deactivateConfirm")}
        confirmingLabel={t("stacks.users.deactivating")}
        cancelLabel={t("stacks.users.cancelAction")}
        confirmVariant="destructive"
        onConfirm={performDeactivate}
        isConfirming={deactivateUser.isPending}
      >
        {deactivateUser.error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.users.deactivateFailedTitle")}</AlertTitle>
            <AlertDescription>{deactivateUser.error.message}</AlertDescription>
          </Alert>
        ) : null}
        <p className="text-sm text-muted-foreground">
          {t("stacks.users.deactivateConsequences")}
        </p>
      </ConfirmationDialog>

      <ConfirmationDialog
        open={reactivateOpen}
        onOpenChange={(open) => {
          setReactivateOpen(open)
          if (!open && !reactivateUser.isPending) {
            setNewPassword("")
            setConfirmation("")
          }
        }}
        title={t("stacks.users.reactivateTitle")}
        description={t("stacks.users.reactivateDescription", {
          matrixUserId: user.matrixUserId ?? user.username,
        })}
        confirmLabel={t("stacks.users.reactivateConfirm")}
        confirmingLabel={t("stacks.users.reactivating")}
        cancelLabel={t("stacks.users.cancelAction")}
        onConfirm={() => performReactivate({ newPassword })}
        isConfirming={reactivateUser.isPending}
        confirmDisabled={!passwordsMatch}
      >
        {reactivateUser.error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.users.reactivateFailedTitle")}</AlertTitle>
            <AlertDescription>{reactivateUser.error.message}</AlertDescription>
          </Alert>
        ) : null}
        <div className="space-y-2">
          <Label htmlFor={`reactivate-password-${user.id}`}>
            {t("stacks.users.passwordResetNewPasswordLabel")}
          </Label>
          <Input
            id={`reactivate-password-${user.id}`}
            type="password"
            value={newPassword}
            minLength={8}
            onChange={(event) => setNewPassword(event.target.value)}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor={`reactivate-confirm-${user.id}`}>
            {t("stacks.users.passwordResetConfirmationLabel")}
          </Label>
          <Input
            id={`reactivate-confirm-${user.id}`}
            type="password"
            value={confirmation}
            minLength={8}
            onChange={(event) => setConfirmation(event.target.value)}
          />
          {confirmation && newPassword !== confirmation ? (
            <p className="text-sm text-destructive">{t("stacks.users.passwordResetMismatch")}</p>
          ) : null}
        </div>
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) {
            setPendingAction(null)
            setStepUpPending(false)
          }
        }}
        onVerified={resumeAfterStepUp}
      />
    </>
  )
}
