import type { ControlPlaneRuntimeContext } from "./api/runtime-context.types"

export function createRuntimeContext(
  overrides: Partial<ControlPlaneRuntimeContext> = {},
): ControlPlaneRuntimeContext {
  return {
    schemaVersion: 1,
    productDisplayName: "MEM Control Plane",
    applicationName: "mem-control-plane",
    runtimeMode: "containerized-production",
    controlPlaneInstanceId: "11111111-1111-1111-1111-111111111111",
    apiProcessInstanceId: "22222222-2222-2222-2222-222222222222",
    environmentName: "Production",
    runningInContainer: true,
    contentRootKind: "published-container",
    stateRootKind: "persistent-volume",
    stateRootProfile: "default",
    uiDeliveryMode: "embedded-spa",
    dockerEndpointKind: "local-unix-socket",
    configuredContainerName: "mem-control-plane",
    version: "0.2.0-test",
    commit: "abc123",
    validationState: "valid",
    mutationsAllowed: true,
    showDevelopmentBanner: false,
    restart: {
      kind: "container",
      guidanceCode: "restart_control_plane_container",
      command: "sudo docker restart mem-control-plane",
      commandAvailable: true,
    },
    dockerOwnership: {
      state: "exclusive",
      mutationsAllowed: true,
      developmentOverrideActive: false,
      competingContainers: [],
      warningCode: null,
    },
    controlPlaneExposure: {
      state: "private",
      accessMode: "ssh-tunnel",
      hostAddress: "127.0.0.1",
      hostPort: 8443,
      bindingCount: 1,
      warningCode: null,
      isPrivate: true,
    },
    warnings: [],
    ...overrides,
  }
}
