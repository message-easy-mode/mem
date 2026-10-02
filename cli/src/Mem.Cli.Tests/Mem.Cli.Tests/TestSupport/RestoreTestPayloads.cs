namespace Mem.Cli.Tests.TestSupport;

public static class RestoreTestPayloads
{
    public const string RestoreSessionId = "restore-20260702-restore-01";

    public const string RestoreListResponse = """
        {
          "source": "control-plane",
          "status": "ok",
          "query": {
            "page": 2,
            "pageSize": 25,
            "search": "restored",
            "status": "needs-action",
            "targetStack": "cool-stack",
            "sortBy": "updated",
            "sortDirection": "desc"
          },
          "summary": {
            "totalSessions": 1,
            "productionRecreateCount": 1,
            "publiclyVerifiedCount": 0,
            "needsActionCount": 1
          },
          "totalSessions": 1,
          "page": 2,
          "pageSize": 25,
          "totalPages": 3,
          "hasPreviousPage": true,
          "hasNextPage": true,
          "targetStacks": ["cool-stack"],
          "sessions": [
            {
              "restoreSessionId": "restore-20260702-restore-01",
              "workspaceAvailable": true,
              "sourceKind": "backup-catalog",
              "sourceLabel": "Deleted backup",
              "sourceDeleted": true,
              "catalogEntryId": null,
              "sourceStackSlug": "cool-stack",
              "sourceBackupId": "bkp-old-01",
              "targetStackSlug": "cool-stack-restored",
              "status": "awaiting-handover",
              "statusLabel": "Needs handover",
              "currentStage": "verification",
              "currentStageLabel": "Public verification",
              "progressPercent": 90,
              "startedAtUtc": "2026-07-02T10:00:00Z",
              "lastUpdatedAtUtc": "2026-07-02T10:15:00Z",
              "terminalAtUtc": null,
              "warningCount": 1,
              "errorCount": 0,
              "productionRecreateStarted": true,
              "publiclyVerified": false,
              "nextActionCode": "complete-handover",
              "nextActionTitle": "Complete handover",
              "detail": "Public verification is ready for operator review."
            }
          ],
          "warnings": [],
          "detail": null
        }
        """;

