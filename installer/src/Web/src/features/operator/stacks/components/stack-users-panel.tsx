import { useEffect, useMemo, useRef, useState } from "react"
import type { FormEvent } from "react"
import {
  Database,
  KeyRound,
  LockKeyhole,
  RefreshCw,
  ShieldCheck,
  UserPlus,
  Users,
} from "lucide-react"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  getRuntimeStackProblemDetail,
  isStepUpRequiredRuntimeStackProblem,
} from "../api/stacks.api"
import type {
  CreateRuntimeStackUserRequest,
  ResetRuntimeStackUserPasswordRequest,
  RuntimeStackUserResponse,
  RuntimeStackUsersResponse,
} from "../api/stacks.types"
import { StackUserLifecycleActions } from "./stack-user-lifecycle-actions"
import {
  useCreateRuntimeStackFirstAdmin,
  useCreateRuntimeStackUser,
  useResetRuntimeStackUserPassword,
  useRuntimeStackUsers,
  useSynchronizeRuntimeStackUsers,
} from "../hooks/use-runtime-stacks"

const userStatusTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  active: "stacks.users.status.active",
  synced: "stacks.users.status.synced",
  deactivated: "stacks.users.status.deactivated",
  missing: "stacks.users.status.missing",
  pending: "stacks.users.status.pending",
  failed: "stacks.users.status.failed",
  error: "stacks.users.status.error",
}

const userOriginTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  "mem-created": "stacks.users.origin.memCreated",
  "mem-creation-unconfirmed": "stacks.users.origin.memCreationUnconfirmed",
  "synapse-discovered": "stacks.users.origin.synapseDiscovered",
}

type PendingHighRiskAction = Readonly<{
  user: RuntimeStackUserResponse
  request: ResetRuntimeStackUserPasswordRequest
}>

