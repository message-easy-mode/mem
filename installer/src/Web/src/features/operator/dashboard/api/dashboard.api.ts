import { getJson } from "@/lib/api"
import type { DashboardOverviewResponse } from "./dashboard.types"

export const dashboardOverviewEndpoint = "/api/operator/dashboard/overview"

export function getDashboardOverview(): Promise<DashboardOverviewResponse> {
  return getJson<DashboardOverviewResponse>(dashboardOverviewEndpoint)
}
