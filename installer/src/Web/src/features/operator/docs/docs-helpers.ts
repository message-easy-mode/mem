import { Children, isValidElement, type ReactNode } from "react"

export function getDocumentationText(value: ReactNode): string {
  if (typeof value === "string" || typeof value === "number") {
    return String(value)
  }

  if (Array.isArray(value)) {
    return value.map(getDocumentationText).join("")
  }

  if (isValidElement<{ children?: ReactNode }>(value)) {
    return getDocumentationText(value.props.children)
  }

  return ""
}

export function toDocumentationHeadingId(value: ReactNode | string): string {
  const text = typeof value === "string" ? value : getDocumentationText(value)
  const normalized = text
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9\s-]/g, "")
    .replace(/\s+/g, "-")
    .replace(/-+/g, "-")
    .replace(/^-|-$/g, "")

  return normalized || "section"
}

export function getReactChildren(value: ReactNode): ReactNode[] {
  return Children.toArray(value)
}
