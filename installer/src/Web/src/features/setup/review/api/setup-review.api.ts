import { getJson, postJson } from "@/lib/api"
import type { SetupReviewResponse } from "./setup-review.types"

const basePath = "/api/setup/review"

export function getSetupReview() {
  return getJson<SetupReviewResponse>(`${basePath}/`)
}

export function acceptSetupReview() {
  return postJson<void, SetupReviewResponse>(`${basePath}/accept`)
}
