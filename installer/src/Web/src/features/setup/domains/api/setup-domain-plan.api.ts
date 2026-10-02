import { getJson, postJson } from "@/lib/api"

import type {
  SetupDomainPlanRequest,
  SetupDomainPlanResponse,
} from "./setup-domain-plan.types"

const basePath = "/api/setup/domains/plan"

export function getSetupDomainPlan() {
  return getJson<SetupDomainPlanResponse>(basePath)
}

export function validateSetupDomainPlan(request: SetupDomainPlanRequest) {
  return postJson<SetupDomainPlanRequest, SetupDomainPlanResponse>(
    `${basePath}/validate`,
    request,
  )
}