    public const string RestoreWorkspaceResponse = """
        {
          "schemaVersion": 1,
          "restoreSessionId": "restore-20260702-restore-01",
          "attempt": {
            "id": "b5128daa-4e81-4d9e-9976-0e2b9815eb21",
            "status": "awaiting-handover",
            "currentStage": "verification",
            "createdAtUtc": "2026-07-02T10:00:00Z",
            "updatedAtUtc": "2026-07-02T10:15:00Z",
            "terminalAtUtc": null,
            "lastEventAtUtc": "2026-07-02T10:15:00Z",
            "lastErrorCode": null,
            "lastErrorSummary": null,
            "warningCount": 1,
            "errorCount": 0,
            "currentOperationId": null
          },
          "source": {
            "kind": "backup-catalog",
            "stackSlug": "cool-stack",
            "backupId": "bkp-old-01",
            "createdAtUtc": "2026-06-28T14:31:08Z",
            "sizeBytes": 196362,
            "matrixHost": "matrix.cool-stack.test",
            "elementHost": "chat.cool-stack.test",
            "validationStatus": "valid",
            "validationSummary": "Structural, manifest, and checksum validation passed.",
            "catalogEntryId": null,
            "sourceDisplayName": "Deleted backup",
            "sourceOriginKind": "local-captured",
            "sourceDeleted": true
          },
          "target": {
            "stackSlug": "cool-stack-restored",
            "matrixHost": "matrix-restored.cool-stack.test",
            "elementHost": "chat-restored.cool-stack.test",
            "availability": "claimed",
            "detail": "Target resources are held by this Restore Workspace.",
            "claims": [
              {
                "resourceType": "stack-slug",
                "resourceValue": "cool-stack-restored",
                "status": "claimed",
                "claimedAtUtc": "2026-07-02T10:01:00Z",
                "releasedAtUtc": null,
                "releaseReason": null
              }
            ]
          },
          "overallStatus": {
            "code": "review-issue",
            "title": "Review before handover",
            "description": "Public verification is ready for operator review.",
            "severity": "warning",
            "nextAction": {
              "code": "complete-handover",
              "title": "Complete handover",
              "description": "Record the terminal handover decision.",
              "enabled": true,
              "relatedStage": "verification"
            }
          },
          "standardStages": [
            {
              "code": "private-test",
              "title": "Private test",
              "description": "Test the catalog payload privately.",
              "state": "completed",
              "required": true,
              "unlocked": true,
              "completedAtUtc": "2026-07-02T10:05:00Z",
              "primaryAction": null,
              "secondaryActions": [],
              "summary": "Private staging completed and was destroyed.",
              "blockers": [],
              "evidenceSummary": {
                "itemCount": 1,
                "latestOccurredAtUtc": "2026-07-02T10:05:00Z",
                "latestStatus": "passed"
              },
              "operationSummary": {
                "operationId": "b5128daa-4e81-4d9e-9976-0e2b9815eb22",
                "operation": "private-test",
                "status": "completed",
                "currentStep": null,
                "requestedAtUtc": "2026-07-02T10:02:00Z",
                "startedAtUtc": "2026-07-02T10:02:02Z",
                "completedAtUtc": "2026-07-02T10:05:00Z"
              },
              "privateTestEvidence": {
                "sourceKind": "backup-catalog",
                "catalogEntryId": null,
                "stagingId": "staging-20260702-01",
                "matrixServerName": "matrix.cool-stack.test",
                "status": "passed",
                "privateOnly": true,
                "dockerNetworkInternal": true,
                "databaseImportSucceeded": true,
                "synapseHealthPassed": true,
                "requiresExplicitDestroy": false,
                "completedAtUtc": "2026-07-02T10:05:00Z",
                "stagingRuntimeStatus": "destroyed",
                "stagingRuntimeDestroyed": true,
                "destroyAvailable": false,
                "destroyedAtUtc": "2026-07-02T10:05:01Z"
              }
            }
          ],
          "advancedTools": [],
          "verification": {
            "status": "passed",
            "hasRun": true,
            "allPassed": true,
            "checkedAtUtc": "2026-07-02T10:15:00Z",
            "checks": [
              {
                "code": "matrix-health",
                "title": "Matrix health",
                "status": "passed"
              }
            ],
            "summary": "All public checks passed."
          },
          "evidence": {
            "categories": [
              {
                "code": "private-test",
                "title": "Private test evidence",
                "status": "passed",
                "itemCount": 1,
                "latestOccurredAtUtc": "2026-07-02T10:05:00Z",
                "items": [
                  {
                    "code": "private-test-passed",
                    "category": "private-test",
                    "title": "Private test passed",
                    "status": "passed",
                    "occurredAtUtc": "2026-07-02T10:05:00Z",
                    "eventCode": "restore.private-test.completed",
                    "operationId": "b5128daa-4e81-4d9e-9976-0e2b9815eb22",
                    "stage": "private-test",
                    "description": "Private staging completed and was destroyed."
                  }
                ]
              }
            ],
            "latestFailure": null,
            "latestSuccess": {
              "code": "private-test-passed",
              "category": "private-test",
              "title": "Private test passed",
              "status": "passed",
              "occurredAtUtc": "2026-07-02T10:05:00Z",
              "eventCode": "restore.private-test.completed",
              "operationId": "b5128daa-4e81-4d9e-9976-0e2b9815eb22",
              "stage": "private-test",
              "description": "Private staging completed and was destroyed."
            }
          },
          "logs": {
            "totalEvents": 2,
            "warningCount": 1,
            "errorCount": 0,
            "latestEvent": {
              "timestampUtc": "2026-07-02T10:15:00Z",
              "stage": "verification",
              "severity": "information",
              "eventCode": "restore.verification.passed",
              "message": "Public verification passed.",
              "operationId": null
            },
            "latestWarningOrError": {
              "timestampUtc": "2026-07-02T10:10:00Z",
              "stage": "verification",
              "severity": "warning",
              "eventCode": "restore.verification.review",
              "message": "Review handover before completing.",
              "operationId": null
            },
            "supportReportAvailable": true,
            "supportBundleAvailable": false,
            "warnings": []
          },
          "warnings": ["The source catalog item was deleted after terminal work began."],
          "cancellation": {
            "canCancel": true,
            "reasonUnavailable": null,
            "summary": "Cancellation releases temporary claims and keeps audit history."
          }
        }
        """;

