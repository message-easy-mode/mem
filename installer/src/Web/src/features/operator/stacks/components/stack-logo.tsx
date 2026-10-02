import { useEffect, useState } from "react"

import { cn } from "@/lib/utils"

type StackLogoProps = {
  displayName: string
  logoUrl?: string | null
  className?: string
}

export function StackLogo({ displayName, logoUrl, className }: StackLogoProps) {
  const [imageFailed, setImageFailed] = useState(false)

  useEffect(() => {
    setImageFailed(false)
  }, [logoUrl])

  return (
    <span
      className={cn(
        "flex shrink-0 items-center justify-center overflow-hidden border border-border bg-muted/30 font-semibold uppercase tracking-wide text-muted-foreground",
        className,
      )}
    >
      {logoUrl && !imageFailed ? (
        <img
          src={logoUrl}
          alt=""
          aria-hidden="true"
          className="h-full w-full object-cover"
          onError={() => setImageFailed(true)}
        />
      ) : (
        <span aria-hidden="true">{stackInitials(displayName)}</span>
      )}
    </span>
  )
}

export function stackInitials(value: string) {
  const parts = value
    .split(/[-_\s]+/)
    .map((part) => part.trim())
    .filter(Boolean)

  if (parts.length === 0) return "•"
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return `${parts[0][0] ?? ""}${parts[1][0] ?? ""}`.toUpperCase()
}
