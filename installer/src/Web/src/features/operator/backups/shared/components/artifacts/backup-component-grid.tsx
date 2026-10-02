import type { ReactNode } from "react"
import {
  CheckCircle2,
  Database,
  FileJson,
  FileKey2,
  Image,
  XCircle,
} from "lucide-react"

import type {
  RuntimeStackBackupHistoryComponents,
  RuntimeStackBackupHistoryDirectoryComponent,
  RuntimeStackBackupHistoryFileComponent,
} from "@/features/operator/backups/api/types/backups.types"

import { formatBytes } from "@/features/operator/backups/shared/components/backup-formatting"

export function ComponentGrid({ components }: { components: RuntimeStackBackupHistoryComponents }) {
  return (
    <div className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-5">
      <FileComponentPill
        icon={<Database className="h-4 w-4" />}
        label="Database"
        component={components.databaseDump}
      />
      <FileComponentPill
        icon={<FileJson className="h-4 w-4" />}
        label="Homeserver"
        component={components.matrixConfig}
      />
      <FileComponentPill
        icon={<FileKey2 className="h-4 w-4" />}
        label="Signing key"
        component={components.matrixSigningKey}
      />
      <DirectoryComponentPill
        icon={<Image className="h-4 w-4" />}
        label="Media"
        component={components.matrixMediaStore}
      />
      <FileComponentPill
        icon={<FileJson className="h-4 w-4" />}
        label="Element"
        component={components.elementConfig}
      />
    </div>
  )
}

function FileComponentPill({
  icon,
  label,
  component,
}: {
  icon: ReactNode
  label: string
  component: RuntimeStackBackupHistoryFileComponent
}) {
  return (
    <div className="rounded-lg border border-border bg-background/40 p-3">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium">
          {icon}
          {label}
        </div>
        {component.present ? (
          <CheckCircle2 className="h-4 w-4 text-emerald-400" />
        ) : (
          <XCircle className="h-4 w-4 text-destructive" />
        )}
      </div>
      <div className="mt-1 text-xs text-muted-foreground">
        {component.present ? formatBytes(component.bytes) : "missing"}
      </div>
    </div>
  )
}

function DirectoryComponentPill({
  icon,
  label,
  component,
}: {
  icon: ReactNode
  label: string
  component: RuntimeStackBackupHistoryDirectoryComponent
}) {
  return (
    <div className="rounded-lg border border-border bg-background/40 p-3">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-sm font-medium">
          {icon}
          {label}
        </div>
        {component.present ? (
          <CheckCircle2 className="h-4 w-4 text-emerald-400" />
        ) : (
          <XCircle className="h-4 w-4 text-destructive" />
        )}
      </div>
      <div className="mt-1 text-xs text-muted-foreground">
        {component.present
          ? `${formatBytes(component.bytes)} · ${component.files} file${component.files === 1 ? "" : "s"}`
          : "missing"}
      </div>
    </div>
  )
}