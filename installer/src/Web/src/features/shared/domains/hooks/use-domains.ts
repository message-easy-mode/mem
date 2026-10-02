// src/features/shared/domains/hooks/use-domains.ts

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"

import {
  createOperatorDomain,
  deleteOperatorDomain,
  deleteOperatorDomainCertificate,
  getLatestOperatorDomainCertificateIssuance,
  getNpmIngressStatus,
  getOperatorDomain,
  getOperatorDomainCertificate,
  getOperatorDomainRenewalHistory,
  getOperatorDomainRenewalStatus,
  importCertificateToNpm,
  listActiveOperatorDomainCertificateIssuances,
  listCertificates,
  listOperatorDomainCertificates,
  listOperatorDomainRenewalStatuses,
  listOperatorDomains,
  probeNpmCertificate,
  removeOperatorDomainRenewalCredential,
  runOperatorDomainRenewal,
  setActiveOperatorDomainCertificate,
  setMainOperatorDomain,
  startOperatorDomainCertificateIssuance,
  testNpmProxyHost,
  updateOperatorDomain,
  updateOperatorDomainRenewalCredential,
  updateOperatorDomainRenewalPolicy,
  validateCertificate,
} from "../api/domains.api"

import type {
  DomainCertificateIssueStartRequest,
  CreateOperatorDomainRequest,
  NpmCertificateTestProxyHostRequest,
  UpdateOperatorDomainRenewalCredentialRequest,
  UpdateOperatorDomainRenewalPolicyRequest,
  UpdateOperatorDomainRequest,
} from "../api/domains.types"

const dashboardOverviewQueryKey = ["dashboard", "overview"] as const

export const domainQueryKeys = {
  npmStatus: ["domains", "npm-status"] as const,
  domains: ["operator-domains"] as const,
  domain: (domainId: string | undefined) => ["operator-domains", domainId] as const,
  certificates: ["domains", "certificates"] as const,
  domainCertificates: (domainId: string | undefined) =>
    ["domains", domainId, "certificates"] as const,
  domainCertificate: (domainId: string | undefined, certificateId: string | undefined) =>
    ["domains", domainId, "certificates", certificateId] as const,
  domainCertificateIssuanceLatest: (domainId: string | undefined) =>
    ["domains", domainId, "certificates", "issuance", "latest"] as const,
  activeCertificateIssuances: ["domains", "certificates", "issuance", "active"] as const,
  renewal: ["domains", "renewal"] as const,
  renewalDomain: (domainId: string | undefined) => ["domains", "renewal", domainId] as const,
  renewalHistory: (domainId: string | undefined) =>
    ["domains", "renewal", domainId, "history"] as const,
}

export function useOperatorDomains() {
  return useQuery({
    queryKey: domainQueryKeys.domains,
    queryFn: listOperatorDomains,
  })
}

export function useOperatorDomain(domainId: string | undefined) {
  return useQuery({
    queryKey: domainQueryKeys.domain(domainId),
    queryFn: () => getOperatorDomain(domainId!),
    enabled: !!domainId,
  })
}

export function useCreateOperatorDomain() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateOperatorDomainRequest) => createOperatorDomain(request),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
    },
  })
}

export function useUpdateOperatorDomain(domainId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: UpdateOperatorDomainRequest) => {
      if (!domainId) {
        throw new Error("No domain id is available.")
      }

      return updateOperatorDomain(domainId, request)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domain(domainId) })
    },
  })
}

export function useDeleteOperatorDomain() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ domainId, force }: { domainId: string; force?: boolean }) =>
      deleteOperatorDomain(domainId, { force }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.certificates })
    },
  })
}

export function useSetMainOperatorDomain() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (domainId: string) => setMainOperatorDomain(domainId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.certificates })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}


export function useOperatorDomainRenewalStatuses(enabled = true) {
  return useQuery({
    queryKey: domainQueryKeys.renewal,
    queryFn: listOperatorDomainRenewalStatuses,
    enabled,
    refetchInterval: 5000,
  })
}

export function useOperatorDomainRenewalStatus(domainId: string | undefined) {
  return useQuery({
    queryKey: domainQueryKeys.renewalDomain(domainId),
    queryFn: () => getOperatorDomainRenewalStatus(domainId!),
    enabled: !!domainId,
    refetchInterval: 5000,
  })
}

export function useOperatorDomainRenewalHistory(
  domainId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: domainQueryKeys.renewalHistory(domainId),
    queryFn: () => getOperatorDomainRenewalHistory(domainId!),
    enabled: !!domainId && enabled,
    refetchInterval: 5000,
  })
}

export function useRunOperatorDomainRenewal() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (domainId: string) => runOperatorDomainRenewal(domainId),
    onSuccess: async (_, domainId) => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.renewal })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalDomain(domainId),
      })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalHistory(domainId),
      })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.certificates })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}

export function useUpdateOperatorDomainRenewalPolicy() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      domainId,
      request,
    }: {
      domainId: string
      request: UpdateOperatorDomainRenewalPolicyRequest
    }) => updateOperatorDomainRenewalPolicy(domainId, request),
    onSuccess: async (_, variables) => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.renewal })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalDomain(variables.domainId),
      })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalHistory(variables.domainId),
      })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}

