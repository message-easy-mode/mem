import type { ReactNode } from "react"
import { Link } from "react-router-dom"
import { ArrowRight, ShieldCheck, Wrench, Server } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"

export function WelcomePage() {
  return (
    <div className="space-y-6">
      <section className="grid gap-6 lg:grid-cols-[1.3fr_0.9fr]">
        <Card className="border-border/80 bg-card/95 shadow-sm backdrop-blur">
          <CardHeader className="space-y-4">
            <div className="inline-flex w-fit rounded-full border border-cyan-400/20 bg-cyan-400/10 px-3 py-1 text-xs font-medium text-cyan-300">
              Guided installer
            </div>

            <div className="space-y-3">
              <CardTitle className="max-w-3xl text-3xl leading-tight tracking-tight text-foreground sm:text-5xl">
                MEM Control Plane setup.
              </CardTitle>

              <CardDescription className="max-w-2xl text-base leading-7 text-muted-foreground sm:text-lg">
                The MEM Control Plane performs host checks, captures installation configuration,
                generates runtime artifacts, launches the platform, and tracks installation
                status.
              </CardDescription>
            </div>
          </CardHeader>

          <CardContent className="flex flex-wrap gap-3">
            <Button
              asChild
              className="bg-primary text-primary-foreground shadow-[0_0_0_1px_rgba(255,255,255,0.04),0_10px_30px_rgba(16,185,129,0.18)] hover:brightness-105"
            >
              <Link to="/checks">
                Start system checks
                <ArrowRight className="ml-2 h-4 w-4" />
              </Link>
            </Button>

            <Button
              asChild
              variant="outline"
              className="border-border/80 bg-background/40 text-foreground hover:bg-accent"
            >
              <Link to="/config">Go to configuration</Link>
            </Button>
          </CardContent>
        </Card>

        <Card className="border-border/80 bg-card/95 shadow-sm backdrop-blur">
          <CardHeader className="space-y-2">
            <CardTitle className="text-foreground">How it works</CardTitle>
            <CardDescription className="text-muted-foreground">
              The first serious AIO operator flow.
            </CardDescription>
          </CardHeader>

          <CardContent className="space-y-5">
            <Step
              icon={<ShieldCheck className="h-4 w-4" />}
              title="Validate the host"
              body="Check Docker, install path readiness, and port availability."
              tone="info"
            />
            <Step
              icon={<Wrench className="h-4 w-4" />}
              title="Capture configuration"
              body="Choose install mode, runtime path, admin email, and optional services."
              tone="info"
            />
            <Step
              icon={<Server className="h-4 w-4" />}
              title="Generate and launch"
              body="Write artifacts, start the runtime, and track orchestration state."
              tone="success"
            />
          </CardContent>
        </Card>
      </section>
    </div>
  )
}

function Step({
  icon,
  title,
  body,
  tone = "info",
}: {
  icon: ReactNode
  title: string
  body: string
  tone?: "info" | "success"
}) {
  return (
    <div className="flex gap-3">
      <div
        className={
          tone === "success"
            ? "mt-0.5 inline-flex h-9 w-9 items-center justify-center rounded-full border border-emerald-400/20 bg-emerald-500/10 text-emerald-300"
            : "mt-0.5 inline-flex h-9 w-9 items-center justify-center rounded-full border border-cyan-400/20 bg-cyan-400/10 text-cyan-300"
        }
      >
        {icon}
      </div>

      <div className="space-y-1">
        <div className="font-medium text-foreground">{title}</div>
        <div className="text-sm leading-6 text-muted-foreground">{body}</div>
      </div>
    </div>
  )
}