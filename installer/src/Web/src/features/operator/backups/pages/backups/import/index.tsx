import { Link } from "react-router-dom"
import {
  FolderArchive,
  ShieldAlert,
  UploadCloud,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { BackupUploadValidationCard } from "@/features/operator/backups/pages/backups/import/components/backup-upload-validation-card"

export function BackupRestoreImportPage() {
  const { t } = useI18n()

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">{t("backupImport.title")}</h1>
          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("backupImport.description")}
          </p>
        </div>

        <Button variant="outline" size="sm" asChild>
          <Link to="/backups">
            <FolderArchive className="mr-2 h-4 w-4" />
            {t("backupImport.backToCatalog")}
          </Link>
        </Button>
      </div>

      <Alert>
        <ShieldAlert className="h-4 w-4" />
        <AlertTitle>{t("backupImport.sources.title")}</AlertTitle>
        <AlertDescription>{t("backupImport.sources.externalDescription")}</AlertDescription>
      </Alert>

      <BackupUploadValidationCard />

      <div className="rounded-xl border border-border bg-card/50 p-4 text-sm text-muted-foreground">
        <div className="flex items-center gap-2 font-medium text-foreground">
          <UploadCloud className="h-4 w-4" />
          {t("backupImport.afterValidation.title")}
        </div>
        <p className="mt-1">{t("backupImport.afterValidation.description")}</p>
      </div>
    </div>
  )
}
