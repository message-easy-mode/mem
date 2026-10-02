import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query"
import {
  backupRuntimeStack,
  createRuntimeStack,
  createRuntimeStackFirstAdmin,
  createRuntimeStackUser,
  connectRuntimeStackTurn,
  disconnectRuntimeStackTurn,
  deactivateRuntimeStackUser,
  destroyRuntimeStack,
  doctorRuntimeStack,
  getLatestRuntimeStackDoctor,
  getRuntimeStackDoctorReport,
  getRuntimeStackImagePolicy,
  listRuntimeStackDoctorHistory,
  getCreateRuntimeStackOperation,
  getDestroyRuntimeStackOperation,
  inspectRuntimeStack,
  inspectRuntimeStackStorage,
  inspectRuntimeStackTurn,
  reviewRuntimeStackTurnConnect,
  reviewRuntimeStackTurnDisconnect,
  listRuntimeStackOperations,
  listRuntimeStacks,
  listRuntimeStackUsers,
  reactivateRuntimeStackUser,
  resetRuntimeStackUserPassword,
  setRuntimeStackMatrixAdminAuthority,
  synchronizeRuntimeStackUsers,
  updateRuntimeStackIdentity,
  uploadRuntimeStackLogo,
  removeRuntimeStackLogo,
} from "../api/stacks.api"
import type {
  CreateRuntimeStackRequest,
  CreateRuntimeStackUserRequest,
  DeactivateRuntimeStackUserRequest,
  ImportMatrixAdminAuthorityRequest,
  ReactivateRuntimeStackUserRequest,
  ResetRuntimeStackUserPasswordRequest,
  RuntimeStackDestroyRequest,
  RuntimeStackIdentityUpdateRequest,
  RuntimeStackListRequest,
  RuntimeStackTurnConnectRequest,
  RuntimeStackTurnDisconnectRequest,
  RuntimeStackUsersResponse,
} from "../api/stacks.types"

export const runtimeStackKeys = {
  all: ["runtime-stacks"] as const,
  listRoot: ["runtime-stacks", "list"] as const,
  list: (request?: RuntimeStackListRequest) =>
    [...runtimeStackKeys.listRoot, request ?? "all"] as const,
  detail: (slugOrId: string) => [...runtimeStackKeys.all, "detail", slugOrId] as const,
  operations: (slugOrId: string) =>
    [...runtimeStackKeys.all, "operations", slugOrId] as const,
  users: (slugOrId: string) => [...runtimeStackKeys.all, "users", slugOrId] as const,
  storage: (slugOrId: string) => [...runtimeStackKeys.all, "storage", slugOrId] as const,
  turn: (slugOrId: string) => [...runtimeStackKeys.all, "turn", slugOrId] as const,
  doctor: (slugOrId: string) => [...runtimeStackKeys.all, "doctor", slugOrId] as const,
  doctorHistoryRoot: (slugOrId: string) =>
    [...runtimeStackKeys.all, "doctor-history", slugOrId] as const,
  doctorHistory: (slugOrId: string, page: number, pageSize: number) =>
    [...runtimeStackKeys.doctorHistoryRoot(slugOrId), page, pageSize] as const,
  doctorReport: (slugOrId: string, reportId: string) =>
    [...runtimeStackKeys.all, "doctor-report", slugOrId, reportId] as const,
  createOperation: (operationId: string) =>
    [...runtimeStackKeys.all, "create-operation", operationId] as const,
  destroyOperation: (operationId: string) =>
    [...runtimeStackKeys.all, "destroy-operation", operationId] as const,
  imagePolicy: ["runtime-stacks", "image-policy"] as const,
}

export function useRuntimeStackImagePolicy() {
  return useQuery({
    queryKey: runtimeStackKeys.imagePolicy,
    queryFn: getRuntimeStackImagePolicy,
    staleTime: Number.POSITIVE_INFINITY,
  })
}

export function useRuntimeStacks(request?: RuntimeStackListRequest) {
  return useQuery({
    queryKey: runtimeStackKeys.list(request),
    queryFn: () => listRuntimeStacks(request),
    refetchInterval: 15000,
    placeholderData: (previousData) => previousData,
  })
}