export function useUpdateOperatorDomainRenewalCredential() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      domainId,
      request,
    }: {
      domainId: string
      request: UpdateOperatorDomainRenewalCredentialRequest
    }) => updateOperatorDomainRenewalCredential(domainId, request),
    onSuccess: async (_, variables) => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.renewal })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalDomain(variables.domainId),
      })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalHistory(variables.domainId),
      })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}

export function useRemoveOperatorDomainRenewalCredential() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (domainId: string) => removeOperatorDomainRenewalCredential(domainId),
    onSuccess: async (_, domainId) => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.renewal })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalDomain(domainId),
      })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalHistory(domainId),
      })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}

export function useNpmIngressStatus() {
  return useQuery({
    queryKey: domainQueryKeys.npmStatus,
    queryFn: getNpmIngressStatus,
    refetchInterval: 10000,
  })
}

export function useCertificates() {
  return useQuery({
    queryKey: domainQueryKeys.certificates,
    queryFn: listCertificates,
    refetchInterval: 5_000,
    refetchIntervalInBackground: false,
  })
}

export function useActiveOperatorDomainCertificateIssuances() {
  return useQuery({
    queryKey: domainQueryKeys.activeCertificateIssuances,
    queryFn: listActiveOperatorDomainCertificateIssuances,
    refetchInterval: 3_000,
    refetchIntervalInBackground: false,
  })
}

export function useOperatorDomainCertificates(domainId: string | undefined) {
  return useQuery({
    queryKey: domainQueryKeys.domainCertificates(domainId),
    queryFn: () => listOperatorDomainCertificates(domainId!),
    enabled: !!domainId,
  })
}

export function useOperatorDomainCertificate(
  domainId: string | undefined,
  certificateId: string | undefined,
) {
  return useQuery({
    queryKey: domainQueryKeys.domainCertificate(domainId, certificateId),
    queryFn: () => getOperatorDomainCertificate(domainId!, certificateId!),
    enabled: !!domainId && !!certificateId,
  })
}

export function useDeleteOperatorDomainCertificate(domainId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (certificateId: string) => {
      if (!domainId) {
        throw new Error("No domain id is available.")
      }

      return deleteOperatorDomainCertificate(domainId, certificateId)
    },
    onSuccess: async (_, certificateId) => {
      queryClient.removeQueries({
        queryKey: domainQueryKeys.domainCertificate(domainId, certificateId),
      })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.domainCertificates(domainId),
      })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domain(domainId) })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.certificates })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.renewal })
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.renewalDomain(domainId),
      })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}

export function useLatestOperatorDomainCertificateIssuance(domainId: string | undefined) {
  return useQuery({
    queryKey: domainQueryKeys.domainCertificateIssuanceLatest(domainId),
    queryFn: () => getLatestOperatorDomainCertificateIssuance(domainId!),
    enabled: !!domainId,
    refetchInterval: (query) => {
      const status = query.state.data?.status?.toLowerCase()
      return status === "queued" || status === "running" ? 1_500 : false
    },
  })
}

export function useStartOperatorDomainCertificateIssuance(domainId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: DomainCertificateIssueStartRequest) => {
      if (!domainId) {
        throw new Error("No domain id is available.")
      }

      return startOperatorDomainCertificateIssuance(domainId, request)
    },
    onSuccess: (response) => {
      if (response.operation) {
        queryClient.setQueryData(
          domainQueryKeys.domainCertificateIssuanceLatest(domainId),
          response.operation,
        )
      }
    },
    onSettled: async () => {
      // A POST can succeed server-side even if the browser loses the response. Always
      // rediscover durable state so an ambiguous network failure cannot strand the UI.
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.domainCertificateIssuanceLatest(domainId),
      })
    },
  })
}

export function useSetActiveOperatorDomainCertificate(domainId: string | undefined) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (certificateId: string) => {
      if (!domainId) {
        throw new Error("No domain id is available.")
      }

      return setActiveOperatorDomainCertificate(domainId, certificateId)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({
        queryKey: domainQueryKeys.domainCertificates(domainId),
      })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domain(domainId) })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.certificates })
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.domains })
      await queryClient.invalidateQueries({ queryKey: dashboardOverviewQueryKey })
    },
  })
}

export function useValidateCertificate() {
  return useMutation({
    mutationFn: (certificateId: string) => validateCertificate(certificateId),
  })
}

export function useProbeNpmCertificate() {
  return useMutation({
    mutationFn: (certificateId: string) => probeNpmCertificate(certificateId),
  })
}

export function useImportCertificateToNpm() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (certificateId: string) => importCertificateToNpm(certificateId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: domainQueryKeys.npmStatus })
    },
  })
}

export function useTestNpmProxyHost(certificateId: string | null) {
  return useMutation({
    mutationFn: (request: NpmCertificateTestProxyHostRequest) => {
      if (!certificateId) {
        throw new Error("No certificate selected.")
      }

      return testNpmProxyHost(certificateId, request)
    },
  })
}
