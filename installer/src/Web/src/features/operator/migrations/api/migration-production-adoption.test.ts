import { describe, expect, it } from "vitest"

import {
  normalizeMigrationProductionAdoptionState,
  normalizeMigrationUtcTimestamp,
  type MigrationProductionAdoptionState,
} from "./migration-production-adoption"

describe("migration production adoption UTC timestamps", () => {
  it("marks offset-less UTC values explicitly as UTC", () => {
    expect(normalizeMigrationUtcTimestamp("2026-07-22T02:34:25")).toBe(
      "2026-07-22T02:34:25Z",
    )
  })

  it("preserves timestamps that already contain an offset", () => {
    expect(normalizeMigrationUtcTimestamp("2026-07-22T02:34:25Z")).toBe(
      "2026-07-22T02:34:25Z",
    )
    expect(normalizeMigrationUtcTimestamp("2026-07-22T14:34:25+12:00")).toBe(
      "2026-07-22T14:34:25+12:00",
    )
    expect(normalizeMigrationUtcTimestamp(null)).toBeNull()
  })

  it("normalizes a reconstructed cutover preview before the UI evaluates expiry", () => {
    const state = {
      source: "control-plane",
      status: "cutover-preview-ready",
      migrationId: "mig_utc_preview",
      planPrepared: true,
      detail: "Ready",
      plan: {
        cutover: {
          preview: {
            createdAtUtc: "2026-07-22T02:19:25",
            expiresAtUtc: "2026-07-22T02:34:25",
          },
        },
      },
    } as unknown as MigrationProductionAdoptionState

    const normalized = normalizeMigrationProductionAdoptionState(state)

    expect(normalized.plan?.cutover.preview.createdAtUtc).toBe(
      "2026-07-22T02:19:25Z",
    )
    expect(normalized.plan?.cutover.preview.expiresAtUtc).toBe(
      "2026-07-22T02:34:25Z",
    )
  })
})
