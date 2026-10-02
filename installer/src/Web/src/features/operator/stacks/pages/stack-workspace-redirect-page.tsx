import { Navigate, useParams } from "react-router-dom"

type StackWorkspaceRedirectTarget = "overview" | "services" | "recovery"

type StackWorkspaceRedirectPageProps = {
  target: StackWorkspaceRedirectTarget
}

/**
 * Keeps retired stack-workspace routes safe after the information architecture
 * was reduced to fewer top-level sections. These redirects preserve old
 * bookmarks while sending operators to the canonical section that now owns the
 * evidence.
 */
export function StackWorkspaceRedirectPage({ target }: StackWorkspaceRedirectPageProps) {
  const { slugOrId } = useParams()

  if (!slugOrId) {
    return <Navigate to="/stacks" replace />
  }

  const stackPath = `/stacks/${encodeURIComponent(slugOrId)}`
  const destination = target === "overview" ? stackPath : `${stackPath}/${target}`

  return <Navigate to={destination} replace />
}
