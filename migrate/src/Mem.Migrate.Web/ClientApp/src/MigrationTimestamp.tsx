import { formatMigrationDateTime, migrationUtcIso } from './migration-time'

/** Only use for source API fields explicitly designated as UTC. */
export function MigrationTimestamp({ value }: { value: string | null | undefined }) {
  const utc = migrationUtcIso(value)
  if (!utc) return <span>{value ? 'Time unavailable' : 'Not recorded'}</span>
  return (
    <time dateTime={utc} title={`Recorded UTC: ${utc}`}>
      {formatMigrationDateTime(value, navigator.language || 'en', 'Time unavailable')}
    </time>
  )
}
