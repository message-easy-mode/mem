import { type RouteObject } from "react-router-dom";

import { BackupRestorePage } from "./pages/backups";
import { BackupRestoreImportPage } from "./pages/backups/import";
import { BackupCatalogEntryRouteBoundary } from "./pages/backups/catalog";
import { UploadedZipSourceDetailPage } from "./pages/backups/uploads/[validationId]";
import { BackupRestoreSessionsPage } from "./pages/restores";
import { BackupRestoreWorkspaceRouteBoundary } from "./pages/restores/[restoreSessionId]";

/**
 * Add these as top-level children of the application's existing router.
 * Folder names communicate ownership; React Router paths remain explicit.
 */
export const backupRoutes: RouteObject[] = [
  { path: "/backups", element: <BackupRestorePage /> },
  { path: "/backups/import", element: <BackupRestoreImportPage /> },
  {
    path: "/backups/catalog/:catalogEntryId",
    element: <BackupCatalogEntryRouteBoundary />,
  },
  {
    path: "/backups/uploads/:validationId",
    element: <UploadedZipSourceDetailPage />,
  },
  { path: "/restores", element: <BackupRestoreSessionsPage /> },
  {
    path: "/restores/:restoreSessionId",
    element: <BackupRestoreWorkspaceRouteBoundary />,
  },
];