export function useRuntimeStackStorage(slugOrId: string | undefined) {
  return useQuery({
    queryKey: runtimeStackKeys.storage(slugOrId ?? ""),
    queryFn: () => inspectRuntimeStackStorage(slugOrId!),
    enabled: Boolean(slugOrId),
    refetchInterval: 15000,
  })
}

export function useRuntimeStack(slugOrId: string | undefined) {
  return useQuery({
    queryKey: runtimeStackKeys.detail(slugOrId ?? ""),
    queryFn: () => inspectRuntimeStack(slugOrId!),
    enabled: Boolean(slugOrId),
    refetchInterval: 15000,
  })
}

export function useUpdateRuntimeStackIdentity(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: RuntimeStackIdentityUpdateRequest) =>
      updateRuntimeStackIdentity(slugOrId, request),
    onSuccess: (result) => {
      queryClient.setQueryData(runtimeStackKeys.detail(slugOrId), result)
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.listRoot })
    },
  })
}

export function useUploadRuntimeStackLogo(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (file: File) => uploadRuntimeStackLogo(slugOrId, file),
    onSuccess: (result) => {
      queryClient.setQueryData(runtimeStackKeys.detail(slugOrId), result)
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.listRoot })
    },
  })
}

export function useRemoveRuntimeStackLogo(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => removeRuntimeStackLogo(slugOrId),
    onSuccess: (result) => {
      queryClient.setQueryData(runtimeStackKeys.detail(slugOrId), result)
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.listRoot })
    },
  })
}

export function useRuntimeStackTurn(slugOrId: string | undefined) {
  return useQuery({
    queryKey: runtimeStackKeys.turn(slugOrId ?? ""),
    queryFn: () => inspectRuntimeStackTurn(slugOrId!),
    enabled: Boolean(slugOrId),
    refetchInterval: 15000,
  })
}

export function useReviewRuntimeStackTurnConnect(slugOrId: string) {
  return useMutation({
    mutationFn: () => reviewRuntimeStackTurnConnect(slugOrId),
  })
}

export function useConnectRuntimeStackTurn(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (
      request: Omit<RuntimeStackTurnConnectRequest, "idempotencyKey"> & {
        idempotencyKey?: string
      },
    ) => connectRuntimeStackTurn(slugOrId, request),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.turn(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.detail(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useReviewRuntimeStackTurnDisconnect(slugOrId: string) {
  return useMutation({
    mutationFn: () => reviewRuntimeStackTurnDisconnect(slugOrId),
  })
}

export function useDisconnectRuntimeStackTurn(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (
      request: Omit<RuntimeStackTurnDisconnectRequest, "idempotencyKey"> & {
        idempotencyKey?: string
      },
    ) => disconnectRuntimeStackTurn(slugOrId, request),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.turn(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.detail(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useRuntimeStackOperations(slugOrId: string | undefined) {
  return useQuery({
    queryKey: runtimeStackKeys.operations(slugOrId ?? ""),
    queryFn: () => listRuntimeStackOperations(slugOrId!),
    enabled: Boolean(slugOrId),
    refetchInterval: 15000,
  })
}

export function useRuntimeStackUsers(slugOrId: string | undefined) {
  return useQuery({
    queryKey: runtimeStackKeys.users(slugOrId ?? ""),
    queryFn: () => listRuntimeStackUsers(slugOrId!),
    enabled: Boolean(slugOrId),
    refetchInterval: 15000,
  })
}

export function useSynchronizeRuntimeStackUsers(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => synchronizeRuntimeStackUsers(slugOrId),
    onSuccess: (result) => {
      queryClient.setQueryData(runtimeStackKeys.users(slugOrId), result)
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
    },
  })
}

/**
 * Loads the latest persisted Doctor report from the Control Plane. A successful
 * POST Doctor replaces this query cache immediately, while a browser refresh or
 * runtime transition reconstructs the same latest report from durable evidence.
 */
export function useCurrentRuntimeStackDoctor(slugOrId: string | undefined) {
  return useQuery({
    queryKey: runtimeStackKeys.doctor(slugOrId ?? ""),
    queryFn: async () => {
      const response = await getLatestRuntimeStackDoctor(slugOrId!)
      return response.report
    },
    enabled: Boolean(slugOrId),
    staleTime: 15_000,
  })
}

export function useRuntimeStackDoctorHistory(
  slugOrId: string | undefined,
  page: number,
  pageSize = 10,
) {
  return useQuery({
    queryKey: runtimeStackKeys.doctorHistory(slugOrId ?? "", page, pageSize),
    queryFn: () => listRuntimeStackDoctorHistory(slugOrId!, page, pageSize),
    enabled: Boolean(slugOrId),
    placeholderData: (previousData) => previousData,
  })
}

export function useRuntimeStackDoctorReport(
  slugOrId: string | undefined,
  reportId: string | null,
) {
  return useQuery({
    queryKey: runtimeStackKeys.doctorReport(slugOrId ?? "", reportId ?? ""),
    queryFn: () => getRuntimeStackDoctorReport(slugOrId!, reportId!),
    enabled: Boolean(slugOrId && reportId),
  })
}

export function useCreateRuntimeStackOperation(operationId: string | null) {
  return useQuery({
    queryKey: runtimeStackKeys.createOperation(operationId ?? ""),
    queryFn: () => getCreateRuntimeStackOperation(operationId!),
    enabled: Boolean(operationId),
    refetchInterval: (query) =>
      query.state.data?.terminal ? false : 1000,
    retry: 3,
  })
}


export function useDestroyRuntimeStackOperation(operationId: string | null) {
  return useQuery({
    queryKey: runtimeStackKeys.destroyOperation(operationId ?? ""),
    queryFn: () => getDestroyRuntimeStackOperation(operationId!),
    enabled: Boolean(operationId),
    refetchInterval: (query) =>
      query.state.data?.terminal ? false : 1000,
    retry: 3,
  })
}

export function useCreateRuntimeStack() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateRuntimeStackRequest) => createRuntimeStack(request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.all })
    },
  })
}