export function StackUsersPanel({ slugOrId }: { slugOrId: string }) {
  const { language, t } = useI18n()
  const usersQuery = useRuntimeStackUsers(slugOrId)
  const synchronizeUsers = useSynchronizeRuntimeStackUsers(slugOrId)
  const resetPassword = useResetRuntimeStackUserPassword(slugOrId)
  const automaticAttemptKey = useRef<string | null>(null)
  const [selectedUser, setSelectedUser] = useState<RuntimeStackUserResponse | null>(null)
  const [pendingHighRiskAction, setPendingHighRiskAction] =
    useState<PendingHighRiskAction | null>(null)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [passwordResetResult, setPasswordResetResult] = useState<{
    matrixUserId: string
  } | null>(null)
  const inventory = usersQuery.data
  const inventoryStatus = inventory?.inventoryStatus?.trim().toLowerCase() ?? null
  const users = inventory?.users ?? []
  const synchronized = inventoryStatus === "synchronized" && inventory?.canCreateUsers === true
  const synchronizing = synchronizeUsers.isPending

  useEffect(() => {
    if (inventoryStatus !== "not_synchronized") {
      automaticAttemptKey.current = null
      return
    }

    const attemptKey = `${slugOrId}:${inventoryStatus}`

    if (automaticAttemptKey.current === attemptKey) {
      return
    }

    automaticAttemptKey.current = attemptKey
    synchronizeUsers.mutate()
  }, [inventoryStatus, slugOrId, synchronizeUsers])

  function performPasswordReset(
    user: RuntimeStackUserResponse,
    request: ResetRuntimeStackUserPasswordRequest,
  ) {
    resetPassword.mutate({ userId: user.id, request }, {
      onSuccess: (result) => {
        setPendingHighRiskAction(null)
        setStepUpOpen(false)
        setSelectedUser(null)
        setPasswordResetResult({ matrixUserId: result.matrixUserId })
      },
      onError: (error) => {
        if (!isStepUpRequiredRuntimeStackProblem(error)) {
          return
        }

        resetPassword.reset()
        setPendingHighRiskAction({ user, request })
        setStepUpOpen(true)
      },
    })
  }

  function resumeHighRiskActionAfterStepUp() {
    const pending = pendingHighRiskAction
    setPendingHighRiskAction(null)
    setStepUpOpen(false)

    if (!pending) {
      return
    }

    performPasswordReset(pending.user, pending.request)
  }

  function changeStepUpOpen(open: boolean) {
    setStepUpOpen(open)

    if (!open) {
      setPendingHighRiskAction(null)
    }
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <Users className="h-5 w-5" />
              {t("stacks.users.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("stacks.users.description")}
            </p>
          </div>
          <InventoryBadge
            inventory={inventory}
            loading={usersQuery.isLoading}
            synchronizing={synchronizing}
            synchronizationFailed={Boolean(synchronizeUsers.error)}
          />
        </div>
      </CardHeader>
      <CardContent className="space-y-6">
        {usersQuery.error && (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.users.loadErrorTitle")}</AlertTitle>
            <AlertDescription>{usersQuery.error.message}</AlertDescription>
          </Alert>
        )}

        {inventory ? (
          <>
            <InventoryStatusPanel
              inventory={inventory}
              synchronizing={synchronizing}
              synchronizationFailed={Boolean(synchronizeUsers.error)}
              onSynchronize={() => synchronizeUsers.mutate()}
            />

            {synchronized ? (
              inventory.requiresFirstAdmin ? (
                <FirstAdminForm slugOrId={slugOrId} />
              ) : (
                <CreateUserForm slugOrId={slugOrId} />
              )
            ) : null}

            {passwordResetResult ? (
              <Alert className="border-emerald-500/20 bg-emerald-500/10">
                <KeyRound className="h-4 w-4" />
                <AlertTitle>{t("stacks.users.passwordResetSucceededTitle")}</AlertTitle>
                <AlertDescription>
                  {t("stacks.users.passwordResetSucceededDescription", {
                    matrixUserId: passwordResetResult.matrixUserId,
                  })}
                </AlertDescription>
              </Alert>
            ) : null}

            {(users.length > 0 || (synchronized && !inventory.requiresFirstAdmin)) && (
              <UsersTable
                slugOrId={slugOrId}
                users={users}
                loading={usersQuery.isLoading}
                synchronized={synchronized}
                activeAdminCount={inventory.activeAdminCount}
                onResetPassword={(user) => {
                  resetPassword.reset()
                  setPasswordResetResult(null)
                  setSelectedUser(user)
                }}
              />
            )}

            {selectedUser ? (
              <PasswordResetPanel
                user={selectedUser}
                pending={resetPassword.isPending}
                error={resetPassword.error}
                onCancel={() => {
                  resetPassword.reset()
                  setSelectedUser(null)
                }}
                onSubmit={(request) => performPasswordReset(selectedUser, request)}
              />
            ) : null}

            {inventory.inventoryLastSynchronizedAtUtc && (
              <p className="text-xs text-muted-foreground">
                {t("stacks.users.lastSynchronized", {
                  date: formatDateTime(inventory.inventoryLastSynchronizedAtUtc, language),
                })}
              </p>
            )}
          </>
        ) : usersQuery.isLoading ? (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("stacks.users.loading")}
          </div>
        ) : (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("stacks.users.inventoryUnavailableDescription")}
          </div>
        )}
      </CardContent>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={changeStepUpOpen}
        onVerified={resumeHighRiskActionAfterStepUp}
      />
    </Card>
  )
}

