import { useEffect, useState } from "react"
import type { FormEvent } from "react"
import {
  KeyRound,
  RefreshCw,
  ShieldCheck,
  ShieldOff,
  UserPlus,
  UsersRound,
} from "lucide-react"

import type { TranslationKey } from "@/app/i18n/messages"
import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { OperatorChangePasswordDialog } from "@/features/auth/operator-change-password-dialog"
import { replaceWithFreshOperatorLogin } from "@/features/auth/operator-auth-navigation"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { useOperatorSession } from "@/features/auth/operator-session-context"
import {
  isStepUpRequiredOperatorDirectoryProblem,
  OperatorDirectoryProblemError,
  type CreatePendingManagedOperatorInput,
  type ManagedOperator,
  type OperatorEnrollmentGrant,
} from "@/features/operator/security/api/operator-directory.api"
import {
  useCreatePendingManagedOperator,
  useIssueManagedOperatorEnrollmentGrant,
  useManagedOperators,
  useRevokeManagedOperatorSessions,
  useSetManagedOperatorEnabled,
  useSetManagedOperatorRoles,
} from "@/features/operator/security/hooks/use-managed-operators"

const availableRoles = [
  "platform_owner",
  "operator",
  "auditor",
] as const

const roleTranslationKeys = {
  platform_owner: "account.role.platformOwner",
  operator: "account.role.operator",
  auditor: "account.role.auditor",
} as const

type RoleName = (typeof availableRoles)[number]

type LifecycleTarget = {
  operator: ManagedOperator
  nextEnabled: boolean
}

type StepUpContinuation = () => void

function roleLabel(role: string, translate: (key: TranslationKey) => string) {
  const key = roleTranslationKeys[role as keyof typeof roleTranslationKeys]
  return key ? translate(key) : role
}

function getStatusKey(operator: ManagedOperator): TranslationKey {
  if (operator.isBootstrapProvisioning) return "operatorDirectory.status.bootstrap"
  if (!operator.isEnabled) return "operatorDirectory.status.disabled"
  if (!operator.hasPassword || !operator.hasTotp) return "operatorDirectory.status.enrolmentRequired"
  return "operatorDirectory.status.active"
}

function formatLastLogin(value: string | null, language: string, notRecorded: string) {
  if (!value) return notRecorded

  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? notRecorded : date.toLocaleString(language)
}

function hasSameRoles(left: string[], right: string[]) {
  if (left.length !== right.length) return false

  const leftSorted = [...left].sort()
  const rightSorted = [...right].sort()

  return leftSorted.every((role, index) => role === rightSorted[index])
}

function isReadyForNormalAccess(operator: ManagedOperator) {
  return (
    !operator.isBootstrapProvisioning &&
    operator.hasPassword &&
    operator.hasTotp &&
    operator.roles.length > 0
  )
}

function problemMessage(
  error: unknown,
  translate: (key: TranslationKey) => string,
) {
  if (!(error instanceof OperatorDirectoryProblemError)) {
    return translate("operatorDirectory.problem.default")
  }

  const keyByCode: Record<string, TranslationKey> = {
    username_required: "operatorDirectory.problem.usernameRequired",
    username_invalid: "operatorDirectory.problem.usernameInvalid",
    username_unavailable: "operatorDirectory.problem.usernameUnavailable",
    email_invalid: "operatorDirectory.problem.emailInvalid",
    operator_role_required: "operatorDirectory.problem.roleRequired",
    operator_role_invalid: "operatorDirectory.problem.roleInvalid",
    operator_enrolment_required: "operatorDirectory.problem.enrolmentRequired",
    operator_self_management_not_allowed: "operatorDirectory.problem.selfManagement",
    last_active_platform_owner: "operatorDirectory.problem.lastActiveOwner",
    operator_not_found: "operatorDirectory.problem.notFound",
    enrollment_account_not_pending: "operatorDirectory.problem.enrollmentNotPending",
    enrollment_code_invalid: "operatorDirectory.problem.enrollmentCodeUnavailable",
    enrollment_reset_failed: "operatorDirectory.problem.enrollmentUnavailable",
  }

  return error.code && keyByCode[error.code]
    ? translate(keyByCode[error.code])
    : translate("operatorDirectory.problem.default")
}

