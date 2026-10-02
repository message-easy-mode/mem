import type { DashboardOverviewResponse } from "./api/dashboard.types"

export const dashboardOverviewFixture: DashboardOverviewResponse = {
  source: "control-plane",
  generatedAtUtc: "2026-07-06T03:45:00Z",
  suggestedRefreshSeconds: 30,
  capabilities: {
    canOperate: true,
    canManagePlatform: true,
  },
  hero: {
    state: "ready",
    headlineCode: "create_first_chat_server",
    action: {
      code: "create_chat_server",
      stackSlug: null,
      restoreSessionId: null,
    },
  },
  onboarding: {
    state: "available",
    completedStepCodes: [],
    nextSteps: [
      {
        code: "create_chat_server",
        state: "available",
        action: {
          code: "create_chat_server",
          stackSlug: null,
          restoreSessionId: null,
        },
      },
    ],
  },
  platform: {
    state: "ready",
    services: [
      {
        key: "postgres",
        requirement: "required",
        state: "running",
        observedAtUtc: "2026-07-06T03:44:30Z",
      },
      {
        key: "npm_ingress",
        requirement: "required",
        state: "running",
        observedAtUtc: "2026-07-06T03:44:30Z",
      },
      {
        key: "coturn",
        requirement: "required",
        state: "running",
        observedAtUtc: "2026-07-06T03:44:30Z",
      },
    ],
    requiredServiceCount: 3,
    runningRequiredServiceCount: 3,
    docker: {
      state: "responsive",
      observedAtUtc: "2026-07-06T03:44:30Z",
    },
  },
  publicAccess: {
    state: "ready",
    mainDomain: "deltabox.dev",
    certificate: {
      state: "valid",
      commonName: "*.deltabox.dev",
      expiresAtUtc: "2026-08-22T19:49:00Z",
    },
    ingress: {
      state: "ready",
      observedAtUtc: "2026-07-06T03:44:30Z",
    },
    lastPlatformRouteVerificationAtUtc: "2026-07-06T03:40:00Z",
  },
  stacks: {
    total: 0,
    healthyLastVerifiedCount: 0,
    attentionCount: 0,
    items: [],
    truncated: false,
  },
  recovery: {
    state: "not_applicable",
    managedStackCount: 0,
    stacksWithValidRecoveryPointCount: 0,
    catalogEntryCount: 0,
    availableCatalogEntryCount: 0,
    validCatalogEntryCount: 0,
    warningCatalogEntryCount: 0,
    invalidCatalogEntryCount: 0,
    latestCapturedAtUtc: null,
    totalPayloadBytes: null,
    activeRestoreCount: 0,
    attentionRestoreCount: 0,
    priorityRestoreSessionId: null,
  },
  host: {
    state: "unavailable",
    observedAtUtc: null,
    unavailableReasonCode: "host_observation_failed",
    operatingSystem: null,
    architecture: null,
    cpuCount: null,
    memoryTotalBytes: null,
    dockerServerVersion: null,
    containerCount: null,
    imageCount: null,
    disk: null,
    storageUnavailableReasonCode: "host_filesystem_unavailable",
  },
  activity: {
    state: "empty",
    items: [],
  },
  notices: [],
}
