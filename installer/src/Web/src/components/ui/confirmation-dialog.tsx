import * as React from "react"
import { LoaderCircle } from "lucide-react"
import { AlertDialog as AlertDialogPrimitive } from "radix-ui"

import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"

type ConfirmationDialogProps = {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: React.ReactNode
  confirmLabel: string
  confirmingLabel?: string
  cancelLabel?: string
  confirmVariant?: "default" | "destructive"
  onConfirm: () => void
  isConfirming?: boolean
  showProgress?: boolean
  confirmDisabled?: boolean
  children?: React.ReactNode
  className?: string
}

/**
 * Controlled, reusable confirmation prompt for consequential operator actions.
 *
 * This owns focus trapping and accessible alert-dialog semantics through Radix.
 * The caller owns the mutation and closes the dialog only after a successful
 * result, so errors remain visible and the operator can retry deliberately.
 */
export function ConfirmationDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  confirmingLabel = "Working...",
  cancelLabel = "Cancel",
  confirmVariant = "default",
  onConfirm,
  isConfirming = false,
  showProgress = false,
  confirmDisabled = false,
  children,
  className,
}: ConfirmationDialogProps) {
  return (
    <AlertDialogPrimitive.Root
      open={open}
      onOpenChange={(nextOpen) => {
        if (!isConfirming) {
          onOpenChange(nextOpen)
        }
      }}
    >
      <AlertDialogPrimitive.Portal>
        <AlertDialogPrimitive.Overlay
          className="fixed inset-0 z-50 bg-black/70 backdrop-blur-[1px]"
        />

        <AlertDialogPrimitive.Content
          className={cn(
            "fixed top-1/2 left-1/2 z-50 grid w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 -translate-y-1/2 gap-5 rounded-xl border border-border bg-card p-5 text-card-foreground shadow-2xl outline-none",
            className,
          )}
        >
          <div className="space-y-2">
            <AlertDialogPrimitive.Title className="text-lg font-semibold tracking-tight">
              {title}
            </AlertDialogPrimitive.Title>
            <AlertDialogPrimitive.Description className="text-sm leading-relaxed text-muted-foreground">
              {description}
            </AlertDialogPrimitive.Description>
          </div>

          {children ? <div className="space-y-3">{children}</div> : null}

          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <AlertDialogPrimitive.Cancel asChild>
              <Button
                type="button"
                variant="outline"
                disabled={isConfirming}
              >
                {cancelLabel}
              </Button>
            </AlertDialogPrimitive.Cancel>

            <Button
              type="button"
              variant={confirmVariant}
              onClick={onConfirm}
              disabled={isConfirming || confirmDisabled}
            >
              {showProgress ? (
                <LoaderCircle data-icon="inline-start" className="animate-spin" aria-hidden="true" />
              ) : null}
              {isConfirming || showProgress ? confirmingLabel : confirmLabel}
            </Button>
          </div>
        </AlertDialogPrimitive.Content>
      </AlertDialogPrimitive.Portal>
    </AlertDialogPrimitive.Root>
  )
}
