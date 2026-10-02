import {
  Database,
  Gauge,
  Globe,
  LayoutDashboard,
  Route,
  ScrollText,
  Server,
  Wrench,
} from "lucide-react"

import type { ManagedServiceName } from "../api/runtime-ui.types"

type Props = {
  serviceName: ManagedServiceName
}

export function ServiceIcon({ serviceName }: Props) {
  const Icon = getIcon(serviceName)
  const tone = getTone(serviceName)

  return (
    <div
      className={[
        "inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border",
        tone.wrapper,
      ].join(" ")}
    >
      <Icon className={["h-5 w-5", tone.icon].join(" ")} />
    </div>
  )
}

function getIcon(serviceName: ManagedServiceName) {
  switch (serviceName) {
    case "postgres":
      return Database
    case "npm":
      return Route
    case "coturn":
      return Route
    case "mem-api":
      return Gauge
    case "mem-web":
      return Globe
    case "seq":
      return ScrollText
    case "pgadmin":
      return LayoutDashboard
    case "portainer":
      return Wrench
    default:
      if (serviceName.includes("element")) return Globe
      if (serviceName.includes("synapse") || serviceName.includes("matrix")) return Server
      return Server
  }
}

function getTone(serviceName: ManagedServiceName) {
  switch (serviceName) {
    case "postgres":
      return {
        wrapper: "border-sky-500/20 bg-sky-500/10",
        icon: "text-sky-300",
      }
    case "npm":
      return {
        wrapper: "border-emerald-500/20 bg-emerald-500/10",
        icon: "text-emerald-300",
      }
    case "coturn":
      return {
        wrapper: "border-fuchsia-500/20 bg-fuchsia-500/10",
        icon: "text-fuchsia-300",
      }
    case "mem-api":
      return {
        wrapper: "border-violet-500/20 bg-violet-500/10",
        icon: "text-violet-300",
      }
    case "mem-web":
      return {
        wrapper: "border-indigo-500/20 bg-indigo-500/10",
        icon: "text-indigo-300",
      }
    case "seq":
      return {
        wrapper: "border-amber-500/20 bg-amber-500/10",
        icon: "text-amber-300",
      }
    case "pgadmin":
      return {
        wrapper: "border-cyan-500/20 bg-cyan-500/10",
        icon: "text-cyan-300",
      }
    case "portainer":
      return {
        wrapper: "border-orange-500/20 bg-orange-500/10",
        icon: "text-orange-300",
      }
    default:
      if (serviceName.startsWith("stack-")) {
        return {
          wrapper:
            "border-sky-500/30 bg-gradient-to-br from-sky-500/15 to-emerald-500/15",
          icon: "text-emerald-600 dark:text-emerald-300",
        }
      }

      if (serviceName.startsWith("restore-staging-")) {
        return {
          wrapper: "border-border bg-background/60",
          icon: "text-foreground",
        }
      }

      if (serviceName.includes("element")) {
        return {
          wrapper: "border-teal-500/20 bg-teal-500/10",
          icon: "text-teal-300",
        }
      }

      if (serviceName.includes("synapse") || serviceName.includes("matrix")) {
        return {
          wrapper: "border-lime-500/20 bg-lime-500/10",
          icon: "text-lime-300",
        }
      }

      return {
        wrapper: "border-border bg-background/60",
        icon: "text-foreground",
      }
  }
}