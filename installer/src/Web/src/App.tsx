import { Outlet, useLocation } from "react-router-dom"

import { AppShell } from "@/components/layout/app-shell"

export default function App() {
  const { pathname } = useLocation()

  if (pathname === "/") {
    return <Outlet />
  }

  return (
    <AppShell>
      <Outlet />
    </AppShell>
  )
}
