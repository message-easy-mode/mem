import { ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

export function IngressTlsIntroCard() {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border bg-muted">
            <ShieldCheck className="h-5 w-5" />
          </div>

          <div>
            <CardTitle>{t("operatorDomains.certificates.advanced.pathTitle")}</CardTitle>
            <CardDescription>
              {t("operatorDomains.certificates.advanced.pathDescription")}
            </CardDescription>
          </div>
        </div>
      </CardHeader>
    </Card>
  )
}
