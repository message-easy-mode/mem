import { Link } from "react-router-dom"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type { PlatformStatusResponse } from "../api/platform-status"

export function RecommendedActionCard({ status }: { status: PlatformStatusResponse }) {
  const installed = status.installationState === "installed"
  const partiallyInstalled = status.installationState === "partially-installed"
  const migrationRequired = status.setupMode === "migration-required"

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Recommended next step</CardTitle>
      </CardHeader>

      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {installed
            ? "The private MEM control plane appears to be installed. Continue in the operator dashboard, or run diagnostics if something needs attention."
            : migrationRequired
              ? "A legacy v0.1.0 API or Web application was detected. Keep it intact and use the separate mem-migrate tool rather than running a fresh installation over it."
              : partiallyInstalled
                ? "Some MEM resources already exist. Run diagnostics before applying more changes so MEM does not overwrite or misunderstand existing resources."
                : "Run server checks before changing the server. This confirms Docker, ports, storage, networking, and existing platform state."}
        </p>

        <div className="flex flex-col gap-2 sm:flex-row">
          {!installed && !migrationRequired ? (
            <Button asChild>
              <Link to="/setup/check-server">
                {partiallyInstalled ? "Run repair check" : "Run server checks"}
              </Link>
            </Button>
          ) : null}

          {installed ? (
            <Button asChild>
              <Link to="/dashboard">Open operator dashboard</Link>
            </Button>
          ) : null}

          <Button asChild variant="outline">
            <Link to="/diagnostics">Run diagnostics</Link>
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}
