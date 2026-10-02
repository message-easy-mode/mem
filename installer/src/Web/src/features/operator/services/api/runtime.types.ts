export type DockerPortBinding = {
  privatePort: number
  publicPort: number
  type: string
  ip: string
}

export type DockerContainerInspection = {
  id: string
  name: string
  image: string
  state: string
  running: boolean
  ports: DockerPortBinding[]
}

export type DockerContainerSummary = {
  id: string
  name: string
  image: string
  state: string
  status: string
  ports: DockerPortBinding[]
}

export type DockerPingResponse = {
  ok: boolean
}

export type DockerContainersResponse = {
  containers: DockerContainerSummary[]
}

export type RuntimeActionResponse = {
  success: boolean
  message: string
  container: DockerContainerInspection | null
}

export type RuntimePlanResponse = {
  serviceName: string
  containerName: string
  image: string
  containerPort: number
  preferredHostPort: number
  selectedHostPort: number
  isPreferredPortAvailable: boolean
  hostDataPath: string | null
  warnings: string[]
}

export type RuntimeServiceStatusResponse = {
  serviceName: string
  containerName: string
  preferredHostPort: number | null
  selectedHostPort: number | null
  hostDataPath: string | null
  exists: boolean
  running: boolean
  state: string | null
  container: DockerContainerInspection | null
  warnings: string[]
}

export type RuntimeNpmPortPlanResponse = {
  containerPort: number
  preferredHostPort: number
  selectedHostPort: number
  isPreferredPortAvailable: boolean
  protocol: string
  warnings: string[]
}

export type RuntimeNpmPlanResponse = {
  serviceName: string
  containerName: string
  image: string
  ports: RuntimeNpmPortPlanResponse[]
  hostDataPath: string | null
  hostLetsEncryptPath: string | null
  warnings: string[]
}

export type RuntimeNpmStatusResponse = {
  serviceName: string
  containerName: string
  hostDataPath: string | null
  hostLetsEncryptPath: string | null
  httpHostPort: number | null
  adminHostPort: number | null
  httpsHostPort: number | null
  exists: boolean
  running: boolean
  state: string | null
  container: DockerContainerInspection | null
  warnings: string[]
}

export type RuntimeSeqPlanResponse = {
  serviceName: string
  containerName: string
  image: string
  containerPort: number
  preferredHostPort: number
  selectedHostPort: number
  isPreferredPortAvailable: boolean
  hostDataPath: string | null
  warnings: string[]
}

export type RuntimeSeqStatusResponse = {
  serviceName: string
  containerName: string
  hostDataPath: string | null
  uiHostPort: number | null
  exists: boolean
  running: boolean
  state: string | null
  container: DockerContainerInspection | null
  warnings: string[]
}

export type PlanPostgresRequest = {
  preferredHostPort: number
  hostDataPath: string | null
}

export type DeployPostgresRequest = PlanPostgresRequest

export type PlanNpmRequest = {
  preferredHttpPort: number
  preferredAdminPort: number
  preferredHttpsPort: number
  hostDataPath: string | null
  hostLetsEncryptPath: string | null
}

export type DeployNpmRequest = PlanNpmRequest & {
  forcePreferredPorts: boolean
}

export type PlanSeqRequest = {
  preferredHostPort: number
  hostDataPath: string | null
}

export type DeploySeqRequest = PlanSeqRequest & {
  adminPassword: string
  forcePreferredPort: boolean
}