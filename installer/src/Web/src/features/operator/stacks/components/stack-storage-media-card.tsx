import { HardDrive, Image, Info as InfoIcon } from "lucide-react"

import { formatBytes, formatNumber } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"

import type { RuntimeStackStorageResponse } from "../api/stacks.types"

type Props = {
  storage: RuntimeStackStorageResponse | null
  isLoading: boolean
}

/**
 * Read-only storage evidence sourced from the HostAgent runtime-stack storage
 * inspection. It intentionally does not offer file browsing, deletion, or
 * cleanup controls.
 */
export function StackStorageMediaCard({ storage, isLoading }: Props) {
  const { language, t } = useI18n()
  const matrix = storage?.matrix ?? null
  const element = storage?.element ?? null

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <HardDrive className="h-5 w-5" />
              {t("stacks.storage.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {t("stacks.storage.description")}
            </p>
          </div>

          {matrix && (
            <div className="rounded-full border border-border bg-background/40 px-3 py-1 text-xs text-muted-foreground">
              {t("stacks.storage.sizeAndFiles", {
                size: formatBytes(matrix.totalBytes, language),
                files: t("stacks.storage.files", { count: matrix.totalFiles }),
              })}
            </div>
          )}
        </div>
      </CardHeader>

      <CardContent className="space-y-4">
        {isLoading && !storage ? (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("stacks.storage.loading")}
          </div>
        ) : !storage || !matrix ? (
          <div className="rounded-xl border border-dashed border-border p-4 text-sm text-muted-foreground">
            {t("stacks.storage.unavailable")}
          </div>
        ) : (
          <>
            <div className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
              <Info label={t("stacks.storage.mediaStore")} value={matrix.mediaStoreExists ? t("stacks.storage.available") : t("stacks.storage.notPresent")} />
              <Info label={t("stacks.storage.matrixMediaSize")} value={formatBytes(matrix.totalBytes, language)} />
              <Info label={t("stacks.storage.matrixMediaFiles")} value={formatNumber(matrix.totalFiles, language)} />
              <Info label={t("stacks.storage.homeserverConfig")} value={formatBytes(matrix.homeserverYamlBytes, language)} />
              <Info label={t("stacks.storage.signingKey")} value={formatBytes(matrix.signingKeyBytes, language)} />
              <Info label={t("stacks.storage.elementConfig")} value={formatBytes(element?.configBytes ?? 0, language)} />
              <Info
                label={t("stacks.storage.mediaSectionInventory")}
                value={matrix.sections.length === 0 ? t("stacks.storage.notReported") : t("stacks.storage.reportedSections", { count: matrix.sections.length })}
              />
            </div>

            <div className="rounded-xl border border-sky-500/20 bg-sky-500/10 p-4 text-sm text-sky-100">
              <div className="flex gap-3">
                <InfoIcon className="mt-0.5 h-4 w-4 shrink-0" />
                <div>
                  <div className="font-medium">{t("stacks.storage.recoveryBoundaryTitle")}</div>
                  <p className="mt-1 text-sky-100/90">
                    {t("stacks.storage.recoveryBoundaryPrefix")} {" "}
                    <code className="rounded bg-background/50 px-1 py-0.5">media_store</code>{" "}
                    {t("stacks.storage.recoveryBoundarySuffix")}
                  </p>
                </div>
              </div>
            </div>

            <div className="rounded-xl border border-border bg-background/40 p-4">
              <div className="mb-3 flex items-center gap-2 text-sm font-medium">
                <Image className="h-4 w-4" />
                {t("stacks.storage.mediaSectionsTitle")}
              </div>

              {matrix.sections.length === 0 ? (
                <div className="rounded-lg border border-dashed border-border p-3 text-sm text-muted-foreground">
                  {t("stacks.storage.mediaSectionsUnavailable")}
                </div>
              ) : (
                <div className="grid gap-3 md:grid-cols-2">
                  {matrix.sections.map((section) => (
                    <div key={section.key} className="rounded-lg border border-border bg-background/40 p-3">
                      <div className="flex items-start justify-between gap-3">
                        <div>
                          <div className="text-sm font-medium">{section.displayName}</div>
                          <div className="mt-1 text-xs text-muted-foreground">
                            {section.exists ? t("stacks.storage.available") : t("stacks.storage.notCreatedYet")}
                          </div>
                        </div>

                        <div className="text-right text-xs text-muted-foreground">
                          <div>{formatBytes(section.bytes, language)}</div>
                          <div>{t("stacks.storage.files", { count: section.files })}</div>
                        </div>
                      </div>

                      <div className="mt-3 break-all font-mono text-xs text-muted-foreground">
                        {section.path}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <details className="group rounded-xl border border-border bg-background/40">
              <summary className="flex cursor-pointer list-none items-center justify-between gap-4 px-4 py-3 marker:hidden [&::-webkit-details-marker]:hidden">
                <div>
                  <div className="text-sm font-medium">{t("stacks.storage.pathsTitle")}</div>
                  <p className="mt-1 text-xs text-muted-foreground">
                    {t("stacks.storage.pathsDescription")}
                  </p>
                </div>
                <span className="text-xs font-medium text-muted-foreground group-open:hidden">{t("stacks.technicalDetails.show")}</span>
                <span className="hidden text-xs font-medium text-muted-foreground group-open:inline">{t("stacks.technicalDetails.hide")}</span>
              </summary>

              <div className="grid gap-3 border-t border-border px-4 py-4 text-sm md:grid-cols-2">
                <PathInfo label={t("stacks.storage.matrixDataPath")} value={matrix.dataPath} />
                <PathInfo label={t("stacks.storage.mediaStorePath")} value={matrix.mediaStorePath} />
                <PathInfo label={t("stacks.storage.homeserverYaml")} value={matrix.homeserverYamlPath ?? t("stacks.storage.notFound")} />
                <PathInfo label={t("stacks.storage.signingKey")} value={matrix.signingKeyPath ?? t("stacks.storage.notFound")} />
                <PathInfo label={t("stacks.storage.elementDataPath")} value={element?.dataPath ?? t("stacks.storage.notFound")} />
                <PathInfo label={t("stacks.storage.elementConfig")} value={element?.configPath ?? t("stacks.storage.notFound")} />
              </div>
            </details>
          </>
        )}
      </CardContent>
    </Card>
  )
}

function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-foreground">{value}</div>
    </div>
  )
}

function PathInfo({ label, value }: { label: string; value: string }) {
  return (
    <div className="md:col-span-2">
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-all font-mono text-xs text-foreground">{value}</div>
    </div>
  )
}
