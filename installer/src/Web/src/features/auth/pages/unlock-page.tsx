import { Navigate, useLocation } from "react-router-dom"

/**
 * Legacy browser route retained only so existing bookmarks fail closed into the
 * scoped first-owner journey. Browser code must not call the generic installer
 * unlock API: a `mem_` code belongs exclusively to `/bootstrap`.
 */
export function UnlockPage() {
  const location = useLocation()

  return (
    <Navigate
      to="/bootstrap"
      replace
      state={location.state}
    />
  )
}
