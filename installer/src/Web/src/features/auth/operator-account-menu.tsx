import { useState } from "react"
import { ChevronDown, KeyRound, LogOut, ShieldCheck, UserRound } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { OperatorChangePasswordDialog } from "@/features/auth/operator-change-password-dialog"
import { replaceWithFreshOperatorLogin } from "@/features/auth/operator-auth-navigation"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import { regenerateOperatorRecoveryCodes } from "@/features/auth/control-plane-auth"
import { OperatorRecoveryCodesDialog } from "@/features/auth/operator-recovery-codes-dialog"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"

const roleTranslationKeys = {
  platform_owner: "account.role.platformOwner",
  operator: "account.role.operator",
  auditor: "account.role.auditor",
} as const

function getRoleLabel(role: string, translate: (key: TranslationKey) => string) {
  const key = roleTranslationKeys[role as keyof typeof roleTranslationKeys]
  return key ? translate(key) : role
}

export function OperatorAccountMenu() {
  const { t } = useI18n()
  const { session, signOut } = useOperatorSession()
  const [open, setOpen] = useState(false)
  const [isSigningOut, setIsSigningOut] = useState(false)
  const [signOutFailed, setSignOutFailed] = useState(false)
  const [isStepUpOpen, setIsStepUpOpen] = useState(false)
  const [isPasswordStepUpOpen, setIsPasswordStepUpOpen] = useState(false)
  const [isPasswordChangeOpen, setIsPasswordChangeOpen] = useState(false)
  const [isRecoveryCodeConfirmationOpen, setIsRecoveryCodeConfirmationOpen] = useState(false)
  const [isRecoveryCodeStepUpOpen, setIsRecoveryCodeStepUpOpen] = useState(false)
  const [recoveryCodeRegenerationFailed, setRecoveryCodeRegenerationFailed] = useState(false)
  const [newRecoveryCodes, setNewRecoveryCodes] = useState<string[] | null>(null)

  const displayName = session.displayName ?? t("account.unknownOperator")

  function beginRecoveryCodeRegeneration() {
    setRecoveryCodeRegenerationFailed(false)
    setNewRecoveryCodes(null)
    setIsRecoveryCodeConfirmationOpen(false)
    setIsRecoveryCodeStepUpOpen(true)
  }

  async function regenerateRecoveryCodesAfterStepUp() {
    try {
      const result = await regenerateOperatorRecoveryCodes()

      if (result.status === "regenerated") {
        setRecoveryCodeRegenerationFailed(false)
        setNewRecoveryCodes(result.recoveryCodes)
        return
      }
    } catch {
      // The generic confirmation error below deliberately carries no raw
      // response body or recovery-code material.
    }

    // A failed request never retains raw code material. Return to the
    // confirmation only; a new attempt requires a new step-up verification.
    setRecoveryCodeRegenerationFailed(true)
    setIsRecoveryCodeConfirmationOpen(true)
  }

  async function handleSignOut() {
    if (isSigningOut) {
      return
    }

    setIsSigningOut(true)
    setSignOutFailed(false)

    try {
      await signOut()
    } catch {
      setSignOutFailed(true)
      setIsSigningOut(false)
    }
  }

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-label={t("account.openMenu")}
          title={t("account.openMenu")}
        >
          <UserRound className="h-4 w-4" />
          <span className="hidden max-w-40 truncate sm:inline">{displayName}</span>
          <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
        </Button>
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="w-64">
        <DropdownMenuLabel className="space-y-1">
          <div className="truncate text-sm font-semibold text-foreground">{displayName}</div>
          <div className="text-xs font-normal text-muted-foreground">
            {t("account.namedOperator")}
          </div>
        </DropdownMenuLabel>

        <DropdownMenuSeparator />

        <div className="space-y-2 px-2 py-1.5">
          <div className="text-xs font-medium text-muted-foreground">{t("account.rolesLabel")}</div>
          <div className="flex flex-wrap gap-1.5">
            {session.roles.length > 0 ? (
              session.roles.map((role) => (
                <Badge key={role} variant="secondary" className="font-normal">
                  {getRoleLabel(role, t)}
                </Badge>
              ))
            ) : (
              <span className="text-xs text-muted-foreground">{t("account.noRoles")}</span>
            )}
          </div>
        </div>

        <DropdownMenuSeparator />

        <DropdownMenuItem
          onSelect={(event) => {
            event.preventDefault()
            setOpen(false)
            setIsStepUpOpen(true)
          }}
        >
          <ShieldCheck className="h-4 w-4" />
          {t("account.verifyIdentity")}
        </DropdownMenuItem>

        <DropdownMenuItem
          onSelect={(event) => {
            event.preventDefault()
            setOpen(false)
            setIsPasswordStepUpOpen(true)
          }}
        >
          <KeyRound className="h-4 w-4" />
          {t("account.changePassword")}
        </DropdownMenuItem>

        <DropdownMenuItem
          onSelect={(event) => {
            event.preventDefault()
            setOpen(false)
            setRecoveryCodeRegenerationFailed(false)
            setNewRecoveryCodes(null)
            setIsRecoveryCodeConfirmationOpen(true)
          }}
        >
          <KeyRound className="h-4 w-4" />
          {t("account.regenerateRecoveryCodes")}
        </DropdownMenuItem>

        {signOutFailed ? (
          <div role="status" className="px-2 pb-1.5 text-xs text-destructive">
            {t("account.signOutFailed")}
          </div>
        ) : null}

        <DropdownMenuItem
          variant="destructive"
          disabled={isSigningOut}
          onSelect={(event) => {
            event.preventDefault()
            void handleSignOut()
          }}
        >
          <LogOut className="h-4 w-4" />
          {isSigningOut ? t("account.signingOut") : t("account.signOut")}
        </DropdownMenuItem>
      </DropdownMenuContent>
      <OperatorStepUpDialog open={isStepUpOpen} onOpenChange={setIsStepUpOpen} />

      <OperatorStepUpDialog
        open={isPasswordStepUpOpen}
        onOpenChange={setIsPasswordStepUpOpen}
        onVerified={() => setIsPasswordChangeOpen(true)}
      />

      <OperatorChangePasswordDialog
        open={isPasswordChangeOpen}
        onOpenChange={setIsPasswordChangeOpen}
        onChanged={replaceWithFreshOperatorLogin}
      />

      <ConfirmationDialog
        open={isRecoveryCodeConfirmationOpen}
        onOpenChange={(nextOpen) => {
          setIsRecoveryCodeConfirmationOpen(nextOpen)

          if (!nextOpen) {
            setRecoveryCodeRegenerationFailed(false)
          }
        }}
        title={t("recoveryCodes.confirmTitle")}
        description={t("recoveryCodes.confirmDescription")}
        confirmLabel={t("recoveryCodes.confirmAction")}
        onConfirm={beginRecoveryCodeRegeneration}
      >
        {recoveryCodeRegenerationFailed ? (
          <Alert variant="destructive">
            <AlertTitle>{t("recoveryCodes.errorTitle")}</AlertTitle>
            <AlertDescription>{t("recoveryCodes.errorDescription")}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>

      <OperatorStepUpDialog
        open={isRecoveryCodeStepUpOpen}
        onOpenChange={setIsRecoveryCodeStepUpOpen}
        onVerified={() => {
          void regenerateRecoveryCodesAfterStepUp()
        }}
      />

      <OperatorRecoveryCodesDialog
        recoveryCodes={newRecoveryCodes}
        onAcknowledge={() => {
          setNewRecoveryCodes(null)
          setRecoveryCodeRegenerationFailed(false)
        }}
      />
    </DropdownMenu>
  )
}
