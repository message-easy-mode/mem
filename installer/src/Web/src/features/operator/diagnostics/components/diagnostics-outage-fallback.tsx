import { useState } from "react"
import { Clipboard, ServerCrash, TerminalSquare } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

const dockerCommand = "sudo docker logs --tail 500 mem-control-plane"
const clefPath = "/data/logs/control-plane/mem-control-plane-.clef"

export function DiagnosticsOutageFallback() {
  const { t } = useI18n()
  const [copied, setCopied] = useState(false)

  const copyCommand = async () => {
    try {
      await navigator.clipboard.writeText(dockerCommand)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Card className="border-amber-500/30">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          <ServerCrash className="h-4 w-4 text-amber-500" />
          {t("diagnostics.outage.title")}
        </CardTitle>
        <CardDescription>{t("diagnostics.outage.description")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4 text-sm">
        <Alert>
          <TerminalSquare className="h-4 w-4" />
          <AlertTitle>{t("diagnostics.outage.portainerTitle")}</AlertTitle>
          <AlertDescription>{t("diagnostics.outage.portainerDescription")}</AlertDescription>
        </Alert>

        <div className="rounded-lg border bg-muted/20 p-3">
          <div className="font-medium">{t("diagnostics.outage.commandTitle")}</div>
          <div className="mt-2 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
            <code className="overflow-x-auto rounded bg-background px-2 py-1 font-mono text-xs">
              {dockerCommand}
            </code>
            <Button type="button" variant="outline" size="sm" onClick={() => void copyCommand()}>
              <Clipboard className="mr-2 h-4 w-4" />
              {copied ? t("diagnostics.outage.copied") : t("diagnostics.outage.copyCommand")}
            </Button>
          </div>
        </div>

        <div className="rounded-lg border bg-muted/20 p-3">
          <div className="font-medium">{t("diagnostics.outage.clefTitle")}</div>
          <p className="mt-1 text-muted-foreground">{t("diagnostics.outage.clefDescription")}</p>
          <code className="mt-2 block overflow-x-auto rounded bg-background px-2 py-1 font-mono text-xs">
            {clefPath}
          </code>
        </div>

        <p className="text-xs text-muted-foreground">{t("diagnostics.outage.privacy")}</p>
      </CardContent>
    </Card>
  )
}
