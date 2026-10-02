import type { RouteObject } from "react-router-dom"
import { Navigate } from "react-router-dom"

import { DocumentationDocumentPage } from "@/features/operator/docs/pages/documentation-document-page"
import { DocumentationHomePage } from "@/features/operator/docs/pages/documentation-home-page"

export const setupDocumentationRoutes: RouteObject[] = [
  {
    path: "docs",
    element: <DocumentationHomePage />,
  },
  {
    path: "docs/*",
    element: <DocumentationDocumentPage />,
  },
]

export const documentationRoutes: RouteObject[] = [
  {
    path: "docs",
    element: <DocumentationHomePage />,
  },
  {
    path: "docs/index",
    element: <Navigate to="/docs" replace />,
  },
  {
    path: "docs/*",
    element: <DocumentationDocumentPage />,
  },
]
