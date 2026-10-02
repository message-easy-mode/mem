export type ControlPlaneRuntimeContext = Readonly<{
  schemaVersion: number
  productDisplayName: string
  applicationName: string
  runtimeMode: string
  controlPlaneInstanceId: string
  apiProcessInstanceId: string
  environmentName: string
  runningInContainer: boolean
  contentRootKind: string
  stateRootKind: string
  stateRootProfile: string
  uiDeliveryMode: string
  dockerEndpointKind: string
  configuredContainerName: string | null
  version: string
  commit: string | null
  validationState: string
  mutationsAllowed: boolean
  showDevelopmentBanner: boolean
  restart: Readonly<{
    kind: string
    guidanceCode: string
    command: string | null
    commandAvailable: boolean
  }>
  dockerOwnership: Readonly<{
    state: string
    mutationsAllowed: boolean
    developmentOverrideActive: boolean
    competingContainers: readonly string[]
    warningCode: string | null
  }>
  controlPlaneExposure: Readonly<{
    state: string
    accessMode: string
    hostAddress: string | null
    hostPort: number | null
    bindingCount: number
    warningCode: string | null
    isPrivate: boolean
  }>
  warnings: readonly string[]
}>
