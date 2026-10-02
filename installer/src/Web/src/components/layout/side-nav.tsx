import { useState } from "react";
import { Link, useLocation } from "react-router-dom";
import {
  ArchiveRestore,
  BadgeCheck,
  BookOpenText,
  CheckCircle2,
  ChevronDown,
  Circle,
  Globe2,
  HardDrive,
  Home,
  Network,
  PackageOpen,
  RefreshCw,
  Server,
  ServerCog,
  Settings,
  UsersRound,
  Stethoscope,
  X,
} from "lucide-react";

import { useI18n } from "@/app/i18n/i18n-context";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { useOptionalOperatorSession } from "@/features/auth/operator-session-context";
import { RuntimeContextSystemInformation } from "@/features/runtime-context/components/runtime-context-system-information";
import { usePlatformStatus } from "@/features/setup/start/hooks/use-platform-status";
import {
  buildSetupDocumentationHref,
  getSetupDocumentationReturnTo,
} from "@/features/operator/docs/documentation-route-scope";

type NavMode = "setup" | "operator";

type NavItem = {
  to: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  isActive: (pathname: string) => boolean;
  disabled?: boolean;
  statusLabel?: string;
};

type SetupStep = {
  key: string;
  to: string;
  label: string;
  isActive: (pathname: string) => boolean;
  isComplete: (pathname: string) => boolean;
  disabled?: boolean;
};

function isPath(pathname: string, root: string) {
  return pathname === root || pathname.startsWith(`${root}/`);
}

function isSetupRootPath(pathname: string) {
  return pathname === "/setup";
}

function isSetupStartPath(pathname: string) {
  return isPath(pathname, "/setup/start") || isPath(pathname, "/start");
}

function isSetupCheckServerPath(pathname: string) {
  return (
    isPath(pathname, "/setup/check-server") || isPath(pathname, "/preflight")
  );
}

function isSetupDomainPath(pathname: string) {
  return (
    isPath(pathname, "/setup/domain") ||
    isPath(pathname, "/setup/domains") ||
    isPath(pathname, "/domains-certificates") ||
    /^\/setup\/[^/]+\/ingress-tls\/?$/.test(pathname)
  );
}

function isSetupReviewPath(pathname: string) {
  return (
    isPath(pathname, "/setup/review") ||
    /^\/setup\/[^/]+\/review\/?$/.test(pathname)
  );
}

function isSetupInstallPath(pathname: string) {
  return (
    isPath(pathname, "/setup/install") ||
    isPath(pathname, "/install") ||
    isPath(pathname, "/activity")
  );
}

function isSetupVerifyPath(pathname: string) {
  return (
    isPath(pathname, "/setup/verify") ||
    /^\/install\/[^/]+\/verify\/?$/.test(pathname)
  );
}

function isSetupHandoffPath(pathname: string) {
  return (
    isPath(pathname, "/setup/handoff") ||
    /^\/install\/[^/]+\/handoff\/?$/.test(pathname)
  );
}

function isSetupDocumentationPath(pathname: string) {
  return isPath(pathname, "/setup/docs");
}

function isAnySetupPath(pathname: string) {
  return (
    isSetupRootPath(pathname) ||
    isSetupStartPath(pathname) ||
    isSetupCheckServerPath(pathname) ||
    isSetupDomainPath(pathname) ||
    isSetupReviewPath(pathname) ||
    isSetupInstallPath(pathname) ||
    isSetupVerifyPath(pathname) ||
    isSetupHandoffPath(pathname) ||
    isSetupDocumentationPath(pathname)
  );
}

function getNavMode(pathname: string): NavMode {
  return isAnySetupPath(pathname) ? "setup" : "operator";
}

function getSetupStage(pathname: string) {
  if (isSetupStartPath(pathname)) return 0;
  if (isSetupCheckServerPath(pathname)) return 1;
  if (isSetupDomainPath(pathname)) return 2;
  if (isSetupReviewPath(pathname)) return 3;
  if (isSetupInstallPath(pathname)) return 4;
  if (isSetupVerifyPath(pathname)) return 5;
  if (isSetupHandoffPath(pathname)) return 6;

  return 0;
}

function getSetupInstallationId(pathname: string) {
  const canonical = pathname.match(
    /^\/setup\/(?:install|verify|handoff)\/([^/]+)(?:\/|$)/,
  );
  if (canonical) {
    return canonical[1];
  }

  const legacy = pathname.match(/^\/install\/([^/]+)\/(?:verify|handoff)(?:\/|$)/);
  return legacy?.[1];
}

