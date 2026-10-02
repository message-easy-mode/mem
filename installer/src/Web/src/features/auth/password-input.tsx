import { useEffect, useState, type ComponentProps } from "react"
import { Eye, EyeOff } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Input } from "@/components/ui/input"
import { cn } from "@/lib/utils"

type PasswordInputProps = Omit<ComponentProps<typeof Input>, "type">

/**
 * Password input with a local show/hide control. Visibility is never persisted
 * and automatically returns to hidden when the owning form clears the value.
 */
export function PasswordInput({ className, value, disabled, ...props }: PasswordInputProps) {
  const { t } = useI18n()
  const [visible, setVisible] = useState(false)

  useEffect(() => {
    if (value === "") {
      setVisible(false)
    }
  }, [value])

  const visibilityLabel = visible
    ? t("common.hidePassword")
    : t("common.showPassword")

  return (
    <div className="relative">
      <Input
        {...props}
        type={visible ? "text" : "password"}
        value={value}
        disabled={disabled}
        className={cn("pr-10", className)}
      />
      <button
        type="button"
        className="absolute inset-y-0 right-0 flex w-10 items-center justify-center rounded-r-lg text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-50"
        aria-label={visibilityLabel}
        aria-pressed={visible}
        title={visibilityLabel}
        disabled={disabled}
        onClick={() => setVisible((current) => !current)}
      >
        {visible ? (
          <EyeOff className="h-4 w-4" aria-hidden="true" />
        ) : (
          <Eye className="h-4 w-4" aria-hidden="true" />
        )}
      </button>
    </div>
  )
}