function InventoryBadge({
  inventory,
  loading,
  synchronizing,
  synchronizationFailed,
}: {
  inventory: RuntimeStackUsersResponse | undefined
  loading: boolean
  synchronizing: boolean
  synchronizationFailed: boolean
}) {
  const { t } = useI18n()

  if (loading && !inventory) {
    return <Badge variant="outline">{t("stacks.users.loadingBadge")}</Badge>
  }

  if (!inventory) {
    return <Badge variant="outline">{t("stacks.users.inventoryUnavailableBadge")}</Badge>
  }

  if (synchronizing) {
    return (
      <Badge variant="outline" className="border-sky-500/30 text-sky-700 dark:text-sky-300">
        {t("stacks.users.inventorySynchronizingBadge")}
      </Badge>
    )
  }

  if (inventory.inventoryStatus === "failed" || synchronizationFailed) {
    return (
      <Badge variant="outline" className="border-red-500/30 text-red-700 dark:text-red-300">
        {t("stacks.users.inventoryFailedBadge")}
      </Badge>
    )
  }

  if (inventory.inventoryStatus !== "synchronized") {
    return (
      <Badge variant="outline" className="border-amber-500/30 text-amber-700 dark:text-amber-300">
        {t("stacks.users.inventoryNotSynchronizedBadge")}
      </Badge>
    )
  }

  if (inventory.requiresFirstAdmin) {
    return (
      <Badge variant="outline" className="border-amber-500/30 text-amber-700 dark:text-amber-300">
        {t("stacks.users.firstAdminRequiredBadge")}
      </Badge>
    )
  }

  return (
    <Badge variant="outline" className="border-emerald-500/30 text-emerald-700 dark:text-emerald-300">
      {t("stacks.users.inventorySynchronizedBadge")}
    </Badge>
  )
}

function InventoryStatusPanel({
  inventory,
  synchronizing,
  synchronizationFailed,
  onSynchronize,
}: {
  inventory: RuntimeStackUsersResponse
  synchronizing: boolean
  synchronizationFailed: boolean
  onSynchronize: () => void
}) {
  const { t } = useI18n()

  if (synchronizing) {
    return (
      <Alert className="border-sky-500/20 bg-sky-500/10">
        <RefreshCw className="h-4 w-4 animate-spin" />
        <AlertTitle>{t("stacks.users.inventorySynchronizingTitle")}</AlertTitle>
        <AlertDescription>{t("stacks.users.inventorySynchronizingDescription")}</AlertDescription>
      </Alert>
    )
  }

  if (inventory.inventoryStatus === "synchronized") {
    return (
      <div className="flex flex-col gap-3 rounded-xl border border-emerald-500/20 bg-emerald-500/10 p-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-start gap-3">
          <Database className="mt-0.5 h-5 w-5 text-emerald-700 dark:text-emerald-300" />
          <div>
            <div className="font-medium">{t("stacks.users.inventorySynchronizedTitle")}</div>
            <p className="text-sm text-muted-foreground">
              {t("stacks.users.inventorySynchronizedDescription", {
                count: inventory.inventoryUserCount ?? inventory.users.length,
                admins: inventory.activeAdminCount,
              })}
            </p>
          </div>
        </div>
        <Button type="button" variant="outline" size="sm" onClick={onSynchronize}>
          <RefreshCw className="h-4 w-4" />
          {t("stacks.users.synchronizeAction")}
        </Button>
      </div>
    )
  }

  if (inventory.inventoryStatus === "failed" || synchronizationFailed) {
    return (
      <Alert variant="destructive">
        <AlertTitle>{t("stacks.users.inventoryFailedTitle")}</AlertTitle>
        <AlertDescription className="space-y-3">
          <p>{inventory.detail ?? t("stacks.users.inventoryFailedDescription")}</p>
          <Button type="button" variant="outline" size="sm" onClick={onSynchronize}>
            <RefreshCw className="h-4 w-4" />
            {t("stacks.users.retrySynchronizationAction")}
          </Button>
        </AlertDescription>
      </Alert>
    )
  }

  return (
    <Alert className="border-amber-500/20 bg-amber-500/10">
      <Database className="h-4 w-4" />
      <AlertTitle>{t("stacks.users.inventoryNotSynchronizedTitle")}</AlertTitle>
      <AlertDescription className="space-y-3">
        <p>{t("stacks.users.inventoryNotSynchronizedDescription")}</p>
        <Button type="button" variant="outline" size="sm" onClick={onSynchronize}>
          <RefreshCw className="h-4 w-4" />
          {t("stacks.users.synchronizeAction")}
        </Button>
      </AlertDescription>
    </Alert>
  )
}

