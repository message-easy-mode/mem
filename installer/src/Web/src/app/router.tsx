import { createBrowserRouter, Navigate, useParams } from "react-router-dom"

import App from "@/App"

import { UnlockPage } from "@/features/auth/pages/unlock-page"
import { LoginPage } from "@/features/auth/pages/login-page"
import { BootstrapPage } from "@/features/auth/pages/bootstrap-page"
import { OperatorEnrollmentPage } from "@/features/auth/pages/operator-enrollment-page"
import { CliDeviceAuthorizationPage } from "@/features/auth/pages/cli-device-authorization-page"
import { AuthGuard } from "@/features/auth/auth-guard"
import { StartupResolverPage } from "@/features/startup/pages/startup-resolver-page"
import { FirstTimeSetupRouteGuard } from "@/features/setup/guards/first-time-setup-route-guard"
import { GlobalRouteErrorPage, RouteNotFoundPage } from "@/app/route-error-page"

import { InstallerStartPage } from "@/features/setup/start/pages/installer-start-page"
import { HostChecksPage } from "@/features/setup/host-checks/pages/host-checks-page"
import { HostCheckRunPage } from "@/features/setup/host-checks/pages/host-checks-run-page"
import { SetupDomainPage } from "@/features/setup/domains/pages/setup-domain-page"
import { SetupReviewPage } from "@/features/setup/review/pages/setup-review-page"
import { SetupInstallPage } from "@/features/setup/install/pages/setup-install-page"
import { InstallRunPage } from "@/features/setup/install-run/pages/install-run-page"
import { VerificationReportPage } from "@/features/setup/verification/pages/verification-report-page"
import { HandoffPage } from "@/features/setup/handoff/pages/handoff-page"
import { SetupTroubleshootingPage } from "@/features/setup/troubleshooting/pages/setup-troubleshooting-page"

import { DashboardPage } from "@/features/operator/dashboard/dashboard-page"
import { DiagnosticsPage } from "@/features/operator/diagnostics/diagnostics-page"
import { DiagnosticsLogsPage } from "@/features/operator/diagnostics/diagnostics-logs-page"
import { DiagnosticsSeqPage } from "@/features/operator/diagnostics/diagnostics-seq-page"
import { DiagnosticsPortainerPage } from "@/features/operator/diagnostics/diagnostics-portainer-page"
import { DiagnosticsRouteErrorPage } from "@/features/operator/diagnostics/components/diagnostics-error-boundary"
import { RuntimeReconciliationPage } from "@/features/operator/maintenance/pages/runtime-reconciliation-page"
import { OperatorDomainsPage } from "@/features/operator/domains/pages/operator-domains-page"
import { OperatorDomainCreatePage } from "@/features/operator/domains/pages/operator-domain-create-page"
import { OperatorDomainDetailPage } from "@/features/operator/domains/pages/operator-domain-detail-page"
import { OperatorDomainCertificatesPage } from "@/features/operator/domains/pages/operator-domain-certificates-page"
import { OperatorDomainCertificateIssuePage } from "@/features/operator/domains/pages/operator-domain-certificate-issue-page"
import { OperatorDomainCertificateListPage } from "@/features/operator/domains/pages/operator-domain-certificate-list-page"
import { OperatorDomainCertificateDetailPage } from "@/features/operator/domains/pages/operator-domain-certificate-detail-page"
import { OperatorDomainRenewalRoutePage } from "@/features/operator/domains/pages/operator-domain-renewal-route-page"
import { StacksListPage } from "@/features/operator/stacks/pages/stacks-list-page"
import { StackCreatePage } from "@/features/operator/stacks/pages/stack-create-page"
import { StackDetailPage } from "@/features/operator/stacks/pages/stack-detail-page"
import { StackServicesPage } from "@/features/operator/stacks/pages/stack-services-page"
import { StackBackupsPage } from "@/features/operator/stacks/pages/stack-backups-page"
import { StackUsersPage } from "@/features/operator/stacks/pages/stack-users-page"
import { StackDiagnosticsPage } from "@/features/operator/stacks/pages/stack-diagnostics-page"
import { StackFederationPage } from "@/features/operator/federation/pages/stack-federation-page"
import { StackSettingsPage } from "@/features/operator/stacks/pages/stack-settings-page"
import { StackWorkspaceRedirectPage } from "@/features/operator/stacks/pages/stack-workspace-redirect-page"
import { StoragePage } from "@/features/operator/storage/pages/storage-page"
import { ServicesPage } from "@/features/operator/services/pages/services-page"
import { PostgresPage } from "@/features/operator/services/pages/postgres-page"
import { NpmPage } from "@/features/operator/services/pages/npm-page"
import { CoturnPage } from "@/features/operator/services/pages/coturn-page"
import { OperatorDirectoryPage } from "@/features/operator/security/pages/operator-directory-page"
import { SecuritySettingsPage } from "@/features/operator/settings/pages/security-settings-page"
import { PrivateNetworkFederationSettingsPage } from "@/features/operator/settings/pages/private-network-federation-settings-page"
import { NpmSettingsPage } from "@/features/operator/settings/pages/npm-settings-page"
import { MigrationIntakesPage } from "@/features/operator/migrations/pages/migration-intakes-page"
import { MigrationsPage } from "@/features/operator/migrations/pages/migrations-page"
import { MigrationSessionPage } from "@/features/operator/migrations/pages/migration-session-page"

