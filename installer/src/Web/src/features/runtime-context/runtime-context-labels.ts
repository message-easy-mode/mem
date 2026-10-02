import type { TranslationKey } from "@/app/i18n/messages"

export function runtimeModeKey(mode: string): TranslationKey {
  switch (mode) {
    case "local-development":
      return "runtime.mode.localDevelopment"
    case "containerized-development":
      return "runtime.mode.containerizedDevelopment"
    case "containerized-production":
      return "runtime.mode.containerizedProduction"
    case "automated-test":
      return "runtime.mode.automatedTest"
    default:
      return "runtime.value.unknown"
  }
}

export function runtimeIndicatorLabelKey(mode: string): TranslationKey {
  switch (mode) {
    case "local-development":
      return "runtime.indicator.localDevelopment"
    case "containerized-development":
      return "runtime.indicator.containerizedDevelopment"
    case "automated-test":
      return "runtime.indicator.automatedTest"
    default:
      return "runtime.value.unknown"
  }
}

export function runtimeIndicatorShortLabelKey(mode: string): TranslationKey {
  return mode === "automated-test"
    ? "runtime.indicator.shortTest"
    : "runtime.indicator.shortDevelopment"
}

export function uiDeliveryKey(mode: string): TranslationKey {
  switch (mode) {
    case "vite":
      return "runtime.ui.vite"
    case "embedded-spa":
      return "runtime.ui.embeddedSpa"
    case "api-only":
      return "runtime.ui.apiOnly"
    case "test-host":
      return "runtime.ui.testHost"
    default:
      return "runtime.value.unknown"
  }
}

export function stateRootLabelKey(kind: string, profile: string): TranslationKey {
  const custom = profile === "custom"
  switch (kind) {
    case "repository-local":
      return custom
        ? "runtime.state.repositoryCustom"
        : "runtime.state.repositoryDefault"
    case "development-volume":
      return custom
        ? "runtime.state.developmentVolumeCustom"
        : "runtime.state.developmentVolumeDefault"
    case "persistent-volume":
      return custom
        ? "runtime.state.persistentVolumeCustom"
        : "runtime.state.persistentVolumeDefault"
    case "disposable-test":
      return "runtime.state.disposableTest"
    default:
      return "runtime.value.unknown"
  }
}

export function dockerEndpointKindKey(kind: string): TranslationKey {
  switch (kind) {
    case "local-unix-socket":
      return "runtime.docker.localUnixSocket"
    case "windows-named-pipe":
      return "runtime.docker.windowsNamedPipe"
    case "remote-http":
      return "runtime.docker.remoteHttp"
    case "remote-https":
      return "runtime.docker.remoteHttps"
    default:
      return "runtime.docker.custom"
  }
}

export function controlPlaneAccessModeKey(mode: string): TranslationKey {
  switch (mode) {
    case "ssh-tunnel":
      return "runtime.exposure.mode.sshTunnel"
    case "trusted-lan":
      return "runtime.exposure.mode.trustedLan"
    case "unsupported":
      return "runtime.exposure.mode.unsupported"
    case "local-development":
      return "runtime.exposure.mode.localDevelopment"
    default:
      return "runtime.exposure.mode.unknown"
  }
}

export function controlPlaneExposureStateKey(state: string): TranslationKey {
  switch (state) {
    case "private":
      return "runtime.exposure.state.private"
    case "needs-attention":
      return "runtime.exposure.state.needsAttention"
    case "unavailable":
      return "runtime.exposure.state.unavailable"
    case "not-applicable":
      return "runtime.exposure.state.notApplicable"
    default:
      return "runtime.exposure.state.unknown"
  }
}