function NavLinkItem({
  item,
  pathname,
  onClick,
}: {
  item: NavItem;
  pathname: string;
  onClick?: () => void;
}) {
  const active = item.isActive(pathname);

  if (item.disabled) {
    return (
      <div
        aria-disabled="true"
        className="flex items-center gap-3 rounded-xl border border-transparent px-3 py-2.5 text-sm text-muted-foreground/60"
      >
        <item.icon className="h-4 w-4 shrink-0" />
        <span className="min-w-0 flex-1">{item.label}</span>
        {item.statusLabel ? (
          <span className="shrink-0 rounded-full border border-border px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
            {item.statusLabel}
          </span>
        ) : null}
      </div>
    );
  }

  return (
    <Link
      to={item.to}
      onClick={onClick}
      aria-current={active ? "page" : undefined}
      className={cn(
        "flex cursor-pointer items-center gap-3 rounded-xl border px-3 py-2.5 text-sm transition",
        active
          ? "border-primary/30 bg-primary text-primary-foreground shadow-sm"
          : "border-transparent text-muted-foreground hover:border-border hover:bg-card hover:text-foreground",
      )}
    >
      <item.icon className="h-4 w-4 shrink-0" />
      <span>{item.label}</span>
    </Link>
  );
}

function SetupStepItem({
  step,
  index,
  currentStage,
  pathname,
  progressPath,
  setupCompleted,
  onClick,
}: {
  step: SetupStep;
  index: number;
  currentStage: number;
  pathname: string;
  progressPath: string;
  setupCompleted: boolean;
  onClick?: () => void;
}) {
  const active = step.isActive(pathname);
  const complete =
    setupCompleted || step.isComplete(progressPath) || index < currentStage;
  const Icon = complete ? CheckCircle2 : Circle;

  if (step.disabled) {
    return (
      <div
        data-setup-complete={complete ? "true" : "false"}
        className="flex items-center gap-3 rounded-xl border border-transparent px-3 py-2.5 text-sm text-muted-foreground/50"
      >
        <Icon
          className={cn(
            "h-4 w-4 shrink-0",
            complete ? "text-primary" : "text-muted-foreground/50",
          )}
        />
        <span>{step.label}</span>
      </div>
    );
  }

  return (
    <Link
      to={step.to}
      onClick={onClick}
      data-setup-complete={complete ? "true" : "false"}
      className={cn(
        "flex cursor-pointer items-center gap-3 rounded-xl border px-3 py-2.5 text-sm transition",
        active
          ? "border-primary/30 bg-primary/10 text-foreground"
          : "border-transparent text-muted-foreground hover:border-border hover:bg-card hover:text-foreground",
      )}
    >
      <Icon
        className={cn(
          "h-4 w-4 shrink-0",
          complete
            ? "text-primary"
            : active
              ? "fill-primary/20 text-primary"
              : "text-muted-foreground",
        )}
      />
      <span>{step.label}</span>
    </Link>
  );
}