import { backupRoutes } from "@/features/operator/backups/routes"
import { documentationRoutes, setupDocumentationRoutes } from "@/features/operator/docs/routes"

export function PreflightRedirect() {
  const { runId } = useParams()

  return (
    <Navigate
      to={runId ? `/setup/check-server/${runId}` : "/setup/check-server"}
      replace
    />
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export const router = createBrowserRouter([
  {
    path: "/unlock",
    element: <UnlockPage />,
  },
  {
    path: "/login",
    element: <LoginPage />,
  },
  {
    path: "/bootstrap",
    element: <BootstrapPage />,
  },
  {
    path: "/enroll",
    element: <OperatorEnrollmentPage />,
  },
  {
    path: "/",
    element: (
      <AuthGuard>
        <App />
      </AuthGuard>
    ),
    errorElement: <GlobalRouteErrorPage />,
    children: [
      { index: true, element: <StartupResolverPage /> },

      // Setup mode. Every first-time setup route passes through one server-owned
      // lifecycle guard so direct URLs, refresh, and browser history cannot reopen
      // actionable setup after installation has completed.
      {
        path: "setup",
        element: <FirstTimeSetupRouteGuard />,
        children: [
          { index: true, element: <Navigate to="/setup/start" replace /> },
          { path: "start", element: <InstallerStartPage /> },
          { path: "check-server", element: <HostChecksPage /> },
          { path: "check-server/:runId", element: <HostCheckRunPage /> },
          { path: "domain", element: <SetupDomainPage /> },
          { path: "review", element: <SetupReviewPage /> },
          { path: "install", element: <SetupInstallPage /> },
          { path: "install/:installationId", element: <InstallRunPage /> },
          { path: "troubleshooting", element: <SetupTroubleshootingPage /> },
          ...setupDocumentationRoutes,

          // Legacy setup paths remain guarded before they redirect.
          {
            path: ":installationId/general",
            element: <Navigate to="/setup/start" replace />,
          },
          {
            path: ":installationId/platform",
            element: <Navigate to="/dashboard" replace />,
          },
          {
            path: ":installationId/ingress-tls",
            element: <Navigate to="/setup/domain" replace />,
          },
          {
            path: ":installationId/optional",
            element: <Navigate to="/dashboard" replace />,
          },
          {
            path: ":installationId/review",
            element: <Navigate to="/setup/review" replace />,
          },
        ],
      },
      { path: "start", element: <Navigate to="/setup/start" replace /> },

      // Verification and handoff are read-only completion evidence. Keep them
      // reachable for the installation that just finished while mutation APIs
      // are independently locked once the Control Plane is established.
      {
        path: "setup/verify/:installationId",
        element: <VerificationReportPage />,
      },
      {
        path: "setup/handoff/:installationId",
        element: <HandoffPage />,
      },

      // Legacy top-level setup redirects enter the guarded setup tree.
      {
        path: "preflight",
        element: <Navigate to="/setup/check-server" replace />,
      },
      { path: "preflight/:runId", element: <PreflightRedirect /> },
      {
        path: "domains-certificates",
        element: <Navigate to="/setup/domain" replace />,
      },

      // Operator mode
      { path: "dashboard", element: <DashboardPage /> },

      { path: "stacks", element: <StacksListPage /> },
      { path: "stacks/new", element: <StackCreatePage /> },
      { path: "stacks/:slugOrId/services", element: <StackServicesPage /> },
      {
        path: "stacks/:slugOrId/storage-media",
        element: <StackWorkspaceRedirectPage target="services" />,
      },
      {
        path: "stacks/:slugOrId/network",
        element: <StackWorkspaceRedirectPage target="services" />,
      },
      {
        path: "stacks/:slugOrId/voice-video",
        element: <StackWorkspaceRedirectPage target="services" />,
      },
      {
        path: "stacks/:slugOrId/backups",
        element: <StackWorkspaceRedirectPage target="recovery" />,
      },
      { path: "stacks/:slugOrId/recovery", element: <StackBackupsPage /> },
      { path: "stacks/:slugOrId/users", element: <StackUsersPage /> },
      { path: "stacks/:slugOrId/federation", element: <StackFederationPage /> },
      { path: "stacks/:slugOrId/diagnostics", element: <StackDiagnosticsPage /> },
      { path: "stacks/:slugOrId/settings", element: <StackSettingsPage /> },
      { path: "stacks/:slugOrId", element: <StackDetailPage /> },

      { path: "domains", element: <OperatorDomainsPage /> },
      { path: "domains/new", element: <OperatorDomainCreatePage /> },
      {
        path: "domains/certificates",
        element: <OperatorDomainCertificatesPage />,
      },
      {
        path: "domains/certificates/new",
        element: <OperatorDomainCertificateIssuePage />,
      },
      {
        path: "domains/renewal",
        element: <OperatorDomainRenewalRoutePage />,
      },
      {
        path: "domains/certificate-authorities",
        element: <Navigate to="/domains" replace />,
      },
      {
        path: "domains/certificate-authorities/*",
        element: <Navigate to="/domains" replace />,
      },
      { path: "domains/:domainId", element: <OperatorDomainDetailPage /> },

      { path: "domains/:domainId/dns", element: <OperatorDomainDetailPage /> },
      {
        path: "domains/:domainId/certificates",
        element: <OperatorDomainCertificateListPage />,
      },
      {
        path: "domains/:domainId/certificates/new",
        element: <OperatorDomainCertificateIssuePage />,
      },
      {
        path: "domains/:domainId/certificates/:certificateId",
        element: <OperatorDomainCertificateDetailPage />,
      },
      {
        path: "domains/:domainId/renewal",
        element: <OperatorDomainRenewalRoutePage />,
      },
      { path: "domains/:domainId/usage", element: <OperatorDomainDetailPage /> },
      { path: "domains/:domainId/history", element: <OperatorDomainDetailPage /> },
      { path: "domains/:domainId/settings", element: <OperatorDomainDetailPage /> },

      { path: "activity/:installationId", element: <InstallRunPage /> },

      { path: "services", element: <ServicesPage /> },
      { path: "services/postgres", element: <PostgresPage /> },
      { path: "services/nginx-proxy-manager", element: <NpmPage /> },
      {
        path: "services/npm",
        element: <Navigate to="/services/nginx-proxy-manager" replace />,
      },
      { path: "services/coturn", element: <CoturnPage /> },
      {
        path: "services/seq",
        element: <Navigate to="/diagnostics/seq" replace />,
      },
      {
        path: "services/portainer",
        element: <Navigate to="/diagnostics/portainer" replace />,
      },
      { path: "services/*", element: <Navigate to="/services" replace /> },

      {
        path: "diagnostics",
        element: <DiagnosticsPage />,
        errorElement: <DiagnosticsRouteErrorPage />,
      },
      {
        path: "diagnostics/logs",
        element: <DiagnosticsLogsPage />,
        errorElement: <DiagnosticsRouteErrorPage />,
      },
      {
        path: "diagnostics/seq",
        element: <DiagnosticsSeqPage />,
        errorElement: <DiagnosticsRouteErrorPage />,
      },
      {
        path: "diagnostics/portainer",
        element: <DiagnosticsPortainerPage />,
        errorElement: <DiagnosticsRouteErrorPage />,
      },
      {
        path: "diagnostics/runtime-reconciliation",
        element: <RuntimeReconciliationPage />,
        errorElement: <DiagnosticsRouteErrorPage />,
      },
      { path: "storage", element: <StoragePage /> },
      { path: "migrations", element: <MigrationsPage /> },
      { path: "migrations/new", element: <MigrationIntakesPage /> },
      { path: "migrations/:migrationId", element: <MigrationSessionPage /> },
      { path: "security/operators", element: <OperatorDirectoryPage /> },
      { path: "settings", element: <Navigate to="/settings/security-access" replace /> },
      { path: "settings/security-access", element: <SecuritySettingsPage /> },
      { path: "settings/network-security", element: <PrivateNetworkFederationSettingsPage /> },
      { path: "settings/nginx-proxy-manager", element: <NpmSettingsPage /> },
      { path: "cli/authorize", element: <CliDeviceAuthorizationPage /> },

      // Documentation owns its reader routes.
      ...documentationRoutes,

      // Backups owns its own current routes and legacy redirects.
      ...backupRoutes,

      // Keep unknown authenticated routes inside the MEM shell instead of
      // exposing React Router's developer-facing default error UI.
      { path: "*", element: <RouteNotFoundPage /> },
    ],
  },
])
