import { useEffect } from "react"
import { useQuery } from "@tanstack/react-query"
import { Navigate, Outlet, useLocation, useSearchParams } from "react-router-dom"
import { AlertTriangle, Loader2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { SetupPreviewBanner } from "@/features/setup/components/setup-preview-banner"
import { usePlatformStatus } from "@/features/setup/start/hooks/use-platform-status"
import { getJson } from "@/lib/api"

const FIRST_TIME_SETUP_LOCKED_STATE_KEY = "firstTimeSetupLocked"
const SETUP_PREVIEW_QUERY_PARAM = "setupPreview"
const SETUP_PREVIEW_SESSION_KEY = "mem.setupPreview"
const SETUP_PREVIEW_ALLOWED_RUNTIME_MODES = new Set([
  "local-development",
  "containerized-development",
  "automated-test",
])

const setupPreviewRuntimeQueryKey = ["setup-preview", "runtime-preflight"] as const

type SetupPreviewRuntimePreflight = {
  runtimeMode: string
}

export function FirstTimeSetupRouteGuard() {
  const { t } = useI18n()
  const platformStatus = usePlatformStatus()
  const location = useLocation()
  const [searchParams] = useSearchParams()

  const previewQueryValue = searchParams.get(SETUP_PREVIEW_QUERY_PARAM)
  const previewRequested = previewQueryValue === "1" || hasSetupPreviewSession()
  const target = platformStatus.data?.startupTarget

  const runtimePreflight = useQuery<SetupPreviewRuntimePreflight>({
    queryKey: setupPreviewRuntimeQueryKey,
    queryFn: () => getJson<SetupPreviewRuntimePreflight>("/health/runtime"),
    enabled: previewRequested && target === "dashboard",
    staleTime: 30_000,
    retry: 0,
  })

  const previewAllowed =
    previewRequested &&
    SETUP_PREVIEW_ALLOWED_RUNTIME_MODES.has(runtimePreflight.data?.runtimeMode ?? "")

  useEffect(() => {
    if (!previewRequested || !runtimePreflight.isFetched) {
      return
    }

    if (previewAllowed) {
      setSetupPreviewSession()
      return
    }

    clearSetupPreviewSession()
  }, [previewAllowed, previewRequested, runtimePreflight.isFetched])

  if (platformStatus.isLoading || !platformStatus.isFetchedAfterMount) {
    return <GuardLoadingFrame />
  }

  if (target === "dashboard") {
    if (!previewRequested) {
      return <LockedSetupRedirect />
    }

    if (runtimePreflight.isLoading || !runtimePreflight.isFetched) {
      return <GuardLoadingFrame />
    }

    if (!previewAllowed) {
      return <LockedSetupRedirect />
    }

    if (previewQueryValue !== "1") {
      return (
        <Navigate
          to={withSetupPreview(location.pathname, location.search, location.hash)}
          replace
        />
      )
    }

    return (
      <>
        <SetupPreviewBanner runtimeMode={runtimePreflight.data?.runtimeMode ?? "unknown"} />
        <Outlet />
      </>
    )
  }

  if (target === "setup-start" || target === "resume-installation") {
    return <Outlet />
  }

  return (
    <GuardFrame>
      <Alert variant="destructive">
        <AlertTriangle className="h-4 w-4" />
        <AlertTitle>{t("startup.unavailableTitle")}</AlertTitle>
        <AlertDescription className="space-y-4">
          <p>{t("startup.unavailableDescription")}</p>
          <Button
            type="button"
            variant="outline"
            onClick={() => void platformStatus.refetch()}
            disabled={platformStatus.isFetching}
          >
            {platformStatus.isFetching ? t("startup.retrying") : t("startup.retry")}
          </Button>
        </AlertDescription>
      </Alert>
    </GuardFrame>
  )
}

function LockedSetupRedirect() {
  return (
    <Navigate
      to="/dashboard"
      replace
      state={{ [FIRST_TIME_SETUP_LOCKED_STATE_KEY]: true }}
    />
  )
}

function GuardLoadingFrame() {
  const { t } = useI18n()

  return (
    <GuardFrame>
      <Card>
        <CardContent className="flex items-center gap-3 p-6 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" />
          {t("startup.checking")}
        </CardContent>
      </Card>
    </GuardFrame>
  )
}

function hasSetupPreviewSession() {
  try {
    return window.sessionStorage.getItem(SETUP_PREVIEW_SESSION_KEY) === "1"
  } catch {
    return false
  }
}

function setSetupPreviewSession() {
  try {
    window.sessionStorage.setItem(SETUP_PREVIEW_SESSION_KEY, "1")
  } catch {
    // Browser storage is an optional convenience for preserving developer preview
    // across setup navigation. The explicit query parameter remains authoritative.
  }
}

function clearSetupPreviewSession() {
  try {
    window.sessionStorage.removeItem(SETUP_PREVIEW_SESSION_KEY)
  } catch {
    // Ignore unavailable browser storage and fail closed through the route guard.
  }
}

function withSetupPreview(pathname: string, search: string, hash: string) {
  const params = new URLSearchParams(search)
  params.set(SETUP_PREVIEW_QUERY_PARAM, "1")
  const nextSearch = params.toString()

  return `${pathname}${nextSearch ? `?${nextSearch}` : ""}${hash}`
}

function GuardFrame({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-[calc(100vh-12rem)] items-center justify-center">
      <div className="w-full max-w-2xl">{children}</div>
    </div>
  )
}
