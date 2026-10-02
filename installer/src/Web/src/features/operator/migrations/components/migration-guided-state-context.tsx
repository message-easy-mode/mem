import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from "react"
import { useQuery, useQueryClient } from "@tanstack/react-query"
import { AlertTriangle, Loader2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import type { MigrationWorkspaceResponse } from "../api/migration-workspace"
import {
  hasGuidedOperationObservation, isDefinitiveGuidedRejection, isGuidedActionAllowed, isGuidedCancellationAllowed,
  MigrationOutcomeUncertain, readPendingGuidedOperation, writePendingGuidedOperation,
  type GuidedOperation, type PendingGuidedOperation,
} from "../api/migration-guided-state"
import { migrationWorkspaceKeys } from "../hooks/use-migration-workspace"

export type MigrationGuidedContextValue = {
  workspace: MigrationWorkspaceResponse
  blocked: boolean
  pendingOperation: GuidedOperation | null
  allows: (...codes: string[]) => boolean
  run: (operation: GuidedOperation, command: () => Promise<unknown>) => Promise<void>
  refresh: () => Promise<unknown>
}

const MigrationGuidedContext = createContext<MigrationGuidedContextValue | null>(null)

export function useMigrationGuidedState(): MigrationGuidedContextValue {
  const value = useContext(MigrationGuidedContext)
  if (!value) throw new Error("Guided Migration requires a server-authored workspace snapshot.")
  return value
}

export function MigrationGuidedStateProvider({ workspace, refresh, children, observationAvailable = true }: {
  workspace: MigrationWorkspaceResponse
  refresh: () => Promise<unknown>
  children: ReactNode
  observationAvailable?: boolean
}) {
  const { t } = useI18n()
  const queryClient = useQueryClient()
  const migrationId = workspace.migration.migrationId
  const [pending, setPending] = useState(() => readPendingGuidedOperation(migrationId))
  const pendingRef = useRef(pending)
  const sending = useRef(false)
  const [checking, setChecking] = useState(Boolean(pending))
  const snapshot = useRef(workspace)
  snapshot.current = workspace
  const refreshRef = useRef(refresh)
  refreshRef.current = refresh

  function remember(value: PendingGuidedOperation | null) {
    pendingRef.current = value
    writePendingGuidedOperation(migrationId, value)
    setPending(value)
  }

  useEffect(() => {
    if (pending && hasGuidedOperationObservation(pending, workspace)) remember(null)
    // An unrelated revision or an unchanged operation token cannot settle a
    // new command. Keep the receipt until the relevant durable state changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pending, workspace])

  useEffect(() => {
    if (!pending) { setChecking(false); return }
    let cancelled = false
    let timer: ReturnType<typeof setTimeout> | undefined
    let reads = 0
    setChecking(true)
    async function check() {
      try { await refreshRef.current() } catch { /* A GET failure does not settle a POST. */ }
      if (cancelled) return
      reads += 1
      if (reads < 6) timer = setTimeout(() => void check(), 1500)
      else setChecking(false)
    }
    timer = setTimeout(() => void check(), 1500)
    return () => { cancelled = true; if (timer) clearTimeout(timer) }
  }, [pending])

  async function run(operation: GuidedOperation, command: () => Promise<unknown>) {
    if (!observationAvailable || pendingRef.current || sending.current) throw new MigrationOutcomeUncertain()
    if (operation === "cancel-migration" && !isGuidedCancellationAllowed(snapshot.current)) throw new MigrationOutcomeUncertain()
    const before = snapshot.current.guided.operationRevisions[operation]
    if (!before || snapshot.current.guided.uncertaintyState !== "known") throw new MigrationOutcomeUncertain()
    sending.current = true
    let dispatched = false
    // Discard a GET begun before this submission. No mutation response is used
    // to seed several caches; the next workspace GET is the presentation truth.
    try {
      await queryClient.cancelQueries({ queryKey: migrationWorkspaceKeys.detail(migrationId), exact: true })
      remember({ migrationId, operation, before })
      dispatched = true
      await command()
    } catch (error) {
      if (!dispatched || isDefinitiveGuidedRejection(error, operation)) {
        remember(null)
        throw error // Preserve normal blocker/step-up handling and exact confirmation bodies.
      }
      throw new MigrationOutcomeUncertain()
    } finally {
      sending.current = false
      void refreshRef.current().catch(() => undefined)
    }
    // An idempotent preview may legitimately return the identical durable
    // preview. A received successful response settles that specific command.
    if (operation === "review-go-live") remember(null)
  }

  const blocked = !observationAvailable || Boolean(pending) || workspace.guided.uncertaintyState !== "known"
  return (
    <MigrationGuidedContext.Provider value={{
      workspace, blocked, pendingOperation: pending?.operation ?? null,
      allows: (...codes) => !blocked && isGuidedActionAllowed(workspace, ...codes), run, refresh,
    }}>
      {!observationAvailable ? (
        <Alert role="status">
          <AlertTriangle className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.guided.staleTitle")}</AlertTitle>
          <AlertDescription>{t("migrationWorkspace.guided.staleDescription")}</AlertDescription>
        </Alert>
      ) : null}
      {pending ? (
        <Alert role="status" aria-live="polite" aria-busy={checking}>
          {checking ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <AlertTriangle className="h-4 w-4" />}
          <AlertTitle>{t(checking ? "migrationWorkspace.guided.checkingTitle" : "migrationWorkspace.guided.uncertainTitle")}</AlertTitle>
          <AlertDescription className="space-y-3">
            <p>{t("migrationWorkspace.guided.uncertainDescription")}</p>
            <Button variant="outline" onClick={() => void refresh().catch(() => undefined)}>
              {t("migrationWorkspace.guided.checkAgain")}
            </Button>
          </AlertDescription>
        </Alert>
      ) : null}
      {children}
    </MigrationGuidedContext.Provider>
  )
}

/** Guided commands use one read-only reconciliation path, never specialist cache inference. */
export function useGuidedMigrationCommand<T>(operation: GuidedOperation, command: (input: T) => Promise<unknown>) {
  const guide = useMigrationGuidedState()
  return {
    isPending: guide.pendingOperation === operation,
    mutateAsync: (input: T) => guide.run(operation, () => command(input)),
  }
}

/** Evidence is keyed to a server-authored operation revision, never a readiness input. */
export function useGuidedMigrationEvidence<T>(
  operation: GuidedOperation, category: string, read: () => Promise<T>, enabled = true,
) {
  const { workspace } = useMigrationGuidedState()
  return useQuery({
    queryKey: ["migration-guided-evidence", workspace.migration.migrationId, category,
      workspace.guided.operationRevisions[operation]],
    queryFn: read,
    enabled,
    retry: 0,
    staleTime: 5_000,
  })
}