function PasswordResetPanel({
  user,
  pending,
  error,
  onCancel,
  onSubmit,
}: {
  user: RuntimeStackUserResponse
  pending: boolean
  error: Error | null
  onCancel: () => void
  onSubmit: (request: ResetRuntimeStackUserPasswordRequest) => void
}) {
  const { t } = useI18n()
  const [password, setPassword] = useState("")
  const [confirmation, setConfirmation] = useState("")
  const mismatch = confirmation.length > 0 && password !== confirmation
  const valid = password.length >= 8 && password === confirmation

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const request = { newPassword: password }
    setPassword("")
    setConfirmation("")
    onSubmit(request)
  }

  return (
    <div className="rounded-xl border border-sky-500/20 bg-sky-500/10 p-4">
      <div className="mb-4 flex items-start gap-3">
        <LockKeyhole className="mt-0.5 h-5 w-5 text-sky-700 dark:text-sky-300" />
        <div>
          <div className="font-medium">{t("stacks.users.passwordResetTitle")}</div>
          <p className="text-sm text-muted-foreground">
            {t("stacks.users.passwordResetDescription", {
              matrixUserId: user.matrixUserId ?? user.username,
            })}
          </p>
        </div>
      </div>

      <form className="space-y-4" onSubmit={handleSubmit}>
        {error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.users.passwordResetFailedTitle")}</AlertTitle>
            <AlertDescription>
              {getRuntimeStackProblemDetail(error) ?? error.message}
            </AlertDescription>
          </Alert>
        ) : null}

        <div className="grid gap-4 md:grid-cols-2">
          <div className="space-y-2">
            <Label htmlFor="matrix-reset-password">
              {t("stacks.users.passwordResetNewPasswordLabel")}
            </Label>
            <Input
              id="matrix-reset-password"
              type="password"
              minLength={8}
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="matrix-reset-password-confirmation">
              {t("stacks.users.passwordResetConfirmationLabel")}
            </Label>
            <Input
              id="matrix-reset-password-confirmation"
              type="password"
              minLength={8}
              autoComplete="new-password"
              value={confirmation}
              onChange={(event) => setConfirmation(event.target.value)}
              required
            />
            {mismatch ? (
              <p className="text-xs text-red-600 dark:text-red-400">
                {t("stacks.users.passwordResetMismatch")}
              </p>
            ) : null}
          </div>
        </div>

        <p className="text-xs text-muted-foreground">
          {t("stacks.users.passwordResetLogoutDevices")}
        </p>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" disabled={pending} onClick={onCancel}>
            {t("stacks.users.cancelAction")}
          </Button>
          <Button type="submit" disabled={pending || !valid}>
            {pending ? t("stacks.users.passwordResetting") : t("stacks.users.passwordResetSubmit")}
          </Button>
        </div>
      </form>
    </div>
  )
}

function FirstAdminForm({ slugOrId }: { slugOrId: string }) {
  const { t } = useI18n()
  const createFirstAdmin = useCreateRuntimeStackFirstAdmin(slugOrId)

  return (
    <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 p-4">
      <div className="mb-4 flex items-start gap-3">
        <ShieldCheck className="mt-0.5 h-5 w-5 text-amber-700 dark:text-amber-300" />
        <div>
          <div className="font-medium">{t("stacks.users.firstAdminTitle")}</div>
          <p className="text-sm text-muted-foreground">{t("stacks.users.firstAdminDescription")}</p>
        </div>
      </div>
      <UserForm
        submitLabelKey="stacks.users.firstAdminSubmit"
        defaultIsAdmin={true}
        forceAdmin={true}
        pending={createFirstAdmin.isPending}
        error={createFirstAdmin.error}
        onSubmit={(request) => createFirstAdmin.mutate(request)}
      />
    </div>
  )
}

