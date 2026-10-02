import { useCallback, useEffect, useState } from "react"
import type { ReactNode } from "react"
import { Navigate, useLocation } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  getControlPlaneSession,
  logoutControlPlane,
  type ControlPlaneSession,
} from "@/features/auth/control-plane-auth"
import type { NamedOperatorSession } from "@/features/auth/operator-session-context"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"

type AuthGuardProps = {
  children: ReactNode
}

type AuthGuardState =
  | { status: "checking" }
  | { status: "unavailable" }
  | { status: "authenticated"; session: NamedOperatorSession }
  | {
      status: "unauthenticated"
      requiresFirstOwnerBootstrap: boolean
    }

function isNamedOperatorSession(session: ControlPlaneSession): session is NamedOperatorSession {
  return session.authenticated && session.authenticationKind === "operator"
}

/**
 * Guards normal control-plane routes.
 *
 * A `mem_` code is a one-time bootstrap credential, not a normal operator
 * login. A transitional installer cookie is also not sufficient to enter the
 * browser application: protected routes require a named Identity operator
 * session. The guard refreshes the session when the browser returns to the
 * foreground, without polling in the background and extending idle sessions.
 */
export function AuthGuard({ children }: AuthGuardProps) {
  const { t } = useI18n()
  const location = useLocation()
  const [state, setState] = useState<AuthGuardState>({ status: "checking" })

  const resolveSession = useCallback(async (): Promise<AuthGuardState> => {
    const session = await getControlPlaneSession()

    if (isNamedOperatorSession(session)) {
      return { status: "authenticated", session }
    }

    return {
      status: "unauthenticated",
      requiresFirstOwnerBootstrap: session.requiresFirstOwnerBootstrap,
    }
  }, [])

  const refreshSession = useCallback(async () => {
    try {
      setState(await resolveSession())
    } catch {
      // Do not discard a working in-memory operator state merely because the
      // control plane was temporarily unreachable. A verified unauthenticated
      // response still redirects immediately.
      setState((current) =>
        current.status === "authenticated"
          ? current
          : { status: "unavailable" },
      )
    }
  }, [resolveSession])

  useEffect(() => {
    let cancelled = false

    async function checkInitialSession() {
      try {
        const nextState = await resolveSession()

        if (!cancelled) {
          setState(nextState)
        }
      } catch {
        if (!cancelled) {
          setState({ status: "unavailable" })
        }
      }
    }

    void checkInitialSession()

    return () => {
      cancelled = true
    }
  }, [resolveSession])

  useEffect(() => {
    if (state.status !== "authenticated") {
      return
    }

    function refreshWhenReturningToForeground() {
      if (document.visibilityState === "visible") {
        void refreshSession()
      }
    }

    window.addEventListener("focus", refreshWhenReturningToForeground)
    document.addEventListener("visibilitychange", refreshWhenReturningToForeground)

    return () => {
      window.removeEventListener("focus", refreshWhenReturningToForeground)
      document.removeEventListener("visibilitychange", refreshWhenReturningToForeground)
    }
  }, [refreshSession, state.status])

  const signOut = useCallback(async () => {
    await logoutControlPlane()
    setState({
      status: "unauthenticated",
      requiresFirstOwnerBootstrap: false,
    })
  }, [])

  if (state.status === "checking") {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background text-foreground">
        <div className="text-sm text-muted-foreground">{t("auth.sessionChecking")}</div>
      </main>
    )
  }

  if (state.status === "unavailable") {
    return (
      <main className="flex min-h-screen items-center justify-center bg-background px-6 text-foreground">
        <div className="max-w-md space-y-4 text-center">
          <div className="text-base font-semibold">{t("auth.sessionUnavailableTitle")}</div>
          <p className="text-sm text-muted-foreground">
            {t("auth.sessionUnavailableDescription")}
          </p>
          <Button type="button" onClick={() => void refreshSession()}>
            {t("auth.sessionRetry")}
          </Button>
        </div>
      </main>
    )
  }

  if (state.status === "unauthenticated") {
    return (
      <Navigate
        to={state.requiresFirstOwnerBootstrap ? "/bootstrap" : "/login"}
        replace
        state={{ from: location.pathname + location.search }}
      />
    )
  }

  return (
    <OperatorSessionProvider session={state.session} signOut={signOut}>
      {children}
    </OperatorSessionProvider>
  )
}
