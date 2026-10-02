import { isValidElement, useState, type ReactNode } from "react"
import { Check, Copy, ExternalLink, Info, Lightbulb, ShieldAlert, TriangleAlert } from "lucide-react"
import ReactMarkdown from "react-markdown"
import { Link } from "react-router-dom"
import remarkGfm from "remark-gfm"

import { useI18n } from "@/app/i18n/i18n-context"
import {
  getDocumentationText,
  getReactChildren,
  toDocumentationHeadingId,
} from "@/features/operator/docs/docs-helpers"
import type { DocumentationHeading } from "@/features/operator/docs/docs.types"
import { cn } from "@/lib/utils"

type DocumentationMarkdownProps = Readonly<{
  markdown: string
  headings?: readonly DocumentationHeading[]
  resolveLocalLink?: (href: string) => string | undefined
  resolveLocalImage?: (source: string) => string | undefined
}>

type CalloutKind = "info" | "tip" | "important" | "warning" | "caution"

const calloutKinds: Readonly<Record<string, CalloutKind>> = {
  INFO: "info",
  NOTE: "info",
  TIP: "tip",
  IMPORTANT: "important",
  WARNING: "warning",
  CAUTION: "caution",
}

const calloutStyles: Readonly<
  Record<CalloutKind, { className: string; Icon: typeof Info }>
> = {
  info: {
    className: "border-primary/35 bg-primary/8 text-foreground",
    Icon: Info,
  },
  tip: {
    className: "border-success/35 bg-success/8 text-foreground",
    Icon: Lightbulb,
  },
  important: {
    className: "border-primary/35 bg-primary/10 text-foreground",
    Icon: ShieldAlert,
  },
  warning: {
    className: "border-amber-500/40 bg-amber-500/10 text-foreground",
    Icon: TriangleAlert,
  },
  caution: {
    className: "border-destructive/40 bg-destructive/10 text-foreground",
    Icon: ShieldAlert,
  },
}

function getCallout(value: ReactNode) {
  const text = getDocumentationText(value).trim()
  const match = text.match(/^\[!(INFO|NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]\s*([\s\S]*)$/)

  if (!match) {
    return undefined
  }

  return {
    kind: calloutKinds[match[1]],
    text: match[2].trim(),
  }
}

function DocumentationCallout({ children }: { children?: ReactNode }) {
  const callout = getCallout(children)

  if (!callout) {
    return (
      <blockquote className="my-5 border-l-4 border-border pl-4 text-muted-foreground">
        {children}
      </blockquote>
    )
  }

  const { Icon, className } = calloutStyles[callout.kind]

  return (
    <aside
      role="note"
      className={cn("my-5 flex gap-3 rounded-xl border p-4 text-sm leading-6", className)}
    >
      <Icon className="mt-0.5 h-5 w-5 shrink-0" aria-hidden="true" />
      <div>{callout.text}</div>
    </aside>
  )
}

function isUnsafeHref(href: string) {
  return /^(?:javascript|vbscript|data):/i.test(href.trim())
}

function DocumentationLink({
  href,
  children,
  resolveLocalLink,
}: {
  href?: string
  children?: ReactNode
  resolveLocalLink?: (href: string) => string | undefined
}) {
  const { t } = useI18n()

  if (!href || isUnsafeHref(href)) {
    return <span>{children}</span>
  }

  const resolvedLocalHref = resolveLocalLink?.(href)

  if (resolvedLocalHref) {
    return (
      <Link
        to={resolvedLocalHref}
        className="font-medium text-primary underline decoration-primary/35 underline-offset-4 hover:decoration-primary"
      >
        {children}
      </Link>
    )
  }

  const isExternal = /^https?:\/\//i.test(href)

  return (
    <a
      href={href}
      className="font-medium text-primary underline decoration-primary/35 underline-offset-4 hover:decoration-primary"
      target={isExternal ? "_blank" : undefined}
      rel={isExternal ? "noopener noreferrer" : undefined}
    >
      {children}
      {isExternal ? <ExternalLink className="ml-1 inline h-3.5 w-3.5" aria-label={t("documentation.externalLink")} /> : null}
    </a>
  )
}

function fallbackCopy(value: string) {
  const textarea = document.createElement("textarea")
  textarea.value = value
  textarea.setAttribute("readonly", "")
  textarea.style.position = "fixed"
  textarea.style.opacity = "0"
  document.body.append(textarea)
  textarea.select()
  document.execCommand("copy")
  textarea.remove()
}

function DocumentationCodeBlock({
  language,
  value,
}: {
  language?: string
  value: string
}) {
  const { t } = useI18n()
  const [copied, setCopied] = useState(false)

  async function copyCode() {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value)
      } else {
        fallbackCopy(value)
      }

      setCopied(true)
      window.setTimeout(() => setCopied(false), 1_600)
    } catch {
      fallbackCopy(value)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1_600)
    }
  }

  return (
    <div className="my-5 overflow-hidden rounded-xl border border-border bg-muted/35">
      <div className="flex items-center justify-between border-b border-border px-3 py-2 text-xs text-muted-foreground">
        <span>{language ?? t("documentation.codeBlock")}</span>
        <button
          type="button"
          onClick={copyCode}
          className="inline-flex items-center gap-1 rounded-md px-2 py-1 font-medium text-foreground transition hover:bg-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          aria-label={copied ? t("documentation.copiedCode") : t("documentation.copyCode")}
        >
          {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
          {copied ? t("documentation.copiedCode") : t("documentation.copyCode")}
        </button>
      </div>
      <pre className="overflow-x-auto p-4 text-sm leading-6">
        <code>{value}</code>
      </pre>
    </div>
  )
}

