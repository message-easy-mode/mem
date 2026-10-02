namespace Mem.Cli.Tests.TestSupport;

public static class RestoreActionTestPayloads
{
    public const string CatalogEntryId = "bkp_20260702-catalog-action-01";
    public const string RestoreSessionId = "restore-20260702-action-01";

    public const string CreateOrResumeResponse = """
        {
          "source": "control-plane",
          "status": "ok",
          "catalogEntryId": "bkp_20260702-catalog-action-01",
          "restoreSessionId": "restore-20260702-action-01",
          "restoreAttemptCreated": true,
          "restoreAttemptResumed": false,
          "sourceKind": "backup-catalog",
          "payloadState": "available",
          "integrityStatus": "valid",
          "warningCount": 0,
          "detail": "A restore workspace was created for the Backup Catalog entry."
        }
        """;

    public const string PrivateTestResponse = """
        {
          "source": "control-plane",
          "status": "ready",
          "restoreSessionId": "restore-20260702-action-01",
          "operationId": "9d54b2ea-cd43-470e-91e5-1d79ecbd8894",
          "sourceKind": "backup-catalog",
          "catalogEntryId": "bkp_20260702-catalog-action-01",
          "stagingId": "staging-20260702-action-01",
          "privateOnly": true,
          "databaseImportSucceeded": true,
          "synapseHealthPassed": true,
          "requiresExplicitDestroy": true,
          "detail": "Private restore test is ready. It is retained, private-only, and requires explicit destruction when inspection is complete."
        }
        """;

    public const string PreflightReadyResponse = """
        {
          "source": "control-plane",
          "status": "ready",
          "checkedAtUtc": "2026-07-02T14:00:00Z",
          "restoreSessionId": "restore-20260702-action-01",
          "canCreate": true,
          "targets": {
            "targetStackSlug": "action-stack-restored",
            "matrixHost": "matrix-action.example.test",
            "elementHost": "chat-action.example.test"
          },
          "checks": [
            {
              "code": "target-stack-runtime-available",
              "title": "Stack name is available",
              "state": "passed",
              "message": "No active runtime stack uses the target name."
            }
          ],
          "blockers": [],
          "warnings": ["No resources or reservations were created by preflight."],
          "detail": "All catalog-native read-only checks passed."
        }
        """;

    public const string StandardRecreateVerifiedResponse = """
        {
          "source": "control-plane",
          "status": "production_recreate_verified",
          "recreateId": "recreate-20260702-action-01",
          "runtimeStackId": "c521b0f1-cf0d-43d5-b799-4ea6e66b7d62",
          "matrixInstanceId": "c559b013-da0e-4c3f-857b-bae4f8c34d70",
          "elementInstanceId": "066e501f-5b10-4b39-bd04-bb24c9a31231",
          "targetStackSlug": "action-stack-restored",
          "matrixHost": "matrix-action.example.test",
          "elementHost": "chat-action.example.test",
          "startedAtUtc": "2026-07-02T14:01:00Z",
          "finishedAtUtc": "2026-07-02T14:03:00Z",
          "operator": "Nigel",
          "note": "CLI action test",
          "database": {
            "provisioned": true,
            "host": "mem-postgres",
            "port": 5432,
            "databaseName": "internal_database_name",
            "databaseUsername": "internal_database_user",
            "importSucceeded": true,
            "publicTableCount": 173,
            "synapseKnownTableCount": 6,
            "usersCount": 1,
            "eventsCount": 11,
            "roomsCount": 1,
            "stateEventsCount": 8
          },
          "runtime": {
            "matrixContainerName": "mem-matrix-action-stack-restored",
            "matrixContainerId": "internal-container-id",
            "matrixStarted": true,
            "matrixHealthPassed": true,
            "matrixHealthResponse": "ok",
            "elementContainerName": "mem-element-action-stack-restored",
            "elementContainerId": "internal-container-id",
            "elementStarted": true,
            "elementHealthPassed": true,
            "elementHealthResponse": "ok",
            "runtimeNetworkName": "mem-gateway",
            "matrixDataPath": "/var/lib/mem/private/matrix",
            "elementDataPath": "/var/lib/mem/private/element",
            "homeserverPath": "/var/lib/mem/private/homeserver.yaml",
            "elementConfigPath": "/var/lib/mem/private/config.json",
            "manifestSaved": true,
            "databaseOwnershipSaved": true,
            "stackRegistered": true
          },
          "routes": {
            "matrixPublicHost": "matrix-action.example.test",
            "matrixForwardHost": "mem-matrix-action-stack-restored",
            "matrixForwardPort": 8008,
            "matrixRouteId": "internal-route-id",
            "matrixRouteReady": true,
            "elementPublicHost": "chat-action.example.test",
            "elementForwardHost": "mem-element-action-stack-restored",
            "elementForwardPort": 80,
            "elementRouteId": "internal-route-id",
            "elementRouteReady": true,
            "publicReadinessPassed": true
          },
          "mutations": {
            "runtimeStackCreated": true,
            "productionPostgresMutated": true,
            "productionContainersTouched": true,
            "npmRoutesChanged": true,
            "dnsChanged": false,
            "certificatesChanged": false,
            "oldStacksDeleted": false,
            "notes": ["Internal mutation detail."]
          },
          "checks": [
            {
              "code": "production-recreate.stack.registered",
              "severity": "error",
              "passed": true,
              "message": "Runtime stack was saved into inventory.",
              "detail": "/var/lib/mem/private/manifest.json"
            }
          ],
          "warnings": [],
          "errors": [],
          "detail": "Production Recreate completed and public readiness checks passed.",
          "catalogEntryId": "bkp_20260702-catalog-action-01",
          "sourceKind": "backup-catalog"
        }
        """;

