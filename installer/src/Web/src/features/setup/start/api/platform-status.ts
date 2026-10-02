import type { ComponentType } from "react"
import {
  Database,
  Layers3,
  Route,
  RadioTower,
  Wrench,
} from "lucide-react"

export type InstallerServiceKey =
  | "postgres"
  | "npm"
  | "coturn"
  | "seq"
  | "pgadmin"
  | "portainer"

export type PlatformInstallationState =
  | "not-installed"
  | "partially-installed"
  | "installed"
  | "repair-required"
  | "unknown"

export type SetupStartMode =
  | "fresh-install"
  | "migration-required"
  | "upgrade"
  | "already-installed"
  | "repair"
  | "unknown"

export type StartupTarget = "dashboard" | "setup-start" | "resume-installation"

export type RecommendedInstallerAction =
  | "run-preflight"
  | "use-mem-migrate"
  | "run-upgrade-check"
  | "run-repair-check"
  | "continue-setup"
  | "resume-install"
  | "review-verification"
  | "complete-handoff"
  | "open-dashboard"
  | "review-diagnostics"

export type InstallerWarning = {
  code: string
  title: string
  message: string
  blocking: boolean
}

export type ServiceUrl = {
  label: string
  href: string
}

export type InstallerServiceSummary = {
  key: InstallerServiceKey
  displayName: string
  description: string
  required: boolean
  installed: boolean
  running: boolean
  healthy: boolean | null
  state: string
  containerName: string | null
  image: string | null
  urls: ServiceUrl[]
  warnings: InstallerWarning[]
}

export type DetectedMemInstallation = {
  detected: boolean
  productName: string | null
  version: string | null
  apiReachable: boolean
  webReachable?: boolean
  upgradeAvailable: boolean
  targetVersion: string | null
}

export type PlatformStatusResponse = {
  installationState: PlatformInstallationState
  recommendedAction: RecommendedInstallerAction
  startupTarget: StartupTarget
  setupMode?: SetupStartMode
  detectedInstallation?: DetectedMemInstallation | null
  docker: {
    reachable: boolean
    message: string | null
  }
  requiredServices: InstallerServiceSummary[]
  supportToolsServices: InstallerServiceSummary[]
  warnings: InstallerWarning[]
  activeInstallationId?: string | null
  activeInstallationStage?: "activity" | "failure-review" | "verification" | "handoff" | null
}

export type InstallerServiceVisual = {
  icon: ComponentType<{ className?: string }>
  detailPath?: string
}

export const installerServiceVisuals: Record<InstallerServiceKey, InstallerServiceVisual> = {
  postgres: {
    icon: Database,
  },
  npm: {
    icon: Route,
  },
  coturn: {
    icon: RadioTower,
    detailPath: "/services/coturn",
  },
  seq: {
    icon: Layers3,
  },
  pgadmin: {
    icon: Database,
  },
  portainer: {
    icon: Wrench,
  },
}

export function getSetupStartMode(status: PlatformStatusResponse): SetupStartMode {
  if (status.setupMode) {
    return status.setupMode
  }

  const detected = status.detectedInstallation

  if (status.installationState === "installed") {
    return "already-installed"
  }

  if (detected?.upgradeAvailable) {
    return "migration-required"
  }

  if (status.installationState === "repair-required") {
    return "repair"
  }

  if (status.installationState === "partially-installed") {
    return "repair"
  }

  if (status.installationState === "not-installed") {
    return "fresh-install"
  }

  return "unknown"
}

export function getServiceStatusLabel(service: InstallerServiceSummary) {
  if (service.running) {
    return service.healthy === false ? "Running, unhealthy" : "Running"
  }

  if (service.installed) {
    return "Detected"
  }

  return "Not installed"
}

export function getServiceStatusClassName(service: InstallerServiceSummary) {
  if (service.running && service.healthy !== false) {
    return "border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
  }

  if (service.running && service.healthy === false) {
    return "border-red-500/30 bg-red-500/10 text-red-300"
  }

  if (service.installed) {
    return "border-sky-500/30 bg-sky-500/10 text-sky-300"
  }

  return "border-border bg-muted text-muted-foreground"
}

export function getInstallationStateLabel(state: PlatformInstallationState) {
  switch (state) {
    case "not-installed":
      return "Setup required"
    case "partially-installed":
      return "Existing setup detected"
    case "installed":
      return "Installed"
    case "repair-required":
      return "Repair required"
    case "unknown":
      return "Unknown"
  }
}

export function getRecommendedActionLabel(action: RecommendedInstallerAction) {
  switch (action) {
    case "run-preflight":
      return "Start setup check"
    case "use-mem-migrate":
      return "Use mem-migrate"
    case "run-upgrade-check":
      return "Review migration"
    case "run-repair-check":
      return "Start repair check"
    case "continue-setup":
      return "Continue setup"
    case "resume-install":
      return "Resume installation"
    case "review-verification":
      return "Review verification"
    case "complete-handoff":
      return "Finish setup"
    case "open-dashboard":
      return "Open dashboard"
    case "review-diagnostics":
      return "Review diagnostics"
  }
}

export function getStartupResumePath(status: PlatformStatusResponse) {
  if (status.startupTarget !== "resume-installation") {
    return null
  }

  const installationId = status.activeInstallationId?.trim()
  if (!installationId) {
    return null
  }

  switch (status.activeInstallationStage) {
    case "activity":
    case "failure-review":
      return `/setup/install/${installationId}`
    case "verification":
      return `/setup/verify/${installationId}`
    case "handoff":
      return `/setup/handoff/${installationId}`
    default:
      return null
  }
}