    public const string RestoreLogsResponse = """
        {
          "restoreSessionId": "restore-20260702-restore-01",
          "page": 2,
          "pageSize": 50,
          "totalEvents": 3,
          "totalPages": 1,
          "summary": {
            "totalEvents": 3,
            "warningCount": 1,
            "errorCount": 0,
            "latestEvent": {
              "schemaVersion": 1,
              "eventId": "event-03",
              "timestampUtc": "2026-07-02T10:15:00Z",
              "restoreSessionId": "restore-20260702-restore-01",
              "operationId": null,
              "stage": "verification",
              "severity": "information",
              "eventCode": "restore.verification.passed",
              "message": "Public verification passed.",
              "details": null
            },
            "latestWarningOrError": {
              "schemaVersion": 1,
              "eventId": "event-02",
              "timestampUtc": "2026-07-02T10:10:00Z",
              "restoreSessionId": "restore-20260702-restore-01",
              "operationId": null,
              "stage": "verification",
              "severity": "warning",
              "eventCode": "restore.verification.review",
              "message": "Review handover before completing.",
              "details": null
            }
          },
          "events": [
            {
              "schemaVersion": 1,
              "eventId": "event-03",
              "timestampUtc": "2026-07-02T10:15:00Z",
              "restoreSessionId": "restore-20260702-restore-01",
              "operationId": null,
              "stage": "verification",
              "severity": "information",
              "eventCode": "restore.verification.passed",
              "message": "Public verification passed.",
              "details": { "checkCount": "2" }
            }
          ],
          "warnings": []
        }
        """;

    public const string SupportReportResponse = """
        {
          "schemaVersion": 1,
          "generatedAtUtc": "2026-07-02T10:16:00Z",
          "memVersion": "0.1.1-dev",
          "restoreSessionId": "restore-20260702-restore-01",
          "attempt": {
            "status": "awaiting-handover",
            "currentStage": "verification",
            "createdAtUtc": "2026-07-02T10:00:00Z",
            "updatedAtUtc": "2026-07-02T10:15:00Z",
            "terminalAtUtc": null,
            "lastErrorCode": null,
            "lastErrorSummary": null,
            "warningCount": 1,
            "errorCount": 0
          },
          "source": {
            "sourceKind": "backup-catalog",
            "sourceKey": "internal-source-key-should-not-reach-cli-output",
            "catalogEntryId": null,
            "sourceDisplayName": "Deleted backup",
            "sourceOriginKind": "local-captured",
            "sourceStackSlug": "cool-stack",
            "sourceBackupId": "bkp-old-01",
            "sourceDeleted": true
          },
          "target": {
            "targetStackSlug": "cool-stack-restored",
            "matrixHost": "matrix-restored.cool-stack.test",
            "elementHost": "chat-restored.cool-stack.test"
          },
          "logs": {
            "totalEvents": 3,
            "warningCount": 1,
            "errorCount": 0,
            "latestEvent": null,
            "latestWarningOrError": null
          },
          "operations": [
            {
              "operationId": "b5128daa-4e81-4d9e-9976-0e2b9815eb22",
              "operation": "private-test",
              "status": "completed",
              "currentStep": null,
              "requestedAtUtc": "2026-07-02T10:02:00Z",
              "startedAtUtc": "2026-07-02T10:02:02Z",
              "completedAtUtc": "2026-07-02T10:05:00Z",
              "lastError": null
            }
          ],
          "recentEvents": [],
          "warnings": []
        }
        """;
}