export function OperatorDirectoryPage() {
  const { t, language } = useI18n()
  const { session } = useOperatorSession()
  const isPlatformOwner = session.roles.includes("platform_owner")
  const directory = useManagedOperators(isPlatformOwner)
  const createOperator = useCreatePendingManagedOperator()
  const issueEnrollmentGrant = useIssueManagedOperatorEnrollmentGrant()
  const setOperatorEnabled = useSetManagedOperatorEnabled()
  const setOperatorRoles = useSetManagedOperatorRoles()
  const revokeSessions = useRevokeManagedOperatorSessions()

  const [isCreateOpen, setIsCreateOpen] = useState(false)
  const [newUsername, setNewUsername] = useState("")
  const [newEmail, setNewEmail] = useState("")
  const [newRoles, setNewRoles] = useState<string[]>(["operator"])
  const [createdUsername, setCreatedUsername] = useState<string | null>(null)

  const [roleTarget, setRoleTarget] = useState<ManagedOperator | null>(null)
  const [selectedRoles, setSelectedRoles] = useState<string[]>([])
  const [isRoleConfirmationOpen, setIsRoleConfirmationOpen] = useState(false)

  const [lifecycleTarget, setLifecycleTarget] = useState<LifecycleTarget | null>(null)
  const [sessionTarget, setSessionTarget] = useState<ManagedOperator | null>(null)
  const [enrollmentTarget, setEnrollmentTarget] = useState<ManagedOperator | null>(null)
  const [issuedEnrollmentGrant, setIssuedEnrollmentGrant] = useState<OperatorEnrollmentGrant | null>(null)
  const [isStepUpOpen, setIsStepUpOpen] = useState(false)
  const [stepUpContinuation, setStepUpContinuation] = useState<StepUpContinuation | null>(null)
  const [isPasswordStepUpOpen, setIsPasswordStepUpOpen] = useState(false)
  const [isPasswordChangeOpen, setIsPasswordChangeOpen] = useState(false)

  if (!isPlatformOwner) {
    return (
      <Card className="mx-auto max-w-2xl">
        <CardHeader>
          <CardTitle>{t("operatorDirectory.accessDeniedTitle")}</CardTitle>
          <CardDescription>{t("operatorDirectory.accessDeniedDescription")}</CardDescription>
        </CardHeader>
      </Card>
    )
  }

  const openCreate = () => {
    createOperator.reset()
    setCreatedUsername(null)
    setIsCreateOpen(true)
  }

  const cancelCreate = () => {
    createOperator.reset()
    setIsCreateOpen(false)
  }

  const toggleNewRole = (role: RoleName, checked: boolean) => {
    setNewRoles((current) => {
      const next = checked
        ? [...new Set([...current, role])]
        : current.filter((item) => item !== role)

      return next.sort()
    })
  }

  useEffect(() => {
    if (
      !stepUpContinuation ||
      isStepUpOpen ||
      isRoleConfirmationOpen ||
      lifecycleTarget !== null ||
      sessionTarget !== null ||
      enrollmentTarget !== null
    ) {
      return
    }

    // Radix AlertDialog disables interaction outside its confirmation surface.
    // Let each confirmation close first, then open the independent verifier on
    // the next task. The continuation stays only in component memory and
    // retries exactly one action after a successful server-side grant.
    const timer = window.setTimeout(() => {
      setIsStepUpOpen(true)
    }, 0)

    return () => {
      window.clearTimeout(timer)
    }
  }, [
    enrollmentTarget,
    isRoleConfirmationOpen,
    isStepUpOpen,
    lifecycleTarget,
    sessionTarget,
    stepUpContinuation,
  ])

  const requireStepUp = (continuation: StepUpContinuation) => {
    setStepUpContinuation(() => continuation)
    setIsRoleConfirmationOpen(false)
    setLifecycleTarget(null)
    setSessionTarget(null)
    setEnrollmentTarget(null)
  }

  const changeStepUpOpen = (open: boolean) => {
    setIsStepUpOpen(open)

    if (!open) {
      setStepUpContinuation(null)
    }
  }

  const resumeAfterStepUp = () => {
    const continuation = stepUpContinuation
    setStepUpContinuation(null)
    continuation?.()
  }

  const runCreate = (input: CreatePendingManagedOperatorInput) => {
    createOperator.mutate(input, {
      onSuccess: (created) => {
        setCreatedUsername(created.username)
        setNewUsername("")
        setNewEmail("")
        setNewRoles(["operator"])
        setIsCreateOpen(false)
      },
      onError: (error) => {
        if (isStepUpRequiredOperatorDirectoryProblem(error)) {
          requireStepUp(() => runCreate(input))
        }
      },
    })
  }

  const submitCreate = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    createOperator.reset()
    runCreate({
      username: newUsername,
      email: newEmail,
      roles: newRoles,
    })
  }

  const openRoleEditor = (operator: ManagedOperator) => {
    setOperatorRoles.reset()
    setRoleTarget(operator)
    setSelectedRoles([...operator.roles].sort())
    setIsRoleConfirmationOpen(false)
  }

  const toggleEditedRole = (role: RoleName, checked: boolean) => {
    setSelectedRoles((current) => {
      const next = checked
        ? [...new Set([...current, role])]
        : current.filter((item) => item !== role)

      return next.sort()
    })
  }

  const closeRoleEditor = () => {
    setOperatorRoles.reset()
    setRoleTarget(null)
    setSelectedRoles([])
    setIsRoleConfirmationOpen(false)
  }

  const runRoleChange = (operator: ManagedOperator, roles: string[]) => {
    setOperatorRoles.mutate(
      { operatorId: operator.operatorId, roles },
      {
        onSuccess: () => closeRoleEditor(),
        onError: (error) => {
          if (isStepUpRequiredOperatorDirectoryProblem(error)) {
            requireStepUp(() => runRoleChange(operator, roles))
          }
        },
      },
    )
  }

  const openLifecycleConfirmation = (
    operator: ManagedOperator,
    nextEnabled: boolean,
  ) => {
    setOperatorEnabled.reset()
    setLifecycleTarget({ operator, nextEnabled })
  }

  const runLifecycleChange = (target: LifecycleTarget) => {
    setOperatorEnabled.mutate(
      {
        operatorId: target.operator.operatorId,
        isEnabled: target.nextEnabled,
      },
      {
        onSuccess: () => setLifecycleTarget(null),
        onError: (error) => {
          if (isStepUpRequiredOperatorDirectoryProblem(error)) {
            requireStepUp(() => runLifecycleChange(target))
          }
        },
      },
    )
  }

  const openSessionConfirmation = (operator: ManagedOperator) => {
    revokeSessions.reset()
    setSessionTarget(operator)
  }

  const runSessionRevocation = (operator: ManagedOperator) => {
    revokeSessions.mutate(
      { operatorId: operator.operatorId },
      {
        onSuccess: () => setSessionTarget(null),
        onError: (error) => {
          if (isStepUpRequiredOperatorDirectoryProblem(error)) {
            requireStepUp(() => runSessionRevocation(operator))
          }
        },
      },
    )
  }

  const openEnrollmentConfirmation = (operator: ManagedOperator) => {
    issueEnrollmentGrant.reset()
    setEnrollmentTarget(operator)
  }

  const runEnrollmentGrantIssue = (operator: ManagedOperator) => {
    issueEnrollmentGrant.mutate(
      { operatorId: operator.operatorId },
      {
        onSuccess: (issued) => {
          setEnrollmentTarget(null)
          setIssuedEnrollmentGrant(issued)
        },
        onError: (error) => {
          if (isStepUpRequiredOperatorDirectoryProblem(error)) {
            requireStepUp(() => runEnrollmentGrantIssue(operator))
          }
        },
      },
    )
  }

  const closeIssuedEnrollmentGrant = () => {
    setIssuedEnrollmentGrant(null)
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
        <div className="space-y-2">
          <div className="flex items-center gap-2 text-primary">
            <ShieldCheck className="h-5 w-5" />
            <span className="text-sm font-medium">{t("operatorDirectory.eyebrow")}</span>
          </div>
          <div>
            <h1 className="text-2xl font-semibold tracking-tight text-foreground">
              {t("operatorDirectory.title")}
            </h1>
            <p className="mt-1 max-w-3xl text-sm leading-relaxed text-muted-foreground">
              {t("operatorDirectory.description")}
            </p>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button type="button" variant="outline" onClick={() => void directory.refetch()}>
            <RefreshCw className={directory.isFetching ? "animate-spin" : undefined} />
            {t("operatorDirectory.refresh")}
          </Button>
          <Button type="button" onClick={openCreate}>
            <UserPlus />
            {t("operatorDirectory.create.open")}
          </Button>
        </div>
      </div>

      {createdUsername ? (
        <Alert>
          <UserPlus className="h-4 w-4" />
          <AlertTitle>{t("operatorDirectory.create.successTitle")}</AlertTitle>
          <AlertDescription>
            {t("operatorDirectory.create.successDescription", { username: createdUsername })}
          </AlertDescription>
        </Alert>
      ) : null}

      {issuedEnrollmentGrant ? (
        <Card className="border-primary/35">
          <CardHeader>
            <CardTitle>
              {t("operatorDirectory.enrollment.codeTitle", { username: issuedEnrollmentGrant.username })}
            </CardTitle>
            <CardDescription>{t("operatorDirectory.enrollment.codeDescription")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <Alert>
              <KeyRound className="h-4 w-4" />
              <AlertTitle>{t("operatorDirectory.enrollment.codeSafetyTitle")}</AlertTitle>
              <AlertDescription>{t("operatorDirectory.enrollment.codeSafetyDescription")}</AlertDescription>
            </Alert>

            <pre
              className="overflow-auto rounded-lg border border-border bg-muted p-4 text-sm"
              aria-label={t("operatorDirectory.enrollment.codeLabel")}
            >
              {issuedEnrollmentGrant.enrollmentCode}
            </pre>

            <p className="text-sm text-muted-foreground">
              {t("operatorDirectory.enrollment.expiresAt", {
                value: new Date(issuedEnrollmentGrant.expiresAtUtc).toLocaleString(language),
              })}
            </p>

            <div className="flex justify-end">
              <Button type="button" variant="outline" onClick={closeIssuedEnrollmentGrant}>
                {t("operatorDirectory.enrollment.closeCode")}
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : null}

      {isCreateOpen ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("operatorDirectory.create.title")}</CardTitle>
            <CardDescription>{t("operatorDirectory.create.description")}</CardDescription>
          </CardHeader>
          <CardContent>
            <form className="space-y-5" onSubmit={submitCreate}>
              <div className="grid gap-4 sm:grid-cols-2">
                <div className="space-y-2">
                  <Label htmlFor="pending-operator-username">
                    {t("operatorDirectory.create.usernameLabel")}
                  </Label>
                  <Input
                    id="pending-operator-username"
                    value={newUsername}
                    onChange={(event) => setNewUsername(event.target.value)}
                    placeholder={t("operatorDirectory.create.usernamePlaceholder")}
                    autoComplete="off"
                    disabled={createOperator.isPending}
                  />
                </div>
                <div className="space-y-2">
                  <Label htmlFor="pending-operator-email">
                    {t("operatorDirectory.create.emailLabel")}
                  </Label>
                  <Input
                    id="pending-operator-email"
                    value={newEmail}
                    onChange={(event) => setNewEmail(event.target.value)}
                    placeholder={t("operatorDirectory.create.emailPlaceholder")}
                    type="email"
                    autoComplete="off"
                    disabled={createOperator.isPending}
                  />
                </div>
              </div>

              <div className="space-y-2">
                <div className="text-sm font-medium">{t("operatorDirectory.create.rolesLabel")}</div>
                <div className="grid gap-2 sm:grid-cols-3">
                  {availableRoles.map((role) => (
                    <Label
                      key={role}
                      htmlFor={`pending-operator-role-${role}`}
                      className="rounded-lg border border-border bg-background/35 p-3"
                    >
                      <Checkbox
                        id={`pending-operator-role-${role}`}
                        checked={newRoles.includes(role)}
                        onCheckedChange={(checked) => toggleNewRole(role, checked === true)}
                        disabled={createOperator.isPending}
                      />
                      {roleLabel(role, t)}
                    </Label>
                  ))}
                </div>
              </div>

              {createOperator.error && !isStepUpRequiredOperatorDirectoryProblem(createOperator.error) ? (
                <Alert variant="destructive">
                  <AlertTitle>{t("operatorDirectory.create.errorTitle")}</AlertTitle>
                  <AlertDescription>{problemMessage(createOperator.error, t)}</AlertDescription>
                </Alert>
              ) : null}

              <div className="flex flex-wrap justify-end gap-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={cancelCreate}
                  disabled={createOperator.isPending}
                >
                  {t("operatorDirectory.create.cancel")}
                </Button>
                <Button
                  type="submit"
                  disabled={
                    createOperator.isPending ||
                    newUsername.trim().length === 0 ||
                    newRoles.length === 0
                  }
                >
                  <UserPlus />
                  {createOperator.isPending
                    ? t("operatorDirectory.create.submitting")
                    : t("operatorDirectory.create.submit")}
                </Button>
              </div>
            </form>
          </CardContent>
        </Card>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <UsersRound className="h-5 w-5" />
            {t("operatorDirectory.tableTitle")}
          </CardTitle>
          <CardDescription>{t("operatorDirectory.tableDescription")}</CardDescription>
        </CardHeader>
        <CardContent>
          {directory.isLoading ? (
            <p className="text-sm text-muted-foreground">{t("operatorDirectory.loading")}</p>
          ) : directory.isError ? (
            <div className="rounded-xl border border-destructive/30 bg-destructive/10 p-4 text-sm text-destructive">
              <div className="font-medium">{t("operatorDirectory.loadErrorTitle")}</div>
              <p className="mt-1 text-destructive/90">{t("operatorDirectory.loadErrorDescription")}</p>
            </div>
          ) : directory.data && directory.data.length > 0 ? (
            <Table className="min-w-[1040px]">
              <TableHeader>
                <TableRow>
                  <TableHead>{t("operatorDirectory.columns.operator")}</TableHead>
                  <TableHead>{t("operatorDirectory.columns.roles")}</TableHead>
                  <TableHead>{t("operatorDirectory.columns.mfa")}</TableHead>
                  <TableHead>{t("operatorDirectory.columns.status")}</TableHead>
                  <TableHead>{t("operatorDirectory.columns.lastLogin")}</TableHead>
                  <TableHead className="sticky right-0 z-30 min-w-52 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]">
                    {t("operatorDirectory.columns.actions")}
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {directory.data.map((operator) => {
                  const accountReady = isReadyForNormalAccess(operator)

                  return (
                    <TableRow key={operator.operatorId}>
                      <TableCell>
                        <div className="min-w-44 space-y-1">
                          <div className="flex flex-wrap items-center gap-2 font-medium text-foreground">
                            <span>{operator.username}</span>
                            {operator.isCurrentOperator ? (
                              <Badge variant="outline">{t("operatorDirectory.currentOperator")}</Badge>
                            ) : null}
                          </div>
                          {operator.email ? (
                            <div className="truncate text-xs text-muted-foreground">{operator.email}</div>
                          ) : null}
                        </div>
                      </TableCell>
                      <TableCell>
                        <div className="flex min-w-36 flex-wrap gap-1.5">
                          {operator.roles.length > 0 ? operator.roles.map((role) => (
                            <Badge key={role} variant="secondary" className="font-normal">
                              {roleLabel(role, t)}
                            </Badge>
                          )) : (
                            <span className="text-xs text-muted-foreground">{t("account.noRoles")}</span>
                          )}
                        </div>
                      </TableCell>
                      <TableCell>
                        <Badge variant={operator.hasTotp ? "secondary" : "outline"}>
                          {operator.hasTotp ? t("operatorDirectory.mfaConfigured") : t("operatorDirectory.mfaMissing")}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        <Badge variant={operator.isEnabled ? "secondary" : "outline"}>
                          {t(getStatusKey(operator))}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-sm text-muted-foreground">
                        {formatLastLogin(operator.lastLoginAtUtc, language, t("common.notRecorded"))}
                      </TableCell>
                      <TableCell className="sticky right-0 z-20 min-w-52 border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]">
                        {operator.isCurrentOperator ? (
                          <div className="flex min-w-48 flex-wrap justify-end gap-1.5">
                            <Button
                              type="button"
                              size="xs"
                              variant="outline"
                              onClick={() => setIsPasswordStepUpOpen(true)}
                            >
                              <KeyRound />
                              {t("account.changePassword")}
                            </Button>
                          </div>
                        ) : operator.isBootstrapProvisioning ? (
                          <span className="text-xs text-muted-foreground">
                            {t("operatorDirectory.actions.bootstrapUnavailable")}
                          </span>
                        ) : (
                          <div className="flex min-w-48 flex-wrap justify-end gap-1.5">
                            <Button
                              type="button"
                              size="xs"
                              variant="outline"
                              onClick={() => openRoleEditor(operator)}
                            >
                              {t("operatorDirectory.actions.editRoles")}
                            </Button>

                            {!operator.isEnabled && !accountReady ? (
                              <Button
                                type="button"
                                size="xs"
                                variant="outline"
                                onClick={() => openEnrollmentConfirmation(operator)}
                              >
                                <KeyRound />
                                {operator.enrollmentGrantExpiresAtUtc
                                  ? t("operatorDirectory.actions.replaceEnrollmentCode")
                                  : t("operatorDirectory.actions.issueEnrollmentCode")}
                              </Button>
                            ) : null}

                            {operator.isEnabled ? (
                              <Button
                                type="button"
                                size="xs"
                                variant="destructive"
                                onClick={() => openLifecycleConfirmation(operator, false)}
                              >
                                <ShieldOff />
                                {t("operatorDirectory.actions.disable")}
                              </Button>
                            ) : accountReady ? (
                              <Button
                                type="button"
                                size="xs"
                                variant="outline"
                                onClick={() => openLifecycleConfirmation(operator, true)}
                              >
                                <ShieldCheck />
                                {t("operatorDirectory.actions.enable")}
                              </Button>
                            ) : (
                              <span className="self-center text-xs text-muted-foreground">
                                {t("operatorDirectory.actions.enrolmentRequired")}
                              </span>
                            )}

                            {operator.hasPassword ? (
                              <Button
                                type="button"
                                size="xs"
                                variant="outline"
                                onClick={() => openSessionConfirmation(operator)}
                              >
                                <KeyRound />
                                {t("operatorDirectory.actions.revokeSessions")}
                              </Button>
                            ) : null}
                          </div>
                        )}
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          ) : (
            <p className="text-sm text-muted-foreground">{t("operatorDirectory.empty")}</p>
          )}
        </CardContent>
      </Card>

      {roleTarget ? (
        <Card>
          <CardHeader>
            <CardTitle>
              {t("operatorDirectory.roles.title", { username: roleTarget.username })}
            </CardTitle>
            <CardDescription>{t("operatorDirectory.roles.description")}</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-2 sm:grid-cols-3">
              {availableRoles.map((role) => (
                <Label
                  key={role}
                  htmlFor={`edit-operator-role-${role}`}
                  className="rounded-lg border border-border bg-background/35 p-3"
                >
                  <Checkbox
                    id={`edit-operator-role-${role}`}
                    checked={selectedRoles.includes(role)}
                    onCheckedChange={(checked) => toggleEditedRole(role, checked === true)}
                    disabled={setOperatorRoles.isPending}
                  />
                  {roleLabel(role, t)}
                </Label>
              ))}
            </div>

            {setOperatorRoles.error && !isStepUpRequiredOperatorDirectoryProblem(setOperatorRoles.error) ? (
              <Alert variant="destructive">
                <AlertTitle>{t("operatorDirectory.roles.errorTitle")}</AlertTitle>
                <AlertDescription>{problemMessage(setOperatorRoles.error, t)}</AlertDescription>
              </Alert>
            ) : null}

            <div className="flex flex-wrap justify-end gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={closeRoleEditor}
                disabled={setOperatorRoles.isPending}
              >
                {t("operatorDirectory.roles.cancel")}
              </Button>
              <Button
                type="button"
                onClick={() => {
                  setOperatorRoles.reset()
                  setIsRoleConfirmationOpen(true)
                }}
                disabled={
                  selectedRoles.length === 0 ||
                  hasSameRoles(selectedRoles, roleTarget.roles) ||
                  setOperatorRoles.isPending
                }
              >
                {t("operatorDirectory.roles.review")}
              </Button>
            </div>
          </CardContent>
        </Card>
      ) : null}

      <p className="text-sm text-muted-foreground">{t("operatorDirectory.followOn")}</p>

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

      <OperatorStepUpDialog
        open={isStepUpOpen}
        onOpenChange={changeStepUpOpen}
        onVerified={resumeAfterStepUp}
      />

      <ConfirmationDialog
        open={isRoleConfirmationOpen}
        onOpenChange={(open) => {
          if (!open && !setOperatorRoles.isPending) {
            setOperatorRoles.reset()
            setIsRoleConfirmationOpen(false)
          }
        }}
        title={t("operatorDirectory.roles.confirmTitle")}
        description={
          roleTarget
            ? t("operatorDirectory.roles.confirmDescription", { username: roleTarget.username })
            : ""
        }
        confirmLabel={t("operatorDirectory.roles.confirm")}
        confirmingLabel={t("operatorDirectory.roles.confirming")}
        cancelLabel={t("operatorDirectory.roles.cancel")}
        onConfirm={() => {
          if (!roleTarget) return

          runRoleChange(roleTarget, selectedRoles)
        }}
        isConfirming={setOperatorRoles.isPending}
        confirmDisabled={selectedRoles.length === 0 || !roleTarget}
      >
        {setOperatorRoles.error && !isStepUpRequiredOperatorDirectoryProblem(setOperatorRoles.error) ? (
          <Alert variant="destructive">
            <AlertTitle>{t("operatorDirectory.roles.errorTitle")}</AlertTitle>
            <AlertDescription>{problemMessage(setOperatorRoles.error, t)}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>

      <ConfirmationDialog
        open={lifecycleTarget !== null}
        onOpenChange={(open) => {
          if (!open && !setOperatorEnabled.isPending) {
            setOperatorEnabled.reset()
            setLifecycleTarget(null)
          }
        }}
        title={
          lifecycleTarget?.nextEnabled
            ? t("operatorDirectory.enable.confirmTitle")
            : t("operatorDirectory.disable.confirmTitle")
        }
        description={
          lifecycleTarget
            ? lifecycleTarget.nextEnabled
              ? t("operatorDirectory.enable.confirmDescription", {
                  username: lifecycleTarget.operator.username,
                })
              : t("operatorDirectory.disable.confirmDescription", {
                  username: lifecycleTarget.operator.username,
                })
            : ""
        }
        confirmLabel={
          lifecycleTarget?.nextEnabled
            ? t("operatorDirectory.enable.confirm")
            : t("operatorDirectory.disable.confirm")
        }
        confirmingLabel={t("operatorDirectory.actions.working")}
        cancelLabel={t("common.close")}
        confirmVariant={lifecycleTarget?.nextEnabled ? "default" : "destructive"}
        onConfirm={() => {
          if (!lifecycleTarget) return

          runLifecycleChange(lifecycleTarget)
        }}
        isConfirming={setOperatorEnabled.isPending}
      >
        {setOperatorEnabled.error && !isStepUpRequiredOperatorDirectoryProblem(setOperatorEnabled.error) ? (
          <Alert variant="destructive">
            <AlertTitle>{t("operatorDirectory.actions.errorTitle")}</AlertTitle>
            <AlertDescription>{problemMessage(setOperatorEnabled.error, t)}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>

      <ConfirmationDialog
        open={sessionTarget !== null}
        onOpenChange={(open) => {
          if (!open && !revokeSessions.isPending) {
            revokeSessions.reset()
            setSessionTarget(null)
          }
        }}
        title={t("operatorDirectory.sessions.confirmTitle")}
        description={
          sessionTarget
            ? t("operatorDirectory.sessions.confirmDescription", {
                username: sessionTarget.username,
              })
            : ""
        }
        confirmLabel={t("operatorDirectory.sessions.confirm")}
        confirmingLabel={t("operatorDirectory.actions.working")}
        cancelLabel={t("common.close")}
        confirmVariant="destructive"
        onConfirm={() => {
          if (!sessionTarget) return

          runSessionRevocation(sessionTarget)
        }}
        isConfirming={revokeSessions.isPending}
      >
        {revokeSessions.error && !isStepUpRequiredOperatorDirectoryProblem(revokeSessions.error) ? (
          <Alert variant="destructive">
            <AlertTitle>{t("operatorDirectory.actions.errorTitle")}</AlertTitle>
            <AlertDescription>{problemMessage(revokeSessions.error, t)}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>

      <ConfirmationDialog
        open={enrollmentTarget !== null}
        onOpenChange={(open) => {
          if (!open && !issueEnrollmentGrant.isPending) {
            issueEnrollmentGrant.reset()
            setEnrollmentTarget(null)
          }
        }}
        title={t("operatorDirectory.enrollment.confirmTitle")}
        description={
          enrollmentTarget
            ? t("operatorDirectory.enrollment.confirmDescription", {
                username: enrollmentTarget.username,
              })
            : ""
        }
        confirmLabel={t("operatorDirectory.enrollment.confirm")}
        confirmingLabel={t("operatorDirectory.enrollment.confirming")}
        cancelLabel={t("common.close")}
        onConfirm={() => {
          if (!enrollmentTarget) return

          runEnrollmentGrantIssue(enrollmentTarget)
        }}
        isConfirming={issueEnrollmentGrant.isPending}
      >
        {issueEnrollmentGrant.error && !isStepUpRequiredOperatorDirectoryProblem(issueEnrollmentGrant.error) ? (
          <Alert variant="destructive">
            <AlertTitle>{t("operatorDirectory.actions.errorTitle")}</AlertTitle>
            <AlertDescription>{problemMessage(issueEnrollmentGrant.error, t)}</AlertDescription>
          </Alert>
        ) : null}
      </ConfirmationDialog>
    </div>
  )
}