function SetupNavContent({
  pathname,
  search,
  onNavigate,
}: {
  pathname: string;
  search: string;
  onNavigate?: () => void;
}) {
  const { t } = useI18n();
  const platformStatus = usePlatformStatus();
  const setupDocsReturnTo = getSetupDocumentationReturnTo(pathname, search);
  const progressPath = setupDocsReturnTo
    ? setupDocsReturnTo.split(/[?#]/, 1)[0]
    : pathname;
  const stage = getSetupStage(progressPath);
  const installationId = getSetupInstallationId(progressPath);
  const setupCompleted = platformStatus.data?.startupTarget === "dashboard";

  const installTo = installationId
    ? `/setup/install/${installationId}`
    : "/setup/install";

  const verifyTo = installationId
    ? `/setup/verify/${installationId}`
    : installTo;

  const handoffTo = installationId
    ? `/setup/handoff/${installationId}`
    : installTo;

  const setupPreview =
    new URLSearchParams(search).get("setupPreview") === "1";
  const documentationItem: NavItem = {
    to: isSetupDocumentationPath(pathname)
      ? `${pathname}${search}`
      : buildSetupDocumentationHref(
          `${pathname}${search}`,
          setupPreview,
        ),
    label: t("navigation.documentation"),
    icon: BookOpenText,
    isActive: isSetupDocumentationPath,
  };

  const steps: SetupStep[] = [
    {
      key: "start",
      to: "/setup/start",
      label: t("navigation.setup.start"),
      isActive: isSetupStartPath,
      isComplete: (value) => getSetupStage(value) > 0,
    },
    {
      key: "check-server",
      to: "/setup/check-server",
      label: t("navigation.setup.checkServer"),
      isActive: isSetupCheckServerPath,
      isComplete: (value) => getSetupStage(value) > 1,
    },
    {
      key: "domain",
      to: "/setup/domain",
      label: t("navigation.setup.domain"),
      isActive: isSetupDomainPath,
      isComplete: (value) => getSetupStage(value) > 2,
    },
    {
      key: "review",
      to: "/setup/review",
      label: t("navigation.setup.review"),
      isActive: isSetupReviewPath,
      isComplete: (value) => getSetupStage(value) > 3,
    },
    {
      key: "install",
      to: installTo,
      label: t("navigation.setup.installMem"),
      isActive: isSetupInstallPath,
      isComplete: (value) => getSetupStage(value) > 4,
    },
    {
      key: "verify",
      to: verifyTo,
      label: t("navigation.setup.verify"),
      isActive: isSetupVerifyPath,
      isComplete: (value) => getSetupStage(value) > 5,
      disabled: !installationId && !isSetupVerifyPath(pathname),
    },
    {
      key: "finish",
      to: handoffTo,
      label: t("navigation.setup.finish"),
      isActive: isSetupHandoffPath,
      isComplete: () => false,
      disabled: !installationId && !isSetupHandoffPath(pathname),
    },
  ];

  return (
    <div className="space-y-5">
      <div>
        <div className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          {t("navigation.setupSection")}
        </div>
        <div className="mt-1 text-sm text-muted-foreground">
          {t("navigation.setupSectionDescription")}
        </div>
      </div>

      <nav className="space-y-1">
        {steps.map((step, index) => (
          <SetupStepItem
            key={step.key}
            step={step}
            index={index}
            currentStage={stage}
            pathname={pathname}
            progressPath={progressPath}
            setupCompleted={setupCompleted}
            onClick={onNavigate}
          />
        ))}

        <div className="mt-4 border-t border-border pt-4">
          <NavLinkItem
            item={documentationItem}
            pathname={pathname}
            onClick={onNavigate}
          />
        </div>
      </nav>
    </div>
  );
}

function DomainsNavGroup({
  pathname,
  onNavigate,
}: {
  pathname: string;
  onNavigate?: () => void;
}) {
  const { t } = useI18n();
  const isFeatureActive = isPath(pathname, "/domains");
  const [isOpen, setIsOpen] = useState(isFeatureActive);

  const isGlobalCertificatesPath = (value: string) =>
    isPath(value, "/domains/certificates");
  const isDomainCertificatesPath = (value: string) =>
    /^\/domains\/[^/]+\/certificates(?:\/|$)/.test(value);
  const isGlobalRenewalPath = (value: string) =>
    isPath(value, "/domains/renewal");
  const isDomainRenewalPath = (value: string) =>
    /^\/domains\/[^/]+\/renewal(?:\/|$)/.test(value);
  const isDomainRegistryPath = (value: string) =>
    isPath(value, "/domains") &&
    !isGlobalCertificatesPath(value) &&
    !isDomainCertificatesPath(value) &&
    !isGlobalRenewalPath(value) &&
    !isDomainRenewalPath(value) &&
    !isPath(value, "/domains/certificate-authorities");

  const children: NavItem[] = [
    {
      to: "/domains",
      label: t("navigation.domainRegistry"),
      icon: Globe2,
      isActive: isDomainRegistryPath,
    },
    {
      to: "/domains/certificates",
      label: t("navigation.certificates"),
      icon: BadgeCheck,
      isActive: (value) =>
        isGlobalCertificatesPath(value) || isDomainCertificatesPath(value),
    },
    {
      to: "/domains/renewal",
      label: t("navigation.renewal"),
      icon: RefreshCw,
      isActive: (value) =>
        isGlobalRenewalPath(value) || isDomainRenewalPath(value),
    },
  ];

  return (
    <div className="space-y-1">
      <div
        className={cn(
          "flex items-center rounded-xl border transition",
          isFeatureActive
            ? "border-primary/30 bg-primary/10 text-foreground"
            : "border-transparent text-muted-foreground hover:border-border hover:bg-card hover:text-foreground",
        )}
      >
        <Link
          to="/domains"
          onClick={onNavigate}
          className="flex min-w-0 flex-1 cursor-pointer items-center gap-3 px-3 py-2.5 text-sm"
        >
          <Network className="h-4 w-4 shrink-0" />
          <span>{t("navigation.domains")}</span>
        </Link>
        <button
          type="button"
          aria-label={isOpen ? t("navigation.collapseDomains") : t("navigation.expandDomains")}
          aria-expanded={isOpen}
          className="mr-1 cursor-pointer rounded-lg p-2 hover:bg-background/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          onClick={() => setIsOpen((current) => !current)}
        >
          <ChevronDown
            className={cn("h-4 w-4 transition-transform", !isOpen && "-rotate-90")}
          />
        </button>
      </div>

      {isOpen ? (
        <div className="ml-5 space-y-1 border-l border-border pl-2">
          {children.map((item) => (
            <NavLinkItem
              key={item.to}
              item={item}
              pathname={pathname}
              onClick={onNavigate}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function BackupRestoreNavGroup({
  pathname,
  onNavigate,
}: {
  pathname: string;
  onNavigate?: () => void;
}) {
  const { t } = useI18n();
  const isFeatureActive =
    isPath(pathname, "/backups") ||
    isPath(pathname, "/restores") ||
    isPath(pathname, "/backup-restore");
  const [isOpen, setIsOpen] = useState(isFeatureActive);

  const children: NavItem[] = [
    {
      to: "/backups",
      label: t("navigation.backups"),
      icon: ArchiveRestore,
      isActive: (value) => isPath(value, "/backups") && !isPath(value, "/backups/import"),
    },
    {
      to: "/restores",
      label: t("navigation.restores"),
      icon: ArchiveRestore,
      isActive: (value) => isPath(value, "/restores"),
    },
    {
      to: "/backups/import",
      label: t("navigation.importZip"),
      icon: ArchiveRestore,
      isActive: (value) => isPath(value, "/backups/import"),
    },
  ];

  return (
    <div className="space-y-1">
      <div
        className={cn(
          "flex items-center rounded-xl border transition",
          isFeatureActive
            ? "border-primary/30 bg-primary/10 text-foreground"
            : "border-transparent text-muted-foreground hover:border-border hover:bg-card hover:text-foreground",
        )}
      >
        <Link
          to="/backups"
          onClick={onNavigate}
          className="flex min-w-0 flex-1 cursor-pointer items-center gap-3 px-3 py-2.5 text-sm"
        >
          <ArchiveRestore className="h-4 w-4 shrink-0" />
          <span>{t("navigation.backupRestore")}</span>
        </Link>
        <button
          type="button"
          aria-label={isOpen ? t("navigation.collapseBackupRestore") : t("navigation.expandBackupRestore")}
          aria-expanded={isOpen}
          className="mr-1 cursor-pointer rounded-lg p-2 hover:bg-background/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          onClick={() => setIsOpen((current) => !current)}
        >
          <ChevronDown
            className={cn("h-4 w-4 transition-transform", !isOpen && "-rotate-90")}
          />
        </button>
      </div>

      {isOpen ? (
        <div className="ml-5 space-y-1 border-l border-border pl-2">
          {children.map((item) => (
            <NavLinkItem
              key={item.to}
              item={item}
              pathname={pathname}
              onClick={onNavigate}
            />
          ))}
        </div>
      ) : null}
    </div>
  );
}

function OperatorNavContent({
  pathname,
  onNavigate,
}: {
  pathname: string;
  onNavigate?: () => void;
}) {
  const { t } = useI18n();
  const operatorSession = useOptionalOperatorSession();
  const isPlatformOwner = operatorSession?.session.roles.includes("platform_owner") ?? false;

  const primaryItemsBeforeDomains: NavItem[] = [
    {
      to: "/dashboard",
      label: t("navigation.home"),
      icon: Home,
      isActive: (value) => value === "/" || isPath(value, "/dashboard"),
    },
    {
      to: "/stacks",
      label: t("navigation.chatServers"),
      icon: Server,
      isActive: (value) => isPath(value, "/stacks"),
    },
  ];

  const primaryItemsAfterDomains: NavItem[] = [
    {
      to: "/services",
      label: t("navigation.services"),
      icon: ServerCog,
      isActive: (value) => isPath(value, "/services"),
    },
    {
      to: "/storage",
      label: t("navigation.storage"),
      icon: HardDrive,
      isActive: (value) => isPath(value, "/storage"),
    },
    {
      to: "/diagnostics",
      label: t("navigation.diagnostics"),
      icon: Stethoscope,
      isActive: (value) => isPath(value, "/diagnostics"),
    },
  ];

  const migrationItem: NavItem = {
    to: "/migrations",
    label: t("navigation.migrations"),
    icon: PackageOpen,
    isActive: (value) => isPath(value, "/migrations"),
  };

  const utilityItems: NavItem[] = [
    ...(isPlatformOwner
      ? [
          {
            to: "/security/operators",
            label: t("navigation.operatorAccess"),
            icon: UsersRound,
            isActive: (value: string) => isPath(value, "/security/operators"),
          },
        ]
      : []),
    ...(isPlatformOwner
      ? [
          {
            to: "/settings",
            label: t("navigation.settings"),
            icon: Settings,
            isActive: (value: string) => isPath(value, "/settings"),
          },
        ]
      : []),
  ];

  const documentationItem: NavItem = {
    to: "/docs",
    label: t("navigation.documentation"),
    icon: BookOpenText,
    isActive: (value) => isPath(value, "/docs"),
  };

  return (
    <div>
      <nav className="space-y-1">
        {primaryItemsBeforeDomains.map((item) => (
          <NavLinkItem
            key={item.to}
            item={item}
            pathname={pathname}
            onClick={onNavigate}
          />
        ))}

        <DomainsNavGroup key={`domains-${pathname}`} pathname={pathname} onNavigate={onNavigate} />

        {primaryItemsAfterDomains.map((item) => (
          <NavLinkItem
            key={item.to}
            item={item}
            pathname={pathname}
            onClick={onNavigate}
          />
        ))}

        <BackupRestoreNavGroup key={`backup-restore-${pathname}`} pathname={pathname} onNavigate={onNavigate} />

        <NavLinkItem
          item={migrationItem}
          pathname={pathname}
          onClick={onNavigate}
        />

        {utilityItems.map((item) => (
          <NavLinkItem
            key={item.to}
            item={item}
            pathname={pathname}
            onClick={onNavigate}
          />
        ))}

        <div className="mt-4 border-t border-border pt-4">
          <NavLinkItem
            item={documentationItem}
            pathname={pathname}
            onClick={onNavigate}
          />
        </div>
      </nav>
    </div>
  );
}

function SideNavContent({
  pathname,
  search,
  onNavigate,
}: {
  pathname: string;
  search: string;
  onNavigate?: () => void;
}) {
  const mode = getNavMode(pathname);

  return mode === "setup" ? (
    <SetupNavContent
      pathname={pathname}
      search={search}
      onNavigate={onNavigate}
    />
  ) : (
    <OperatorNavContent pathname={pathname} onNavigate={onNavigate} />
  );
}

export function SideNav({
  mobileOpen,
  onMobileOpenChange,
}: {
  mobileOpen?: boolean
  onMobileOpenChange?: (open: boolean) => void
} = {}) {
  const { t } = useI18n();
  const location = useLocation();
  const operatorSession = useOptionalOperatorSession();
  const [uncontrolledMobileOpen, setUncontrolledMobileOpen] = useState(false);
  const isMobileOpen = mobileOpen ?? uncontrolledMobileOpen;
  const setMobileOpen = onMobileOpenChange ?? setUncontrolledMobileOpen;
  const showSystemInformation = Boolean(operatorSession) && getNavMode(location.pathname) === "operator";

  return (
    <>
      <div aria-hidden="true" className="hidden w-72 shrink-0 lg:block" />
      <aside
        aria-label={t("navigation.menu")}
        className="fixed bottom-0 left-0 top-16 z-30 hidden w-72 flex-col border-r border-border bg-sidebar/95 lg:flex dark:bg-background/60"
      >
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-6">
          <SideNavContent pathname={location.pathname} search={location.search} />
        </div>
        {showSystemInformation ? (
          <RuntimeContextSystemInformation className="mx-4 mb-4 shrink-0" />
        ) : null}
      </aside>

      {isMobileOpen ? (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            aria-hidden="true"
            className="absolute inset-0 bg-background/80 backdrop-blur-sm"
            onClick={() => setMobileOpen(false)}
          />

          <div className="relative flex h-full w-80 max-w-[85vw] flex-col border-r border-border bg-sidebar p-4 shadow-xl dark:bg-background">
            <div className="mb-4 flex shrink-0 items-center justify-between">
              <div className="text-sm font-semibold text-foreground">{t("header.operatorTitle")}</div>
              <Button
                type="button"
                variant="ghost"
                size="icon-sm"
                aria-label={t("navigation.closeMenu")}
                title={t("navigation.closeMenu")}
                className="text-muted-foreground hover:bg-muted hover:text-foreground"
                onClick={() => setMobileOpen(false)}
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>

            <div className="min-h-0 flex-1 overflow-y-auto">
              <SideNavContent
                pathname={location.pathname}
                search={location.search}
                onNavigate={() => setMobileOpen(false)}
              />
            </div>
            {showSystemInformation ? (
              <RuntimeContextSystemInformation className="mt-4 shrink-0" />
            ) : null}
          </div>
        </div>
      ) : null}
    </>
  );
}
