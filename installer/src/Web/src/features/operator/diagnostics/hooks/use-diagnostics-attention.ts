import { useEffect, useState } from "react"
import { useQuery } from "@tanstack/react-query"

import { getDiagnosticsAttention } from "@/features/operator/diagnostics/api/diagnostics.api"

const POLL_INTERVAL_MS = 60_000

export const diagnosticsAttentionQueryKey = ["diagnostics", "attention"] as const

export function diagnosticsAttentionPollingInterval(visible: boolean) {
  return visible ? POLL_INTERVAL_MS : false
}

function useDocumentVisibility() {
  const [visible, setVisible] = useState(
    typeof document === "undefined" || document.visibilityState !== "hidden",
  )

  useEffect(() => {
    function updateVisibility() {
      setVisible(document.visibilityState !== "hidden")
    }

    document.addEventListener("visibilitychange", updateVisibility)
    return () => document.removeEventListener("visibilitychange", updateVisibility)
  }, [])

  return visible
}

export function useDiagnosticsAttention() {
  const visible = useDocumentVisibility()

  return useQuery({
    queryKey: diagnosticsAttentionQueryKey,
    queryFn: getDiagnosticsAttention,
    retry: false,
    staleTime: 15_000,
    refetchInterval: diagnosticsAttentionPollingInterval(visible),
    refetchIntervalInBackground: false,
  })
}