function CreateUserForm({ slugOrId }: { slugOrId: string }) {
  const { t } = useI18n()
  const createUser = useCreateRuntimeStackUser(slugOrId)
  const [formGeneration, setFormGeneration] = useState(0)

  return (
    <div className="rounded-xl border border-border bg-background/40 p-4">
      <div className="mb-4 flex items-start gap-3">
        <UserPlus className="mt-0.5 h-5 w-5 text-muted-foreground" />
        <div>
          <div className="font-medium">{t("stacks.users.createUserTitle")}</div>
          <p className="text-sm text-muted-foreground">{t("stacks.users.createUserDescription")}</p>
        </div>
      </div>
      <UserForm
        key={formGeneration}
        submitLabelKey="stacks.users.createUserSubmit"
        defaultIsAdmin={false}
        pending={createUser.isPending}
        error={createUser.error}
        onSubmit={(request) => createUser.mutate(request, {
          onSuccess: () => setFormGeneration((current) => current + 1),
        })}
      />
    </div>
  )
}

function UserForm({
  submitLabelKey,
  defaultIsAdmin,
  forceAdmin,
  pending,
  error,
  onSubmit,
}: {
  submitLabelKey: TranslationKey
  defaultIsAdmin: boolean
  forceAdmin?: boolean
  pending: boolean
  error?: Error | null
  onSubmit: (request: CreateRuntimeStackUserRequest) => void
}) {
  const { t } = useI18n()
  const [username, setUsername] = useState("")
  const [password, setPassword] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [email, setEmail] = useState("")
  const [isAdmin, setIsAdmin] = useState(defaultIsAdmin)

  const normalizedUsername = useMemo(
    () => username.trim().toLowerCase().replace(/[^a-z0-9._=-]+/g, ""),
    [username],
  )

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    onSubmit({
      username: normalizedUsername,
      password,
      isAdmin: forceAdmin ? true : isAdmin,
      displayName: displayName.trim() || null,
      email: email.trim() || null,
    })
    setPassword("")
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      {error && (
        <Alert variant="destructive">
          <AlertTitle>{t("stacks.users.creationFailedTitle")}</AlertTitle>
          <AlertDescription>
            {getRuntimeStackProblemDetail(error) ?? t("stacks.users.creationFailedDescription")}
          </AlertDescription>
        </Alert>
      )}
      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="matrix-username">{t("stacks.users.usernameLabel")}</Label>
          <Input id="matrix-username" value={username} onChange={(event) => setUsername(event.target.value)} placeholder="admin" required />
          {normalizedUsername && normalizedUsername !== username && (
            <p className="text-xs text-muted-foreground">
              {t("stacks.users.normalizedUsername", { username: normalizedUsername })}
            </p>
          )}
        </div>
        <div className="space-y-2">
          <Label htmlFor="matrix-password">{t("stacks.users.passwordLabel")}</Label>
          <Input id="matrix-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} minLength={8} required />
        </div>
        <div className="space-y-2">
          <Label htmlFor="matrix-display-name">{t("stacks.users.displayNameLabel")}</Label>
          <Input id="matrix-display-name" value={displayName} onChange={(event) => setDisplayName(event.target.value)} placeholder={t("stacks.users.displayNamePlaceholder")} />
        </div>
        <div className="space-y-2">
          <Label htmlFor="matrix-email">{t("stacks.users.emailLabel")}</Label>
          <Input id="matrix-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} placeholder={t("stacks.users.optionalPlaceholder")} />
        </div>
      </div>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <label className="flex items-center gap-2 text-sm">
          <Checkbox checked={forceAdmin ? true : isAdmin} disabled={forceAdmin} onCheckedChange={(value) => setIsAdmin(value === true)} />
          {t("stacks.users.adminCheckbox")}
        </label>
        <Button type="submit" disabled={pending || !normalizedUsername || password.length < 8}>
          {pending ? t("stacks.users.creating") : t(submitLabelKey)}
        </Button>
      </div>
    </form>
  )
}

