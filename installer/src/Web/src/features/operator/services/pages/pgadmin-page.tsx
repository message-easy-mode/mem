import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"

export function PgAdminPage() {
  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">PgAdmin</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Optional managed admin service for PostgreSQL.
        </p>
      </div>

      <Alert>
        <AlertTitle>Coming soon</AlertTitle>
        <AlertDescription>
          PgAdmin is planned as an optional managed service, but its runtime integration
          is not yet wired into MEM
        </AlertDescription>
      </Alert>
    </div>
  )
}