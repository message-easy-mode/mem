import { useQuery } from "@tanstack/react-query"
import { getVerificationReport } from "@/features/setup/verification/api/verification.api"

export function useVerificationReport(installationId: string | undefined) {
  return useQuery({
    queryKey: ["setup", "verification", installationId],
    queryFn: () => getVerificationReport(installationId!),
    enabled: Boolean(installationId),
    refetchInterval: 10000,
  })
}