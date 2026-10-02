import { ExternalLink } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"

type Props = {
  supported?: boolean
  openUiHref?: string | null
}

export function ServiceActions({
  supported = true,
  openUiHref = null,
}: Props) {
  const { t } = useI18n()

  if (!supported || !openUiHref) {
    return null
  }

  return (
    <Button asChild size="sm">
      <a href={openUiHref} target="_blank" rel="noreferrer">
        {t("services.action.open")}
        <ExternalLink className="ml-2 h-3.5 w-3.5" />
      </a>
    </Button>
  )
}
