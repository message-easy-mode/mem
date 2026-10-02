import type { ReactNode } from "react"

import {
  Archive,
  ArrowLeft,
  ExternalLink,
  RefreshCw,
  Stethoscope,
} from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"

import { StackStatusPill } from "./stack-status-pill"

type StackWorkspaceHeaderProps = {
  slugOrId: string | undefined
  displayName: string
  status: string | null | undefined
  verificationFreshness?: "current" | "stale" | null
  matrixPublicBaseUrl: string | null | undefined
  elementPublicBaseUrl: string | null | undefined
  onRefresh: () => void
  refreshing: boolean
  onRunDoctor: () => void
  doctorPending: boolean
  onCreateBackup: () => void
  backupPending: boolean
  dangerAction?: ReactNode
}

/**
 * Shared header for every implemented Stack Workspace route.
 *
 * The actions deliberately preserve the existing doctor and backup contracts.
 * Individual workspace areas only decide which existing stack projections to load.
 */
export function StackWorkspaceHeader({
  slugOrId,
  displayName,
  status,
  verificationFreshness,
  matrixPublicBaseUrl,
  elementPublicBaseUrl,
  onRefresh,
  refreshing,
  onRunDoctor,
  doctorPending,
  onCreateBackup,
  backupPending,
  dangerAction,
}: StackWorkspaceHeaderProps) {
  const { t } = useI18n()
  const matrixLaunchUrl = getSafePublicUrl(matrixPublicBaseUrl)
  const elementLaunchUrl = getSafePublicUrl(elementPublicBaseUrl)

  return (
    <div className="grid gap-4 min-[1180px]:grid-cols-[minmax(0,1fr)_auto] min-[1180px]:items-start">
      <div className="min-w-0">
        <Button variant="ghost" size="sm" asChild className="mb-3 -ml-2">
          <Link to="/stacks">
            <ArrowLeft className="mr-2 h-4 w-4" />
            {t("stacks.workspace.back")}
          </Link>
        </Button>

        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">{displayName}</h1>
          <StackStatusPill status={status} />
        </div>

        <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
          {t("stacks.workspace.description")}
        </p>
        {verificationFreshness === "stale" ? (
          <p className="mt-2 max-w-3xl text-sm font-medium text-amber-300">
            {t("stacks.workspace.verificationStale")}
          </p>
        ) : null}
      </div>

      <div
        data-testid="stack-workspace-actions"
        className="flex w-full flex-col gap-2 min-[1180px]:w-auto min-[1180px]:items-end min-[1500px]:flex-row min-[1500px]:items-center"
      >
        <div className="flex flex-wrap items-center gap-2">
          {elementLaunchUrl ? (
            <Button variant="outline" size="sm" asChild>
              <a href={elementLaunchUrl} target="_blank" rel="noopener noreferrer">
                <img
                  src="/brands/element.svg"
                  alt=""
                  aria-hidden="true"
                  className="mr-2 h-4 w-4 shrink-0"
                />
                {t("stacks.workspace.actions.openElement")}
                <ExternalLink className="ml-2 h-3.5 w-3.5" aria-hidden="true" />
              </a>
            </Button>
          ) : null}

          {matrixLaunchUrl ? (
            <Button variant="outline" size="sm" asChild>
              <a href={matrixLaunchUrl} target="_blank" rel="noopener noreferrer">
                <span
                  aria-hidden="true"
                  data-brand-icon="matrix"
                  className="mr-2 h-4 w-9 shrink-0 bg-foreground [mask-image:url('/brands/matrix-logo.svg')] [mask-position:center] [mask-repeat:no-repeat] [mask-size:contain]"
                />
                {t("stacks.workspace.actions.openMatrixApi")}
                <ExternalLink className="ml-2 h-3.5 w-3.5" aria-hidden="true" />
              </a>
            </Button>
          ) : null}

          <Button variant="outline" size="sm" onClick={onRefresh} disabled={refreshing}>
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("stacks.workspace.actions.refresh")}
          </Button>
        </div>

        <span
          aria-hidden="true"
          className="hidden h-6 w-px bg-border min-[1500px]:block"
        />

        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" onClick={onRunDoctor} disabled={!slugOrId || doctorPending}>
            <Stethoscope className="mr-2 h-4 w-4" />
            {doctorPending
              ? t("stacks.workspace.actions.runningDoctor")
              : t("stacks.workspace.actions.runDoctor")}
          </Button>

          <Button
            variant="outline"
            size="sm"
            onClick={onCreateBackup}
            disabled={!slugOrId || backupPending}
          >
            <Archive className="mr-2 h-4 w-4" />
            {backupPending
              ? t("stacks.workspace.actions.creatingBackup")
              : t("stacks.workspace.actions.createBackup")}
          </Button>

          {dangerAction}
        </div>
      </div>
    </div>
  )
}


function getSafePublicUrl(value: string | null | undefined) {
  const candidate = value?.trim()
  if (!candidate) return null

  try {
    const parsed = new URL(candidate)
    return parsed.protocol === "https:" || parsed.protocol === "http:" ? candidate : null
  } catch {
    return null
  }
}