function UsersTable({
  slugOrId,
  users,
  loading,
  synchronized,
  activeAdminCount,
  onResetPassword,
}: {
  slugOrId: string
  users: RuntimeStackUserResponse[]
  loading: boolean
  synchronized: boolean
  activeAdminCount: number
  onResetPassword: (user: RuntimeStackUserResponse) => void
}) {
  const { t } = useI18n()

  if (loading) {
    return <div className="text-sm text-muted-foreground">{t("stacks.users.loading")}</div>
  }

  if (users.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
        {synchronized ? t("stacks.users.emptySynchronized") : t("stacks.users.emptyUnknown")}
      </div>
    )
  }

  return (
    <div className="overflow-hidden rounded-xl border border-border">
      <Table className="min-w-[1120px] table-fixed">
        <TableHeader>
          <TableRow className="hover:bg-transparent">
            <TableHead className="w-[14%]">{t("stacks.users.table.username")}</TableHead>
            <TableHead className="w-[25%]">{t("stacks.users.table.matrixUserId")}</TableHead>
            <TableHead className="w-[8%]">{t("stacks.users.table.role")}</TableHead>
            <TableHead className="w-[10%]">{t("stacks.users.table.status")}</TableHead>
            <TableHead className="w-[16%]">{t("stacks.users.table.origin")}</TableHead>
            <TableHead className="sticky right-0 z-30 w-[27%] min-w-[290px] border-l border-border bg-card text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]">
              {t("stacks.users.table.actions")}
            </TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          {users.map((user) => (
            <TableRow
              key={user.id}
              data-testid={`matrix-user-row-${user.id}`}
              data-matrix-user-id={user.matrixUserId ?? undefined}
            >
              <TableCell className="w-[14%] align-top">
                <div className="font-medium">{user.username}</div>
                {user.displayName && (
                  <div className="mt-1 text-xs text-muted-foreground">{user.displayName}</div>
                )}
              </TableCell>
              <TableCell className="w-[25%] break-all align-top text-muted-foreground">
                {user.matrixUserId ?? t("stacks.users.pendingMatrixUserId")}
              </TableCell>
              <TableCell className="w-[8%] align-top">
                {user.isAdmin ? t("stacks.users.role.admin") : t("stacks.users.role.user")}
              </TableCell>
              <TableCell className="w-[10%] align-top">
                <Badge variant="outline">{formatUserStatus(user.status, t)}</Badge>
                {user.lastError && (
                  <div className="mt-1 text-xs text-red-500">{user.lastError}</div>
                )}
              </TableCell>
              <TableCell className="w-[16%] align-top">
                <Badge variant="outline">{formatUserOrigin(user.origin, t)}</Badge>
              </TableCell>
              <TableCell
                data-testid={`matrix-user-actions-${user.id}`}
                className="sticky right-0 z-20 w-[27%] min-w-[290px] border-l border-border bg-card align-top text-right shadow-[-8px_0_12px_-10px_rgba(0,0,0,0.9)]"
              >
                <StackUserLifecycleActions
                  slugOrId={slugOrId}
                  user={user}
                  activeAdminCount={activeAdminCount}
                  onResetPassword={() => onResetPassword(user)}
                />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  )
}

function formatUserStatus(status: string, t: (key: TranslationKey) => string): string {
  const translationKey = userStatusTranslationKeys[status.trim().toLowerCase()]
  return translationKey ? t(translationKey) : status
}

function formatUserOrigin(origin: string, t: (key: TranslationKey) => string): string {
  const translationKey = userOriginTranslationKeys[origin.trim().toLowerCase()]
  return translationKey ? t(translationKey) : t("stacks.users.origin.unknown")
}
