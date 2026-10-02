import { Navigate } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { getDocumentationView } from "@/features/operator/docs/documentation-release-pack"

export function DocumentationRootRedirect() {
  const { language } = useI18n()
  const documentationView = getDocumentationView(language)

  return <Navigate to={`/docs/${documentationView.defaultDocument.key}`} replace />
}
