import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

import type { HostCheckGroup as HostCheckGroupModel } from "../api/host-checks.types"
import { localizedHostCheckGroupCopy } from "../host-check-copy-i18n"
import { HostCheckRow } from "./host-check-row"

export function HostCheckGroup({ group }: { group: HostCheckGroupModel }) {
  const { t } = useI18n()
  const attentionChecks = group.checks.filter((check) => check.status !== "Pass")
  const passedChecks = group.checks.filter((check) => check.status === "Pass")
  const copy = localizedHostCheckGroupCopy(group.key, t, group.title, group.description)

  return (
    <Card>
      <CardHeader className="pb-3">
        <div className="flex flex-col gap-1 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle>{copy.title}</CardTitle>
            <CardDescription>{copy.description}</CardDescription>
          </div>
          <div className="text-xs text-muted-foreground">
            {attentionChecks.length > 0
              ? `${t("setup.checks.group.review", { count: attentionChecks.length })} · `
              : ""}
            {t("setup.checks.group.passed", { count: passedChecks.length })}
          </div>
        </div>
      </CardHeader>

      <CardContent className="space-y-3">
        {attentionChecks.map((check) => (
          <HostCheckRow key={check.key} check={check} />
        ))}

        {passedChecks.length > 0 ? (
          <details className="rounded-lg border border-border bg-muted/20 p-3">
            <summary className="cursor-pointer text-sm font-medium">
              {t("setup.checks.group.passedChecks", { count: passedChecks.length })}
            </summary>
            <div className="mt-3 space-y-3">
              {passedChecks.map((check) => (
                <HostCheckRow key={check.key} check={check} compact />
              ))}
            </div>
          </details>
        ) : null}
      </CardContent>
    </Card>
  )
}
