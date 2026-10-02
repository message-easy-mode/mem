import { formatDateTime } from "@/app/formatters"
import type { TranslationKey, UiLanguage } from "@/app/i18n/messages"

const operationTranslationKeys: Readonly<Record<string, TranslationKey>> = {
  "backup-stack": "stacks.operation.backupStack",
  "create-stack-runtime": "stacks.operation.createStackRuntime",
  "run-doctor": "stacks.operation.runDoctor",
  doctor: "stacks.operation.runDoctor",
}

export function formatOptionalStackDateTime(
  value: string | null,
  language: UiLanguage,
  fallback: string,
): string {
  return value ? formatDateTime(value, language) : fallback
}

export function formatStackOperationName(
  operation: string,
  t: (key: TranslationKey) => string,
): string {
  const translationKey = operationTranslationKeys[operation.trim().toLowerCase()]
  return translationKey ? t(translationKey) : operation
}
