// src/features/operator/domains/pages/operator-domain-create-page.tsx

import { useState } from "react"
import { useNavigate } from "react-router-dom"
import { ArrowLeft, Loader2, Plus } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

import { getErrorMessage } from "@/features/shared/domains/components/ingress-tls-shared"
import { useCreateOperatorDomain } from "@/features/shared/domains/hooks/use-domains"

type CreateDomainForm = {
  baseDomain: string
  displayName: string
  dnsZone: string
  notes: string
}

export function OperatorDomainCreatePage() {
  const navigate = useNavigate()
  const { t } = useI18n()
  const createDomain = useCreateOperatorDomain()

  const [form, setForm] = useState<CreateDomainForm>({
    baseDomain: "",
    displayName: "",
    dnsZone: "",
    notes: "",
  })

  function updateField<TKey extends keyof CreateDomainForm>(
    key: TKey,
    value: CreateDomainForm[TKey],
  ) {
    setForm((current) => ({
      ...current,
      [key]: value,
    }))
  }

  const normalizedBaseDomain = normaliseBaseDomain(form.baseDomain)
  const effectiveDnsZone = normaliseBaseDomain(form.dnsZone) || normalizedBaseDomain
  const wildcardDomain = normalizedBaseDomain
    ? `*.${normalizedBaseDomain}`
    : "*.example.com"

  const canSubmit = normalizedBaseDomain.length > 0 && !createDomain.isPending

  function handleSubmit() {
    if (!canSubmit) return

    createDomain.mutate(
      {
        baseDomain: normalizedBaseDomain,
        displayName: form.displayName.trim() || normalizedBaseDomain,
        // Purpose is registry metadata, not a certificate environment choice.
        // The API promotes the first domain to platform-main; later domains are
        // registered for future stack use without asking the operator to manage
        // this legacy classification.
        purpose: "stack",
        dnsProvider: "desec",
        dnsZone: effectiveDnsZone,
        notes: form.notes.trim() || null,
      },
      {
        onSuccess: (domain) => {
          navigate(`/domains/${domain.id}`)
        },
      },
    )
  }

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="space-y-2">
          <PageBreadcrumbs
            items={[
              { label: t("operatorDomains.common.domains"), to: "/domains" },
              { label: t("operatorDomains.create.title") },
            ]}
          />

          <h1 className="text-2xl font-semibold tracking-tight">
            {t("operatorDomains.create.title")}
          </h1>
          <p className="max-w-3xl text-sm text-muted-foreground">
            {t("operatorDomains.create.description")}
          </p>
        </div>

        <Button variant="outline" onClick={() => navigate("/domains")}>
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("operatorDomains.common.backToDomains")}
        </Button>
      </div>

      <Card className="border-sky-500/30 bg-sky-500/5">
        <CardContent className="space-y-2 p-4 text-sm text-muted-foreground">
          <div className="font-medium text-foreground">
            {t("operatorDomains.create.registrationOnly")}
          </div>
          <p>{t("operatorDomains.create.registrationDescription1")}</p>
          <p>{t("operatorDomains.create.registrationDescription2")}</p>
        </CardContent>
      </Card>

      <Card>
        <CardHeader className="text-center">
          <CardTitle>{t("operatorDomains.create.cardTitle")}</CardTitle>
          <CardDescription>{t("operatorDomains.create.cardDescription")}</CardDescription>
        </CardHeader>

        <CardContent className="mx-auto max-w-xl space-y-6">
          <div className="rounded-2xl border border-border bg-muted/20 p-4 text-sm">
            <div className="font-medium text-foreground">
              {t("operatorDomains.create.thisStepWill")}
            </div>
            <ul className="mt-2 space-y-1 text-muted-foreground">
              <li>✓ {t("operatorDomains.create.willRegister")}</li>
              <li>✓ {t("operatorDomains.create.willProvider")}</li>
              <li>✓ {t("operatorDomains.create.willZone")}</li>
              <li>✓ {t("operatorDomains.create.willRole")}</li>
              <li>— {t("operatorDomains.create.willNoExternal")}</li>
            </ul>
          </div>

          <div className="space-y-2">
            <Label htmlFor="baseDomain">{t("operatorDomains.create.baseDomain")}</Label>
            <Input
              id="baseDomain"
              placeholder="example.com"
              autoComplete="off"
              disabled={createDomain.isPending}
              value={form.baseDomain}
              onChange={(event) => updateField("baseDomain", event.target.value)}
            />
            <p className="text-xs text-muted-foreground">
              {t("operatorDomains.create.plannedWildcard", { domain: wildcardDomain })}
            </p>
          </div>

          <div className="space-y-2">
            <Label htmlFor="dnsProvider">{t("operatorDomains.create.dnsProvider")}</Label>
            <Input id="dnsProvider" value="deSEC" disabled />
            <p className="text-xs text-muted-foreground">
              {t("operatorDomains.create.desecSupported")}
            </p>
          </div>

          <details className="rounded-2xl border border-border bg-muted/10 p-4">
            <summary className="cursor-pointer text-sm font-medium text-foreground">
              {t("operatorDomains.create.advancedZone")}
            </summary>
            <div className="mt-4 space-y-2">
              <Label htmlFor="dnsZone">{t("operatorDomains.create.zoneOverride")}</Label>
              <Input
                id="dnsZone"
                placeholder={normalizedBaseDomain || "example.com"}
                autoComplete="off"
                disabled={createDomain.isPending}
                value={form.dnsZone}
                onChange={(event) => updateField("dnsZone", event.target.value)}
              />
              <p className="text-xs text-muted-foreground">
                {t("operatorDomains.create.zoneHelp")}
              </p>
            </div>
          </details>

          <details className="rounded-2xl border border-border bg-muted/10 p-4">
            <summary className="cursor-pointer text-sm font-medium text-foreground">
              {t("operatorDomains.create.optionalDetails")}
            </summary>
            <div className="mt-4 space-y-5">
              <div className="space-y-2">
                <Label htmlFor="displayName">
                  {t("operatorDomains.create.displayName")}
                </Label>
                <Input
                  id="displayName"
                  placeholder={normalizedBaseDomain || "example.com"}
                  disabled={createDomain.isPending}
                  value={form.displayName}
                  onChange={(event) => updateField("displayName", event.target.value)}
                />
              </div>

              <div className="space-y-2">
                <Label htmlFor="notes">{t("operatorDomains.create.notes")}</Label>
                <Input
                  id="notes"
                  placeholder={t("operatorDomains.create.notesPlaceholder")}
                  disabled={createDomain.isPending}
                  value={form.notes}
                  onChange={(event) => updateField("notes", event.target.value)}
                />
              </div>
            </div>
          </details>

          {createDomain.error ? (
            <div className="rounded-xl border border-red-500/30 bg-red-500/5 p-4 text-sm text-red-200">
              {t("operatorDomains.create.failed", {
                error: getErrorMessage(createDomain.error),
              })}
            </div>
          ) : null}

          <Button className="w-full" onClick={handleSubmit} disabled={!canSubmit}>
            {createDomain.isPending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                {t("operatorDomains.create.creating")}
              </>
            ) : (
              <>
                <Plus className="mr-2 h-4 w-4" />
                {t("operatorDomains.create.create")}
              </>
            )}
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}

function normaliseBaseDomain(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/^https?:\/\//, "")
    .replace(/^\*\./, "")
    .replace(/\/.*$/, "")
    .replace(/^\.+|\.+$/g, "")
}
