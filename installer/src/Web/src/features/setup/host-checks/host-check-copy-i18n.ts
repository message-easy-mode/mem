import type { TranslationKey } from "@/app/i18n/messages"

const GROUP_COPY: Record<string, { title: TranslationKey; description: TranslationKey }> = {
  host: {
    title: "setup.checks.groupTitle.host",
    description: "setup.checks.groupDescription.host",
  },
  docker: {
    title: "setup.checks.groupTitle.docker",
    description: "setup.checks.groupDescription.docker",
  },
  "existing-docker-resources": {
    title: "setup.checks.groupTitle.existing",
    description: "setup.checks.groupDescription.existing",
  },
  "existing-installation": {
    title: "setup.checks.groupTitle.existing",
    description: "setup.checks.groupDescription.existing",
  },
  storage: {
    title: "setup.checks.groupTitle.storage",
    description: "setup.checks.groupDescription.storage",
  },
  ports: {
    title: "setup.checks.groupTitle.ports",
    description: "setup.checks.groupDescription.ports",
  },
}

const CHECK_TITLES: Record<string, TranslationKey> = {
  "ubuntu-version": "setup.checks.checkTitle.ubuntu-version",
  architecture: "setup.checks.checkTitle.architecture",
  cpu: "setup.checks.checkTitle.cpu",
  memory: "setup.checks.checkTitle.memory",
  "docker-reachable": "setup.checks.checkTitle.docker-reachable",
  "docker-compose-plugin": "setup.checks.checkTitle.docker-compose-plugin",
  "docker-data-root": "setup.checks.checkTitle.docker-data-root",
  "docker-disk-usage": "setup.checks.checkTitle.docker-disk-usage",
  "existing-docker-containers": "setup.checks.checkTitle.existing-docker-containers",
  "existing-docker-networks": "setup.checks.checkTitle.existing-docker-networks",
  "existing-docker-volumes": "setup.checks.checkTitle.existing-docker-volumes",
  "disk-space": "setup.checks.checkTitle.disk-space",
  ports: "setup.checks.checkTitle.ports",
}

export function localizedHostCheckGroupCopy(
  key: string,
  t: (key: TranslationKey) => string,
  fallbackTitle: string,
  fallbackDescription: string,
) {
  const copy = GROUP_COPY[key]
  return copy
    ? { title: t(copy.title), description: t(copy.description) }
    : { title: fallbackTitle, description: fallbackDescription }
}

export function localizedHostCheckTitle(
  key: string,
  t: (key: TranslationKey) => string,
  fallback: string,
) {
  const translationKey = CHECK_TITLES[key]
  return translationKey ? t(translationKey) : fallback
}