function DocumentationCode({ children }: { children?: ReactNode }) {
  return (
    <code className="rounded-md bg-muted px-1.5 py-0.5 font-mono text-[0.92em] text-foreground">
      {children}
    </code>
  )
}

function DocumentationPre({ children }: { children?: ReactNode }) {
  const [code] = getReactChildren(children)

  if (isValidElement<{ className?: string; children?: ReactNode }>(code)) {
    const language = code.props.className?.match(/language-([^\s]+)/)?.[1]
    const value = getDocumentationText(code.props.children).replace(/\n$/, "")

    return <DocumentationCodeBlock language={language} value={value} />
  }

  return <pre className="my-5 overflow-x-auto rounded-xl border border-border p-4">{children}</pre>
}

function createDocumentationHeadingIdResolver(headings: readonly DocumentationHeading[]) {
  const headingIdsByLabel = new Map<string, string[]>()
  const nextIndexByLabel = new Map<string, number>()

  for (const heading of headings) {
    const ids = headingIdsByLabel.get(heading.label) ?? []
    ids.push(heading.id)
    headingIdsByLabel.set(heading.label, ids)
  }

  return (children: ReactNode | undefined) => {
    const label = getDocumentationText(children).trim()
    const ids = headingIdsByLabel.get(label)
    const nextIndex = nextIndexByLabel.get(label) ?? 0
    const id = ids?.[nextIndex]

    if (id) {
      nextIndexByLabel.set(label, nextIndex + 1)
      return id
    }

    return toDocumentationHeadingId(children ?? "")
  }
}

function Heading({
  level,
  children,
  id,
}: {
  level: 2 | 3 | 4
  children?: ReactNode
  id: string
}) {
  const Tag = `h${level}` as const

  return (
    <Tag
      id={id}
      className={cn(
        "scroll-mt-28 font-semibold tracking-tight text-foreground",
        level === 2 && "mt-10 text-2xl",
        level === 3 && "mt-8 text-xl",
        level === 4 && "mt-6 text-lg",
      )}
    >
      <a href={`#${id}`} className="hover:text-primary">
        {children}
      </a>
    </Tag>
  )
}

/**
 * Markdown is rendered without rehype-raw. HTML embedded in a document remains
 * inert text, and no MDX/JSX compiler is present in the runtime bundle.
 */
export function DocumentationMarkdown({
  markdown,
  headings = [],
  resolveLocalLink,
  resolveLocalImage,
}: DocumentationMarkdownProps) {
  const resolveHeadingId = createDocumentationHeadingIdResolver(headings)

  return (
    <article className="min-w-0 text-[0.95rem] leading-7 text-foreground">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          h1: ({ children }) => <Heading level={2} id={resolveHeadingId(children)}>{children}</Heading>,
          h2: ({ children }) => <Heading level={2} id={resolveHeadingId(children)}>{children}</Heading>,
          h3: ({ children }) => <Heading level={3} id={resolveHeadingId(children)}>{children}</Heading>,
          h4: ({ children }) => <Heading level={4} id={resolveHeadingId(children)}>{children}</Heading>,
          p: ({ children }) => <p className="my-4 text-foreground/90">{children}</p>,
          ul: ({ children }) => <ul className="my-4 list-disc space-y-2 pl-6">{children}</ul>,
          ol: ({ children }) => <ol className="my-4 list-decimal space-y-2 pl-6">{children}</ol>,
          li: ({ children }) => <li className="pl-1">{children}</li>,
          blockquote: ({ children }) => <DocumentationCallout>{children}</DocumentationCallout>,
          a: ({ href, children }) => (
            <DocumentationLink href={href} resolveLocalLink={resolveLocalLink}>
              {children}
            </DocumentationLink>
          ),
          img: ({ src, alt }) => {
            const resolvedSource = src ? resolveLocalImage?.(src) : undefined

            if (!resolvedSource) {
              return (
                <span className="my-5 block rounded-xl border border-border bg-muted/30 p-4 text-sm text-muted-foreground">
                  {alt ?? "Documentation image unavailable"}
                </span>
              )
            }

            return (
              <img
                src={resolvedSource}
                alt={alt ?? ""}
                loading="lazy"
                decoding="async"
                className="my-6 h-auto max-w-full rounded-xl border border-border bg-muted/20 shadow-sm"
              />
            )
          },
          pre: ({ children }) => <DocumentationPre>{children}</DocumentationPre>,
          code: ({ children }) => <DocumentationCode>{children}</DocumentationCode>,
          table: ({ children }) => (
            <div className="my-5 overflow-x-auto rounded-xl border border-border">
              <table className="w-full border-collapse text-left text-sm">{children}</table>
            </div>
          ),
          thead: ({ children }) => <thead className="bg-muted/55">{children}</thead>,
          th: ({ children }) => <th className="border-b border-border px-3 py-2.5 font-semibold">{children}</th>,
          td: ({ children }) => <td className="border-b border-border px-3 py-2.5 align-top last:border-b-0">{children}</td>,
          hr: () => <hr className="my-8 border-border" />,
        }}
      >
        {markdown}
      </ReactMarkdown>
    </article>
  )
}
