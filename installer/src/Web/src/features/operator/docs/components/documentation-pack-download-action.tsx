import { Download, LoaderCircle } from "lucide-react"
import { useState } from "react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { getDocumentationReleasePack } from "@/features/operator/docs/documentation-release-pack"
import { downloadDocumentationPack } from "@/features/operator/docs/documentation-pack-download"

export function DocumentationPackDownloadAction({ compact = false }: { compact?: boolean }) {
  const { language, t } = useI18n()
  const documentationReleasePack = getDocumentationReleasePack(language)
  const [isPreparing, setIsPreparing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleDownload = async () => {
    setError(null)
    setIsPreparing(true)

    try {
      await downloadDocumentationPack(language)
    } catch {
      setError(t("documentation.downloadPackError"))
    } finally {
      setIsPreparing(false)
    }
  }

  return (
    <div className="flex flex-col items-start gap-2">
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={isPreparing}
        onClick={() => void handleDownload()}
      >
        {isPreparing ? (
          <LoaderCircle className="animate-spin" aria-hidden="true" />
        ) : (
          <Download aria-hidden="true" />
        )}
        {isPreparing ? t("documentation.preparingPack") : t("documentation.downloadPack")}
      </Button>
      {!compact ? (
        <p className="max-w-sm text-xs leading-5 text-muted-foreground">
          {t("documentation.downloadPackDescription", {
            count: documentationReleasePack.downloadFileCount,
          })}
        </p>
      ) : null}
      {error ? <p className="text-xs text-destructive" role="alert">{error}</p> : null}
    </div>
  )
}
