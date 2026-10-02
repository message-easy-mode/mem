import { useI18n } from "@/app/i18n/i18n-context"
import type {
  ManagedServiceActionLink,
  ManagedServiceLink,
} from "@/features/operator/services/api/runtime-ui.types"

type Props = {
  image: string | null
  containerName: string | null
  actualPorts: string[]
  hostPaths: string[]
  warnings: string[]
  urls: ManagedServiceLink[]
  actions?: ManagedServiceActionLink[]
  notes: string[]
}

export function ServiceDetailsPanel({
  image,
  containerName,
  actualPorts,
  hostPaths,
  warnings,
  urls,
  actions = [],
  notes,
}: Props) {
  const { t } = useI18n()

  return (
    <div className="mt-3 rounded-lg border border-border bg-background/40 p-4 text-sm">
      <div className="grid gap-4 md:grid-cols-2">
        <DetailGroup title={t("services.details.runtime")}>
          <DetailItem
            label={t("services.details.container")}
            value={containerName ?? "-"}
            technical
          />
          <DetailItem
            label={t("services.details.image")}
            value={image ?? "-"}
            technical
          />
        </DetailGroup>

        <DetailGroup title={t("services.details.ports")}>
          {actualPorts.length > 0 ? (
            <div className="space-y-1">
              {actualPorts.map((port) => (
                <div key={port}>{port}</div>
              ))}
            </div>
          ) : (
            <div>-</div>
          )}
        </DetailGroup>

        <DetailGroup title={t("services.details.quickLinks")}>
          {urls.length > 0 || actions.length > 0 ? (
            <div className="space-y-3">
              {actions.map((action) => (
                <a
                  key={`${action.label}-${action.href}`}
                  href={action.href}
                  className="block rounded-md border border-border bg-card px-3 py-2 font-medium hover:bg-accent hover:text-accent-foreground"
                >
                  <span>{action.label}</span>
                  {action.description && (
                    <span className="mt-1 block text-xs font-normal text-muted-foreground">
                      {action.description}
                    </span>
                  )}
                </a>
              ))}

              {urls.map((url) => (
                <div key={`${url.label}-${url.href}`}>
                  <div className="text-xs uppercase tracking-wide text-muted-foreground">
                    {url.label}
                  </div>
                  <a
                    href={url.href}
                    target="_blank"
                    rel="noreferrer"
                    className="break-all font-medium underline underline-offset-4"
                  >
                    {url.href}
                  </a>
                </div>
              ))}
            </div>
          ) : (
            <div>-</div>
          )}
        </DetailGroup>

        <DetailGroup title={t("services.details.hostPaths")}>
          {hostPaths.length > 0 ? (
            <div className="space-y-1">
              {hostPaths.map((path) => (
                <div key={path} className="break-all">
                  {path}
                </div>
              ))}
            </div>
          ) : (
            <div>-</div>
          )}
        </DetailGroup>

        <DetailGroup title={t("services.details.notes")}>
          {notes.length > 0 ? (
            <div className="space-y-1">
              {notes.map((note, index) => (
                <div key={`${note}-${index}`}>{note}</div>
              ))}
            </div>
          ) : (
            <div>-</div>
          )}
        </DetailGroup>

        <DetailGroup title={t("services.details.warnings")}>
          {warnings.length > 0 ? (
            <div className="space-y-1 text-amber-300">
              {warnings.map((warning, index) => (
                <div key={`${warning}-${index}`}>{warning}</div>
              ))}
            </div>
          ) : (
            <div>{t("services.details.none")}</div>
          )}
        </DetailGroup>
      </div>
    </div>
  )
}

function DetailGroup({
  title,
  children,
}: {
  title: string
  children: React.ReactNode
}) {
  return (
    <div className="min-w-0 space-y-2 overflow-hidden rounded-lg border border-border/80 bg-background/30 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {title}
      </div>
      <div className="space-y-1 text-sm">{children}</div>
    </div>
  )
}

function DetailItem({
  label,
  value,
  technical = false,
}: {
  label: string
  value: string
  technical?: boolean
}) {
  return (
    <div className="min-w-0">
      <span className="text-muted-foreground">{label}:</span>{" "}
      <span
        className={
          technical
            ? "select-text break-all font-mono text-xs leading-5"
            : "break-words"
        }
      >
        {value}
      </span>
    </div>
  )
}
