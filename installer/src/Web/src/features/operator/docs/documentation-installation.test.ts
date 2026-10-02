import manifestJson from "./release-pack/metadata/manifest.json?raw"

import { describe, expect, it } from "vitest"

import { getDocumentationView } from "./documentation-release-pack"

const installationDocumentKeys = [
  "installation",
  "installation/ubuntu-server-on-prem",
  "installation/prepare-host",
  "installation/bootstrap-installer",
  "installation/private-access-and-setup-code",
  "installation/check-server",
  "installation/domain-and-certificate",
  "installation/advanced-install",
  "installation/review-and-install",
  "installation/verify-and-handoff",
  "installation/first-platform-owner",
  "installation/troubleshooting",
] as const

type ManifestDocument = Readonly<{
  translationKey: string
  locale: "en" | "de"
  status: string
  appliesTo: readonly string[]
}>

type Manifest = Readonly<{
  sourceContentVersion: string
  documents: readonly ManifestDocument[]
}>

const manifest = JSON.parse(manifestJson) as Manifest

describe("MEM 0.2.0 installation documentation", () => {
  it("ships one reviewed English and German variant for every installation topic", () => {
    const englishView = getDocumentationView("en")
    const germanView = getDocumentationView("de")
    const installationManifestDocuments = manifest.documents.filter(
      (document) =>
        document.translationKey === "installation" ||
        document.translationKey.startsWith("installation/"),
    )

    expect(manifest.sourceContentVersion).toBe("0.2.0")
    expect(installationManifestDocuments).toHaveLength(24)

    for (const key of installationDocumentKeys) {
      expect(englishView.documentByKey.get(key)).toMatchObject({ key, locale: "en" })
      expect(germanView.documentByKey.get(key)).toMatchObject({ key, locale: "de" })

      const variants = installationManifestDocuments.filter(
        (document) => document.translationKey === key,
      )

      expect(variants.map((document) => document.locale).sort()).toEqual(["de", "en"])
      expect(variants.every((document) => document.status === "supported")).toBe(true)
      expect(variants.every((document) => document.appliesTo.includes("0.2.x"))).toBe(true)
    }
  })

  it("documents the current bootstrap and guided setup contracts", () => {
    const view = getDocumentationView("en")
    const germanView = getDocumentationView("de")
    const overview = view.documentByKey.get("installation")!
    const onPrem = view.documentByKey.get("installation/ubuntu-server-on-prem")!
    const prepareHost = view.documentByKey.get("installation/prepare-host")!
    const bootstrap = view.documentByKey.get("installation/bootstrap-installer")!
    const access = view.documentByKey.get("installation/private-access-and-setup-code")!
    const checks = view.documentByKey.get("installation/check-server")!
    const domain = view.documentByKey.get("installation/domain-and-certificate")!
    const advanced = view.documentByKey.get("installation/advanced-install")!
    const install = view.documentByKey.get("installation/review-and-install")!
    const verify = view.documentByKey.get("installation/verify-and-handoff")!
    const firstOwner = view.documentByKey.get("installation/first-platform-owner")!
    const troubleshooting = view.documentByKey.get("installation/troubleshooting")!

    expect(overview.markdown).toContain("MEM Migrate")
    expect(overview.markdown).toContain("mem-api")
    expect(overview.markdown).toContain("mem-web")
    expect(overview.markdown).toContain("mem-control-plane-data")
    expect(overview.markdown).toContain("mem-installer-data")
    expect(overview.markdown).toContain("does not support directly exposing the Control Plane")
    expect(overview.markdown).toContain("SSH tunnelling")

    expect(onPrem.markdown).toContain("ubuntu-lv")
    expect(onPrem.markdown).toContain("/var/lib/message-easy-mode")
    expect(onPrem.markdown).toContain("49160-49200/udp")
    expect(onPrem.markdown).toContain("split DNS")

    expect(prepareHost.markdown).toContain("Ubuntu 24.04")
    expect(prepareHost.markdown).toContain("3,500 MB")
    expect(prepareHost.markdown).toContain("20,000 MB")
    expect(prepareHost.markdown).toContain("libsecret-tools")

    expect(bootstrap.markdown).toContain("sudo ./install.sh --dry-run")
    expect(bootstrap.markdown).toContain("--show-setup-token")
    expect(bootstrap.markdown).toContain("mem-control-plane")
    expect(bootstrap.markdown).toContain("--control-plane-access ssh")
    expect(bootstrap.markdown).toContain("--control-plane-access trusted-lan")
    expect(bootstrap.markdown).toContain("exact Docker host binding")
    expect(bootstrap.markdown).toContain("left stopped rather than being automatically re-exposed")

    expect(access.markdown).toContain("self-signed")
    expect(access.markdown).toContain("SSH tunnel / local-only")
    expect(access.markdown).toContain("Trusted LAN")
    expect(access.markdown).toContain("127.0.0.1:8443")
    expect(access.markdown).toContain("SHA-256 fingerprint")
    expect(access.markdown).toContain("There is no supported direct public Control Plane URL")
    expect(access.markdown).toContain("Wildcard/public drift is a Diagnostics error incident")

    expect(checks.markdown).toContain("legacy MEM 0.1.0 source")
    expect(checks.markdown).toContain("8443")

    expect(domain.markdown).toContain("deSEC")
    expect(domain.markdown).toContain("wildcard")
    expect(domain.markdown).toContain("Nginx Proxy Manager")

    expect(advanced.markdown).toContain("--control-plane-port")
    expect(advanced.markdown).toContain("--control-plane-image mem-control-plane:local")
    expect(advanced.markdown).toContain("--control-plane-bind-address 192.168.10.20")
    expect(advanced.markdown).toContain("0.0.0.0:<port>")
    expect(advanced.markdown).toContain("public MEM administration hostname")
    expect(advanced.markdown).toContain("Unsupported advanced paths")

    expect(install.markdown).toContain("mem-gateway")
    expect(install.markdown).toContain("mem-postgres")
    expect(install.markdown).toContain("mem-npm")
    expect(install.markdown).toContain("WaitingForUser")

    expect(verify.markdown).toContain("Verification report")
    expect(verify.markdown).toContain("control_plane.exposure.unsupported")
    expect(verify.markdown).toContain("Matrix and Element stack creation is a separate operator workflow")
    expect(verify.markdown).toContain("Services → Coturn")
    expect(verify.markdown).toContain("reach **Healthy**")

    const germanAccess = germanView.documentByKey.get("installation/private-access-and-setup-code")!
    const germanVerify = germanView.documentByKey.get("installation/verify-and-handoff")!
    expect(germanAccess.markdown).toContain("SSH-Tunnel / local-only")
    expect(germanAccess.markdown).toContain("Trusted LAN")
    expect(germanAccess.markdown).toContain("SHA-256-Fingerprint")
    expect(germanVerify.markdown).toContain("control_plane.exposure.unsupported")

    expect(firstOwner.markdown).toContain("TOTP")
    expect(firstOwner.markdown).toContain("recovery codes")
    expect(firstOwner.markdown).toContain("Platform Owner")

    expect(troubleshooting.markdown).toContain("sudo docker logs mem-control-plane")
    expect(troubleshooting.markdown).toContain("Legacy Control Plane runtime name detected")
    expect(troubleshooting.markdown).toContain("Open diagnostics")
    expect(troubleshooting.markdown).toContain("Never include passwords")
  })
})
