import { useEffect, useRef, useState, type ChangeEvent } from "react"
import { ImageUp, Pencil, Tag, Trash2, UserRound } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"

import type { RuntimeStackInspectResponse } from "../api/stacks.types"
import {
  useRemoveRuntimeStackLogo,
  useUpdateRuntimeStackIdentity,
  useUploadRuntimeStackLogo,
} from "../hooks/use-runtime-stacks"
import { StackLogo } from "./stack-logo"

type StackIdentityCardProps = {
  stack: RuntimeStackInspectResponse
}

export function StackIdentityCard({ stack }: StackIdentityCardProps) {
  const { t } = useI18n()
  const updateIdentity = useUpdateRuntimeStackIdentity(stack.slug)
  const uploadLogo = useUploadRuntimeStackLogo(stack.slug)
  const removeLogo = useRemoveRuntimeStackLogo(stack.slug)
  const logoInputRef = useRef<HTMLInputElement>(null)
  const resolvedDisplayName = stack.displayName?.trim() || stack.slug
  const [editing, setEditing] = useState(false)
  const [displayName, setDisplayName] = useState(resolvedDisplayName)
  const [category, setCategory] = useState(stack.category?.trim() || "")

  useEffect(() => {
    if (editing) return
    setDisplayName(resolvedDisplayName)
    setCategory(stack.category?.trim() || "")
  }, [editing, resolvedDisplayName, stack.category])

  const logoPending = uploadLogo.isPending || removeLogo.isPending
  const logoError = uploadLogo.error ?? removeLogo.error

  function saveIdentity() {
    const normalizedName = displayName.trim()
    if (!normalizedName) return

    updateIdentity.mutate(
      {
        displayName: normalizedName,
        category: category.trim() || null,
      },
      {
        onSuccess: () => setEditing(false),
      },
    )
  }

  function cancelEditing() {
    updateIdentity.reset()
    setDisplayName(resolvedDisplayName)
    setCategory(stack.category?.trim() || "")
    setEditing(false)
  }

  function chooseLogo() {
    uploadLogo.reset()
    removeLogo.reset()
    logoInputRef.current?.click()
  }

  function onLogoSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    event.target.value = ""
    if (!file) return

    removeLogo.reset()
    uploadLogo.mutate(file)
  }

  function removeCurrentLogo() {
    uploadLogo.reset()
    removeLogo.mutate()
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <UserRound className="h-5 w-5" />
              {t("stacks.identity.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("stacks.identity.description")}
            </p>
          </div>

          {!editing ? (
            <Button variant="outline" size="sm" onClick={() => setEditing(true)}>
              <Pencil className="mr-2 h-4 w-4" />
              {t("stacks.identity.edit")}
            </Button>
          ) : null}
        </div>
      </CardHeader>

      <CardContent className="space-y-5">
        {logoError ? (
          <Alert variant="destructive">
            <AlertTitle>{t("stacks.identity.logoErrorTitle")}</AlertTitle>
            <AlertDescription>{logoError.message}</AlertDescription>
          </Alert>
        ) : null}

        <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
          <StackLogo
            displayName={resolvedDisplayName}
            logoUrl={stack.logoUrl}
            className="h-16 w-16 rounded-2xl text-lg"
          />

          <div className="min-w-0 flex-1">
            {!editing ? (
              <>
                <div className="text-lg font-semibold tracking-tight">{resolvedDisplayName}</div>
                <div className="mt-1 font-mono text-xs text-muted-foreground">{stack.slug}</div>
                {stack.category ? (
                  <div className="mt-3 inline-flex items-center gap-1.5 rounded-full border border-primary/25 bg-primary/10 px-2.5 py-1 text-xs font-medium text-primary">
                    <Tag className="h-3.5 w-3.5" />
                    {stack.category}
                  </div>
                ) : (
                  <p className="mt-3 text-xs text-muted-foreground">
                    {t("stacks.identity.noCategory")}
                  </p>
                )}
              </>
            ) : (
              <>
                <div className="text-sm font-medium">{t("stacks.identity.logo")}</div>
                <p className="mt-1 max-w-xl text-xs text-muted-foreground">
                  {t("stacks.identity.logoHelper")}
                </p>
              </>
            )}
          </div>

          <div className="flex shrink-0 flex-wrap gap-2">
            <input
              ref={logoInputRef}
              type="file"
              accept="image/png,.png"
              className="sr-only"
              aria-label={t("stacks.identity.logoInput")}
              onChange={onLogoSelected}
              disabled={logoPending}
            />
            <Button variant="outline" size="sm" onClick={chooseLogo} disabled={logoPending}>
              <ImageUp className="mr-2 h-4 w-4" />
              {uploadLogo.isPending
                ? t("stacks.identity.logoUploading")
                : stack.logoUrl
                  ? t("stacks.identity.logoReplace")
                  : t("stacks.identity.logoUpload")}
            </Button>
            {stack.logoUrl ? (
              <Button
                variant="ghost"
                size="sm"
                onClick={removeCurrentLogo}
                disabled={logoPending}
                className="text-destructive hover:text-destructive"
              >
                <Trash2 className="mr-2 h-4 w-4" />
                {removeLogo.isPending
                  ? t("stacks.identity.logoRemoving")
                  : t("stacks.identity.logoRemove")}
              </Button>
            ) : null}
          </div>
        </div>

        {editing ? (
          <div className="space-y-4 border-t border-border pt-5">
            {updateIdentity.error ? (
              <Alert variant="destructive">
                <AlertTitle>{t("stacks.identity.updateErrorTitle")}</AlertTitle>
                <AlertDescription>{updateIdentity.error.message}</AlertDescription>
              </Alert>
            ) : null}

            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="stack-display-name">{t("stacks.identity.displayName")}</Label>
                <Input
                  id="stack-display-name"
                  value={displayName}
                  onChange={(event) => setDisplayName(event.target.value)}
                  maxLength={200}
                  autoComplete="off"
                />
                <p className="text-xs text-muted-foreground">
                  {t("stacks.identity.displayNameHelper")}
                </p>
              </div>

              <div className="space-y-2">
                <Label htmlFor="stack-category">{t("stacks.identity.category")}</Label>
                <Input
                  id="stack-category"
                  value={category}
                  onChange={(event) => setCategory(event.target.value)}
                  maxLength={40}
                  placeholder={t("stacks.identity.categoryPlaceholder")}
                  autoComplete="off"
                />
                <p className="text-xs text-muted-foreground">
                  {t("stacks.identity.categoryHelper")}
                </p>
              </div>
            </div>

            <div className="rounded-xl border border-border bg-background/40 p-3 text-sm">
              <div className="text-xs uppercase tracking-wide text-muted-foreground">
                {t("stacks.identity.slug")}
              </div>
              <div className="mt-1 font-mono text-xs text-foreground">{stack.slug}</div>
              <p className="mt-2 text-xs text-muted-foreground">
                {t("stacks.identity.slugHelper")}
              </p>
            </div>

            <div className="flex flex-wrap justify-end gap-2">
              <Button variant="outline" onClick={cancelEditing} disabled={updateIdentity.isPending}>
                {t("stacks.identity.cancel")}
              </Button>
              <Button
                onClick={saveIdentity}
                disabled={!displayName.trim() || updateIdentity.isPending}
              >
                {updateIdentity.isPending
                  ? t("stacks.identity.saving")
                  : t("stacks.identity.save")}
              </Button>
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  )
}
