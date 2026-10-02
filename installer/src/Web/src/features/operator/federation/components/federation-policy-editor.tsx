import { CircleAlert, Globe2, ListChecks, RotateCcw, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey, TranslationValues } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import { cn } from "@/lib/utils"

import type { ManagedFederationMode } from "../api/federation.types"
import type { FederationDraftValidation } from "./federation-policy-draft"

type FederationPolicyEditorProps = {
  mode: ManagedFederationMode
  domainText: string
  validation: FederationDraftValidation
  disabled?: boolean
  disabledReason?: string | null
  reviewPending?: boolean
  reviewError?: string | null
  onModeChange: (mode: ManagedFederationMode) => void
  onDomainTextChange: (value: string) => void
  onReset: () => void
  onReview: () => void
}

export function FederationPolicyEditor({
  mode,
  domainText,
  validation,
  disabled = false,
  disabledReason,
  reviewPending = false,
  reviewError,
  onModeChange,
  onDomainTextChange,
  onReset,
  onReview,
}: FederationPolicyEditorProps) {
  const { t } = useI18n()

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("federation.editor.title")}</CardTitle>
        <CardDescription>{t("federation.editor.description")}</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        {disabledReason ? (
          <Alert className="border-amber-500/30 bg-amber-500/10">
            <CircleAlert className="h-4 w-4" />
            <AlertTitle>{t("federation.editor.unavailableTitle")}</AlertTitle>
            <AlertDescription>{disabledReason}</AlertDescription>
          </Alert>
        ) : null}

        <fieldset disabled={disabled || reviewPending} className="space-y-3">
          <legend className="text-sm font-medium">{t("federation.editor.modeLabel")}</legend>
          <div className="grid gap-3 md:grid-cols-3">
            <ModeChoice
              selected={mode === "public"}
              icon={Globe2}
              title={t("federation.mode.public")}
              description={t("federation.mode.publicDescription")}
              onClick={() => onModeChange("public")}
            />
            <ModeChoice
              selected={mode === "restricted"}
              icon={ListChecks}
              title={t("federation.mode.restricted")}
              description={t("federation.mode.restrictedDescription")}
              onClick={() => onModeChange("restricted")}
            />
            <ModeChoice
              selected={mode === "local_only"}
              icon={ShieldCheck}
              title={t("federation.mode.localOnly")}
              description={t("federation.mode.localOnlyDescription")}
              onClick={() => onModeChange("local_only")}
            />
          </div>
        </fieldset>

        {mode === "restricted" ? (
          <div className="space-y-3">
            <div className="space-y-1">
              <Label htmlFor="federation-approved-homeservers">
                {t("federation.editor.allowlistLabel")}
              </Label>
              <p className="text-sm text-muted-foreground">
                {t("federation.editor.allowlistHelp")}
              </p>
            </div>
            <textarea
              id="federation-approved-homeservers"
              className="min-h-32 w-full rounded-lg border border-input bg-transparent px-3 py-2 font-mono text-sm outline-none transition-colors placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:bg-input/50 disabled:opacity-50"
              value={domainText}
              disabled={disabled || reviewPending}
              autoCorrect="off"
              autoCapitalize="none"
              spellCheck={false}
              placeholder={t("federation.editor.allowlistPlaceholder")}
              onChange={(event) => onDomainTextChange(event.target.value)}
            />

            {validation.issues.length > 0 ? (
              <Alert variant="destructive">
                <AlertTitle>{t("federation.editor.validationTitle")}</AlertTitle>
                <AlertDescription>
                  <ul className="list-disc space-y-1 pl-5">
                    {validation.issues.map((issue, index) => (
                      <li key={`${issue.code}-${issue.value ?? index}`}>
                        {draftIssueMessage(issue.code, issue.value, t)}
                      </li>
                    ))}
                  </ul>
                </AlertDescription>
              </Alert>
            ) : validation.canonicalAllowlist.length > 0 ? (
              <div className="space-y-2 rounded-lg border bg-muted/20 p-3">
                <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t("federation.editor.canonicalPreview")}
                </p>
                <div className="flex flex-wrap gap-2">
                  {validation.canonicalAllowlist.map((domain) => (
                    <code key={domain} className="rounded border bg-background px-2 py-1 text-xs">
                      {domain}
                    </code>
                  ))}
                </div>
              </div>
            ) : null}

            <Alert>
              <ShieldCheck className="h-4 w-4" />
              <AlertTitle>{t("federation.editor.restrictedBoundaryTitle")}</AlertTitle>
              <AlertDescription>{t("federation.editor.restrictedBoundaryDescription")}</AlertDescription>
            </Alert>
          </div>
        ) : mode === "local_only" ? (
          <div className="space-y-3">
            <p className="rounded-lg border bg-muted/20 p-3 text-sm text-muted-foreground">
              {t("federation.editor.localOnlyAllowlistEmpty")}
            </p>
            <Alert className="border-amber-500/30 bg-amber-500/10">
              <ShieldCheck className="h-4 w-4" />
              <AlertTitle>{t("federation.editor.localOnlyBoundaryTitle")}</AlertTitle>
              <AlertDescription className="space-y-2">
                <p>{t("federation.editor.localOnlyBoundaryDescription")}</p>
                <p>{t("federation.editor.localOnlyClientCaveat")}</p>
                <p>{t("federation.editor.localOnlyHistoryCaveat")}</p>
              </AlertDescription>
            </Alert>
          </div>
        ) : (
          <p className="rounded-lg border bg-muted/20 p-3 text-sm text-muted-foreground">
            {t("federation.editor.publicAllowlistEmpty")}
          </p>
        )}

        {reviewError ? (
          <Alert variant="destructive">
            <AlertTitle>{t("federation.editor.reviewFailedTitle")}</AlertTitle>
            <AlertDescription>{reviewError}</AlertDescription>
          </Alert>
        ) : null}

        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button
            type="button"
            variant="outline"
            disabled={disabled || reviewPending}
            onClick={onReset}
          >
            <RotateCcw className="h-4 w-4" />
            {t("federation.editor.reset")}
          </Button>
          <Button
            type="button"
            disabled={disabled || reviewPending || !validation.valid}
            onClick={onReview}
          >
            {reviewPending
              ? t("federation.editor.reviewing")
              : t("federation.editor.review")}
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

