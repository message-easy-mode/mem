# MEM Conversion Worker Contract v1

The conversion worker is an internal, non-listening process contract used by the MEM control plane. It reuses the existing `SynapseConversionService`; it is not a second conversion engine.

## Invocation

```bash
mem-migrate worker convert --request /absolute/server-owned/request.json
```

The command accepts exactly one request-file argument. The control plane must invoke the executable directly without a shell.

## Request

Schema: `mem-conversion-worker-request`, version `1`.

Required values include an operation ID, conversion ID, and absolute server-owned archive, workspace, and output paths. The browser must never supply these paths directly.

## Output

Standard output is JSON Lines. Every line uses schema `mem-conversion-worker-event`, version `1`, and contains a monotonically increasing sequence number.

Current event types:

- `OperationStarted`
- `StepStarted`
- `StepCompleted`
- `OperationCompleted`
- `OperationFailed`

The existing immutable `conversion-report.json` and conversion evidence remain the authoritative conversion result.

## Exit codes

- `0`: completed without warnings
- `2`: completed with warnings
- `14`: invalid migration archive/request content
- `64`: invalid arguments or malformed request
- `70`: conversion execution failure

## Security

- No daemon or listener is created.
- No shell command is accepted from the request.
- Archive, work, and output paths must be absolute and server-owned.
- Existing Docker image validation, no-pull behavior, private network isolation, journalling, reconciliation, and cleanup remain in force.
