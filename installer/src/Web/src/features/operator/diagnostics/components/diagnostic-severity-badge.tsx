import { Badge } from "@/components/ui/badge"

export function DiagnosticSeverityBadge({ severity }: { severity: string }) {
  const normalized = severity.toLowerCase()
  const variant = normalized === "critical" || normalized === "error"
    ? "destructive"
    : normalized === "warning"
      ? "secondary"
      : "outline"

  return <Badge variant={variant}>{severity}</Badge>
}
