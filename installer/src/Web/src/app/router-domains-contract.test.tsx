import type { ReactElement } from "react"
import { describe, expect, it } from "vitest"
import { Navigate } from "react-router-dom"

import { router } from "@/app/router"
import { OperatorDomainCertificatesPage } from "@/features/operator/domains/pages/operator-domain-certificates-page"
import { OperatorDomainCertificateIssuePage } from "@/features/operator/domains/pages/operator-domain-certificate-issue-page"
import { OperatorDomainCertificateListPage } from "@/features/operator/domains/pages/operator-domain-certificate-list-page"
import { OperatorDomainCertificateDetailPage } from "@/features/operator/domains/pages/operator-domain-certificate-detail-page"

function authenticatedRoutes() {
  const rootRoute = router.routes.find((route) => route.path === "/")
  return rootRoute?.children ?? []
}

describe("Domains route contract", () => {
  it("locks the workspace and Domain-owned route namespaces", () => {
    const paths = authenticatedRoutes().map((route) => route.path)

    for (const path of [
      "domains",
      "domains/new",
      "domains/certificates",
      "domains/certificates/new",
      "domains/renewal",
      "domains/:domainId",
      "domains/:domainId/dns",
      "domains/:domainId/certificates",
      "domains/:domainId/certificates/new",
      "domains/:domainId/certificates/:certificateId",
      "domains/:domainId/renewal",
      "domains/:domainId/usage",
      "domains/:domainId/history",
      "domains/:domainId/settings",
    ]) {
      expect(paths).toContain(path)
    }
  })

  it("reserves the future certificate-authorities namespace without enabling a workflow", () => {
    for (const path of [
      "domains/certificate-authorities",
      "domains/certificate-authorities/*",
    ]) {
      const route = authenticatedRoutes().find((candidate) => candidate.path === path)
      const element =
        route && "element" in route
          ? (route.element as ReactElement<{ to: string; replace?: boolean }>)
          : undefined

      expect(route).toBeTruthy()
      expect(element?.type).toBe(Navigate)
      expect(element?.props.to).toBe("/domains")
      expect(element?.props.replace).toBe(true)
    }
  })

  it("binds certificate routes to inventory and Domain-owned resource pages", () => {
    const expected = new Map([
      ["domains/certificates", OperatorDomainCertificatesPage],
      ["domains/certificates/new", OperatorDomainCertificateIssuePage],
      ["domains/:domainId/certificates", OperatorDomainCertificateListPage],
      ["domains/:domainId/certificates/new", OperatorDomainCertificateIssuePage],
      ["domains/:domainId/certificates/:certificateId", OperatorDomainCertificateDetailPage],
    ])

    for (const [path, component] of expected) {
      const route = authenticatedRoutes().find((candidate) => candidate.path === path)
      const element = route && "element" in route ? (route.element as ReactElement) : undefined

      expect(route).toBeTruthy()
      expect(element?.type).toBe(component)
    }
  })
})
