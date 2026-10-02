import type { MigrationConversionOptions } from "@/features/operator/migrations/api/migration-conversions"
import { MigrationBoundSourceStack } from "@/features/operator/migrations/components/migration-bound-source-stack"

/**
 * Retained as a source-compatible wrapper for older component imports.
 * Current schema-v2 packages contain one source-selected stack and never render a target chooser.
 */
export function MigrationSourceStackSelection({
  options,
}: {
  options: MigrationConversionOptions
  selectedSourceStackId: string | null
  disabled?: boolean
  onSelect: (sourceStackId: string) => void
}) {
  return <MigrationBoundSourceStack stack={options.boundSourceStack} />
}
