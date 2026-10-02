import { Navigate, useParams } from "react-router-dom"

/**
 * Settings was retired from the Stack Workspace because every inspected fact
 * it displayed now has a more useful home in a focused, read-only area.
 *
 * Keep old bookmarks safe by redirecting them to Stack Overview rather than
 * presenting a redundant configuration record.
 */
export function StackSettingsPage() {
  const { slugOrId } = useParams()

  return (
    <Navigate
      to={slugOrId ? `/stacks/${encodeURIComponent(slugOrId)}` : "/stacks"}
      replace
    />
  )
}
