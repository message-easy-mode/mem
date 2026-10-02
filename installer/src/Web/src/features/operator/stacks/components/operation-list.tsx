import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { RuntimeStackOperationResponse } from "../api/stacks.types"
import { StackStatusPill } from "./stack-status-pill"
import { formatOptionalStackDateTime, formatStackOperationName } from "./stack-workspace-formatting"

type Props = {
  operations: RuntimeStackOperationResponse[]
}

export function OperationList({ operations }: Props) {
  const { language, t } = useI18n()
  const unknown = t("stacks.common.unknown")
  const none = t("stacks.common.none")

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("stacks.operations.title")}</CardTitle>
        <p className="text-sm text-muted-foreground">
          {t("stacks.operations.description")}
        </p>
      </CardHeader>
      <CardContent>
        {operations.length === 0 ? (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("stacks.operations.empty")}
          </div>
        ) : (
          <div className="space-y-3">
            {operations.map((operation) => (
              <div
                key={operation.id}
                className="rounded-xl border border-border bg-background/40 p-4"
              >
                <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                  <div>
                    <div className="font-medium">{formatStackOperationName(operation.operation, t)}</div>
                    <div className="mt-1 text-xs text-muted-foreground">
                      {operation.id}
                    </div>
                  </div>
                  <StackStatusPill status={operation.status} />
                </div>

                <div className="mt-4 grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
                  <Info label={t("stacks.operations.requestedBy")} value={operation.requestedBy ?? unknown} />
                  <Info label={t("stacks.operations.mutationLevel")} value={operation.hostMutationLevel ?? unknown} />
                  <Info label={t("stacks.operations.currentStep")} value={operation.currentStep ?? unknown} />
                  <Info
                    label={t("stacks.operations.requested")}
                    value={formatDateTime(operation.requestedAtUtc, language)}
                  />
                  <Info
                    label={t("stacks.operations.completed")}
                    value={formatOptionalStackDateTime(operation.completedAtUtc, language, unknown)}
                  />
                  <Info label={t("stacks.operations.idempotency")} value={operation.idempotencyKey ?? none} />
                </div>

                {operation.lastError && (
                  <div className="mt-3 rounded-lg border border-red-500/20 bg-red-500/10 p-3 text-sm text-red-200">
                    {operation.lastError}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-foreground">{value}</div>
    </div>
  )
}
