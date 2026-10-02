import type { PropsWithChildren } from "react"

import {
  operatorSessionContext,
  type OperatorSessionContextValue,
} from "@/features/auth/operator-session-context"

type OperatorSessionProviderProps = PropsWithChildren<OperatorSessionContextValue>

export function OperatorSessionProvider({
  session,
  signOut,
  children,
}: OperatorSessionProviderProps) {
  return (
    <operatorSessionContext.Provider value={{ session, signOut }}>
      {children}
    </operatorSessionContext.Provider>
  )
}