function ModeChoice({
  selected,
  icon: Icon,
  title,
  description,
  onClick,
}: {
  selected: boolean
  icon: typeof Globe2
  title: string
  description: string
  onClick: () => void
}) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      className={cn(
        "rounded-lg border p-4 text-left transition-colors",
        selected
          ? "border-primary bg-primary/5 ring-2 ring-primary/20"
          : "bg-muted/20 hover:bg-muted/40",
      )}
      onClick={onClick}
    >
      <div className="flex items-center gap-2 font-medium">
        <Icon className="h-4 w-4" aria-hidden="true" />
        {title}
      </div>
      <p className="mt-2 text-sm text-muted-foreground">{description}</p>
    </button>
  )
}

function draftIssueMessage(
  code: FederationDraftValidation["issues"][number]["code"],
  value: string | undefined,
  t: (key: TranslationKey, values?: TranslationValues) => string,
) {
  switch (code) {
    case "public_allowlist_not_empty":
      return t("federation.editor.error.publicAllowlist")
    case "local_only_allowlist_not_empty":
      return t("federation.editor.error.localOnlyAllowlist")
    case "restricted_allowlist_required":
      return t("federation.editor.error.restrictedRequired")
    case "duplicate_domain":
      return t("federation.editor.error.duplicateDomain", { domain: value ?? "" })
    default:
      return t("federation.editor.error.invalidDomain", { domain: value ?? "" })
  }
}
