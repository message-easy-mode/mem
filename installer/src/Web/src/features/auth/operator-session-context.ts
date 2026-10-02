import { createContext, useContext } from "react"

import type { ControlPlaneSession } from "@/features/auth/control-plane-auth"

export type NamedOperatorSession = ControlPlaneSession & {
  authenticated: true
  authenticationKind: "operator"
}

export type OperatorSessionContextValue = {
  session: NamedOperatorSession
  signOut: () => Promise<void>
}

export const operatorSessionContext = createContext<OperatorSessionContextValue | null>(null)

export function useOptionalOperatorSession(): OperatorSessionContextValue | null {
  return useContext(operatorSessionContext)
}

export function useOperatorSession(): OperatorSessionContextValue {
  const session = useOptionalOperatorSession()

  if (!session) {
    throw new Error("useOperatorSession must be used inside OperatorSessionProvider")
  }

  return session
}
