import { Moon, Sun } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { useTheme } from "@/app/theme"
import { Button } from "@/components/ui/button"

export function ThemeToggle() {
  const { theme, toggleTheme } = useTheme()
  const { t } = useI18n()

  const isDark = theme === "dark"
  const label = isDark ? t("theme.switchToLight") : t("theme.switchToDark")

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      onClick={toggleTheme}
      aria-label={label}
      className="border-border bg-background text-muted-foreground hover:bg-muted hover:text-foreground dark:border-white/10 dark:bg-white/5 dark:text-white dark:hover:bg-white/10"
    >
      {isDark ? (
        <>
          <Sun className="mr-2 h-4 w-4" />
          {label}
        </>
      ) : (
        <>
          <Moon className="mr-2 h-4 w-4" />
          {label}
        </>
      )}
    </Button>
  )
}