    public const string CancelledAttemptResponse = """
        {
          "id": "f0db91d1-b6e3-4dee-8564-f29c32626306",
          "restoreSessionId": "restore-20260702-action-01",
          "sourceKind": "backup-catalog",
          "sourceKey": "internal-source-key",
          "catalogEntryId": "bkp_20260702-catalog-action-01",
          "sourceDisplayName": "Action backup",
          "sourceOriginKind": "imported-zip",
          "sourceStackSlug": "action-stack",
          "sourceBackupId": null,
          "status": "cancelled",
          "currentStage": "cancelled",
          "createdAtUtc": "2026-07-02T14:00:00Z",
          "updatedAtUtc": "2026-07-02T14:04:00Z",
          "terminalAtUtc": "2026-07-02T14:04:00Z",
          "lastEventAtUtc": "2026-07-02T14:04:00Z",
          "lastErrorCode": null,
          "lastErrorSummary": null,
          "warningCount": 0,
          "errorCount": 0,
          "sessionDirectoryPath": "/var/lib/mem/private/session",
          "logDirectoryPath": "/var/lib/mem/private/logs",
          "supportReportPath": "/var/lib/mem/private/report.json",
          "runtimeOperationId": null,
          "backupCatalogEntryId": "068f8637-ed2a-4b31-b5dc-492786a7a70d"
        }
        """;

    public const string CompletedAttemptResponse = """
        {
          "id": "f0db91d1-b6e3-4dee-8564-f29c32626306",
          "restoreSessionId": "restore-20260702-action-01",
          "sourceKind": "backup-catalog",
          "sourceKey": "internal-source-key",
          "catalogEntryId": "bkp_20260702-catalog-action-01",
          "sourceDisplayName": "Action backup",
          "sourceOriginKind": "imported-zip",
          "sourceStackSlug": "action-stack",
          "sourceBackupId": null,
          "status": "completed",
          "currentStage": "public-verification",
          "createdAtUtc": "2026-07-02T14:00:00Z",
          "updatedAtUtc": "2026-07-02T14:05:00Z",
          "terminalAtUtc": "2026-07-02T14:05:00Z",
          "lastEventAtUtc": "2026-07-02T14:05:00Z",
          "lastErrorCode": null,
          "lastErrorSummary": null,
          "warningCount": 0,
          "errorCount": 0,
          "sessionDirectoryPath": "/var/lib/mem/private/session",
          "logDirectoryPath": "/var/lib/mem/private/logs",
          "supportReportPath": "/var/lib/mem/private/report.json",
          "runtimeOperationId": null,
          "backupCatalogEntryId": "068f8637-ed2a-4b31-b5dc-492786a7a70d"
        }
        """;
    public const string PrivateTestDestroyResponse = """
        {
          "source": "control-plane",
          "status": "destroyed",
          "mode": "private-synapse-staging",
          "stagingId": "staging-20260702-action-01",
          "workspacePath": "/var/lib/mem/private/should-not-reach-cli-output",
          "runtimePath": "/var/lib/mem/private/runtime",
          "postgresContainerId": "internal-postgres-id",
          "synapseContainerId": "internal-synapse-id",
          "destroy": {
            "destroyedAtUtc": "2026-07-02T14:06:00Z",
            "synapseContainerRemoved": true,
            "postgresContainerRemoved": true,
            "networkRemoved": true,
            "workspaceRemoved": true,
            "warnings": []
          },
          "warnings": [],
          "errors": [],
          "detail": "The retained private staging runtime was destroyed."
        }
        """;

}