export function useDoctorRuntimeStack(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => doctorRuntimeStack(slugOrId),
    onSuccess: (result) => {
      queryClient.setQueryData(runtimeStackKeys.doctor(slugOrId), result)
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.doctorHistoryRoot(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.listRoot })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.detail(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useBackupRuntimeStack(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => backupRuntimeStack(slugOrId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.detail(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.storage(slugOrId) })
    },
  })
}

export function useDestroyRuntimeStack() {
  return useMutation({
    mutationFn: ({
      slugOrId,
      request,
    }: {
      slugOrId: string
      request: RuntimeStackDestroyRequest
    }) => destroyRuntimeStack(slugOrId, request),
  })
}

export function useCreateRuntimeStackFirstAdmin(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateRuntimeStackUserRequest) =>
      createRuntimeStackFirstAdmin(slugOrId, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.detail(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useCreateRuntimeStackUser(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: CreateRuntimeStackUserRequest) =>
      createRuntimeStackUser(slugOrId, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useSetRuntimeStackMatrixAdminAuthority(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (request: ImportMatrixAdminAuthorityRequest) =>
      setRuntimeStackMatrixAdminAuthority(slugOrId, request),
    onSuccess: (adminAuthority) => {
      queryClient.setQueryData<RuntimeStackUsersResponse>(
        runtimeStackKeys.users(slugOrId),
        (current) => current ? { ...current, adminAuthority } : current,
      )
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useResetRuntimeStackUserPassword(slugOrId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      userId,
      request,
    }: {
      userId: string
      request: ResetRuntimeStackUserPasswordRequest
    }) => resetRuntimeStackUserPassword(slugOrId, userId, request),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useDeactivateRuntimeStackUser(slugOrId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ userId, request }: { userId: string; request: DeactivateRuntimeStackUserRequest }) =>
      deactivateRuntimeStackUser(slugOrId, userId, request),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}

export function useReactivateRuntimeStackUser(slugOrId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ userId, request }: { userId: string; request: ReactivateRuntimeStackUserRequest }) =>
      reactivateRuntimeStackUser(slugOrId, userId, request),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.users(slugOrId) })
      void queryClient.invalidateQueries({ queryKey: runtimeStackKeys.operations(slugOrId) })
    },
  })
}
