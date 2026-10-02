import { useLocation } from "react-router-dom"

const OPERATOR_DOCUMENTATION_ROOT = "/docs"
const SETUP_DOCUMENTATION_ROOT = "/setup/docs"

const setupDocumentationSearchKeys = ["returnTo", "setupPreview"] as const

function isSetupDocumentationPath(pathname: string) {
  return (
    pathname === SETUP_DOCUMENTATION_ROOT ||
    pathname.startsWith(`${SETUP_DOCUMENTATION_ROOT}/`)
  )
}

function normalizeSetupReturnTo(value: string | null) {
  if (!value) {
    return "/setup/start"
  }

  try {
    const parsed = new URL(value, "http://mem.local")

    if (
      parsed.origin !== "http://mem.local" ||
      !parsed.pathname.startsWith("/setup/") ||
      isSetupDocumentationPath(parsed.pathname)
    ) {
      return "/setup/start"
    }

    return `${parsed.pathname}${parsed.search}${parsed.hash}`
  } catch {
    return "/setup/start"
  }
}

function getSetupDocumentationSearch(search: string) {
  const source = new URLSearchParams(search)
  const target = new URLSearchParams()

  for (const key of setupDocumentationSearchKeys) {
    const value = source.get(key)

    if (value) {
      target.set(key, value)
    }
  }

  const serialized = target.toString()
  return serialized ? `?${serialized}` : ""
}

function splitHref(href: string) {
  const hashIndex = href.indexOf("#")
  const hash = hashIndex >= 0 ? href.slice(hashIndex) : ""
  const withoutHash = hashIndex >= 0 ? href.slice(0, hashIndex) : href
  const queryIndex = withoutHash.indexOf("?")

  return {
    path: queryIndex >= 0 ? withoutHash.slice(0, queryIndex) : withoutHash,
    search: queryIndex >= 0 ? withoutHash.slice(queryIndex + 1) : "",
    hash,
  }
}

function mergeSetupDocumentationSearch(
  preservedSearch: string,
  targetSearch: string,
) {
  const merged = new URLSearchParams(preservedSearch.replace(/^\?/, ""))

  for (const [key, value] of new URLSearchParams(targetSearch)) {
    merged.set(key, value)
  }

  const serialized = merged.toString()
  return serialized ? `?${serialized}` : ""
}

export function buildSetupDocumentationHref(
  returnTo: string,
  setupPreview = false,
) {
  const search = new URLSearchParams()
  search.set("returnTo", normalizeSetupReturnTo(returnTo))

  if (setupPreview) {
    search.set("setupPreview", "1")
  }

  return `${SETUP_DOCUMENTATION_ROOT}?${search.toString()}`
}

export function getSetupDocumentationReturnTo(
  pathname: string,
  search: string,
) {
  if (!isSetupDocumentationPath(pathname)) {
    return undefined
  }

  return normalizeSetupReturnTo(new URLSearchParams(search).get("returnTo"))
}

export function useDocumentationRouteScope() {
  const location = useLocation()
  const setupMode = isSetupDocumentationPath(location.pathname)
  const rootPath = setupMode
    ? SETUP_DOCUMENTATION_ROOT
    : OPERATOR_DOCUMENTATION_ROOT
  const preservedSearch = setupMode
    ? getSetupDocumentationSearch(location.search)
    : ""
  const returnTo = setupMode
    ? normalizeSetupReturnTo(new URLSearchParams(location.search).get("returnTo"))
    : undefined

  function rootHref() {
    return `${rootPath}${preservedSearch}`
  }

  function documentHref(documentKey: string) {
    return `${rootPath}/${documentKey}${preservedSearch}`
  }

  function rebaseHref(href: string) {
    if (!setupMode || !href.startsWith(OPERATOR_DOCUMENTATION_ROOT)) {
      return href
    }

    const { path, search, hash } = splitHref(href)
    const suffix = path.slice(OPERATOR_DOCUMENTATION_ROOT.length)
    const mergedSearch = mergeSetupDocumentationSearch(
      preservedSearch,
      search,
    )

    return `${SETUP_DOCUMENTATION_ROOT}${suffix}${mergedSearch}${hash}`
  }

  return {
    setupMode,
    rootPath,
    returnTo,
    preservedSearch,
    rootHref,
    documentHref,
    rebaseHref,
  } as const
}
