import { FileKey2, KeyRound, LoaderCircle, Network } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import type { StoredCertificateMetadata } from "@/features/shared/domains/api/domains.types"

export function CertificateActionsCard({
  selectedCertificate,
  busy,
  onValidate,
  onProbeNpm,
  onImportToNpm,
  isImporting,
}: {
  selectedCertificate: StoredCertificateMetadata | null
  busy: boolean
  onValidate: () => void
  onProbeNpm: () => void
  onImportToNpm: () => void
  isImporting: boolean
}) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("operatorDomains.certificates.actions.title")}</CardTitle>
        <CardDescription>
          {t("operatorDomains.certificates.actions.description")}
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        {!selectedCertificate ? (
          <div className="rounded-lg border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("operatorDomains.certificates.actions.selectFirst")}
          </div>
        ) : null}

        {selectedCertificate ? (
          <div className="rounded-lg border border-border bg-background/40 p-3 text-sm">
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {t("operatorDomains.certificates.actions.selectedCertificate")}
            </div>
            <div className="mt-1 break-all">
              {selectedCertificate.certificateId}
            </div>
          </div>
        ) : null}

        <div className="flex flex-wrap gap-3">
          <Button
            variant="outline"
            onClick={onValidate}
            disabled={!selectedCertificate || busy}
          >
            <FileKey2 className="mr-2 h-4 w-4" />
            {t("operatorDomains.certificates.actions.validate")}
          </Button>

          <Button
            variant="outline"
            onClick={onProbeNpm}
            disabled={!selectedCertificate || busy}
          >
            <Network className="mr-2 h-4 w-4" />
            {t("operatorDomains.certificates.actions.probeNpm")}
          </Button>

          <Button onClick={onImportToNpm} disabled={!selectedCertificate || busy}>
            {isImporting ? (
              <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <KeyRound className="mr-2 h-4 w-4" />
            )}
            {isImporting
              ? t("operatorDomains.certificates.actions.importing")
              : t("operatorDomains.certificates.actions.importNpm")}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
