import { useEffect, useMemo } from "react"

import type { InstallationStepExecutionResponse } from "../api/installation-run.types"
import {
  useInstallation,
  useInstallationSteps,
  useRunInstallation,
} from "./use-installation-run"
import type { InstallationStatus, WorkflowStep } from "../api/install.types"


export type InstallationWorkflowStatusDto = {
  installationId: string
  installationStatus: InstallationStatus
  lastError: string | null
  startedAtUtc: string | null
  completedAtUtc: string | null
  steps: WorkflowStep[]
}

export function useInstallWorkflow(installationId: string | undefined) {
  const installationQuery = useInstallation(installationId)
  const installation = installationQuery.data
  const installationStatus = installation
    ? normaliseInstallationStatus(installation.status)
    : undefined
  const shouldPoll =
    installationStatus === "Running" || installationStatus === "WaitingForUser"
  const stepsQuery = useInstallationSteps(installationId, shouldPoll)
  const runMutation = useRunInstallation()
  const refetchInstallation = installationQuery.refetch
  const refetchSteps = stepsQuery.refetch

  const rawSteps = stepsQuery.data ?? []

  const steps = useMemo(() => rawSteps.map(toWorkflowStep), [rawSteps])

  const allStepsSucceeded = steps.length > 0 && steps.every((step) => step.status === "Succeeded")

  const workflow = useMemo<InstallationWorkflowStatusDto | undefined>(() => {
    if (!installation) return undefined

    return {
      installationId: installation.id,
      installationStatus: normaliseInstallationStatus(installation.status),
      lastError: installation.lastError,
      startedAtUtc: installation.startedAtUtc,
      completedAtUtc: installation.completedAtUtc,
      steps,
    }
  }, [installation, steps])

  useEffect(() => {
    if (!installation) return

    const status = normaliseInstallationStatus(installation.status)

    if (status === "Running" && allStepsSucceeded) {
      void refetchInstallation()
    }
  }, [installation, allStepsSucceeded, refetchInstallation])

  useEffect(() => {
    if (!installationId) return

    const synchronizeFromServer = () => {
      void refetchInstallation()
      void refetchSteps()
    }

    const onVisibilityChange = () => {
      if (document.visibilityState === "visible") {
        synchronizeFromServer()
      }
    }

    window.addEventListener("focus", synchronizeFromServer)
    document.addEventListener("visibilitychange", onVisibilityChange)

    return () => {
      window.removeEventListener("focus", synchronizeFromServer)
      document.removeEventListener("visibilitychange", onVisibilityChange)
    }
  }, [installationId, refetchInstallation, refetchSteps])

  async function refresh() {
    await refetchInstallation()
    await refetchSteps()
  }

  function startOrContinue() {
    if (!installationId) return

    runMutation.mutate(installationId, {
      onSuccess: async () => {
        await refresh()
      },
    })
  }

  return {
    workflow,
    steps,
    installation,
    isLoading: installationQuery.isLoading || stepsQuery.isLoading,
    error: installationQuery.error ?? stepsQuery.error,
    isContinuePending: runMutation.isPending,
    lastRunMessage: runMutation.data?.message,
    refresh,
    startOrContinue,
  }
}

export function toWorkflowStep(step: InstallationStepExecutionResponse): WorkflowStep {
  return {
    order: step.sequence,
    name: step.stepName,
    title: step.stepName,
    kind: "server-step",
    notes: null,
    category: "Installation",
    tags: [],
    requiresHumanAction: step.status === "WaitingForUser",
    humanActionPrompt:
      step.humanActionPrompt ??
      (step.status === "WaitingForUser"
        ? step.errorMessage ??
          "Review the current step, complete the required setup action, then continue installation."
        : null),
    isCheckpoint: false,
    status: normaliseStepStatus(step.status),
    message: step.message,
    errorMessage: step.errorMessage,
    attemptCount: step.attemptCount,
    startedAtUtc: step.startedAtUtc,
    completedAtUtc: step.completedAtUtc,
    progress: step.progress ?? null,
  }
}

function normaliseStepStatus(status: string): WorkflowStep["status"] {
  switch (status) {
    case "Pending":
    case "Running":
    case "Succeeded":
    case "WaitingForUser":
    case "Failed":
      return status
    case "Completed":
      return "Succeeded"
    default:
      return "Pending"
  }
}

function normaliseInstallationStatus(status: string): InstallationStatus {
  switch (status) {
    case "Draft":
    case "Ready":
    case "Running":
    case "WaitingForUser":
    case "Succeeded":
    case "Failed":
      return status
    case "Completed":
      return "Succeeded"
    default:
      return "Draft"
  }
}