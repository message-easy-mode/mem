# MM-03 Archive Contract

## Purpose

MM-03 creates a complete, verifiable rehearsal capture of the exact supported
MEM v0.1.0 source without changing the source installation.

The archive is input to MM-04 conversion proof. It is not itself a normal MEM
backup and it is not accepted directly by the permanent control plane.

## Identity

```text
schema: mem-v010-migration
schemaVersion: 1
root: mem-migration/
```

Unknown schema versions fail closed.

## Capture state

MM-03 emits only:

```json
{
  "kind": "preview",
  "sourceFrozen": false,
  "rehearsalOnly": true
}
```

No command in MM-03 may emit a final or frozen capture.

## Consistency

- PostgreSQL: custom-format `pg_dump` snapshot.
- Synapse SQLite: online backup through SQLite.
- Configuration and signing key: direct read-only file copy.
- Media: live file copy with source fingerprint comparison.
- Docker and routes: typed assessment evidence.

Source fingerprints before and after the copy are included. Drift is a warning,
not a claim of final consistency.

## Path policy

Every file is beneath `mem-migration/` and uses canonical UTF-8/NFC forward
slash paths. Reject:

- rooted and drive-prefixed paths;
- `.` and `..` segments;
- empty segments;
- backslashes and NUL characters;
- non-normalized Unicode;
- exact duplicates;
- case-folding collisions;
- symbolic links and non-regular archive entry types.

Source and private staging traversal also reject symbolic links, hard-linked
regular files, and special filesystem entries. The writer emits an
`.unverified` archive, performs an independent full checksum verification, and
only then promotes it to the final `.memmigration.zip` path.

## Checksums

`checksums/sha256.json` lists every archive file except itself exactly once.
The source capture receipt records the SHA-256 of the complete ZIP, which covers
the checksum index.

## Encryption

A cross-server artifact is a normal ZIP64 archive wrapped by standard `age`
authenticated encryption:

```text
<name>.memmigration.zip.age
```

The source receives only a recipient/public key. Decryption requires the target
identity. Plain ZIP transfer is outside the supported cross-server workflow.

## Development coexistence transport

Development-only port overrides, especially `:18443`, may appear in source
runtime evidence but are excluded from canonical service URLs. They never
change Matrix `server_name`, signing identity, or target public-host semantics.

## Explicit exclusions

MM-03 does not:

- stop or restart containers;
- freeze Synapse;
- mutate PostgreSQL or SQLite;
- modify NPM;
- convert databases;
- create v0.1.1 target records;
- import old operator identities or sessions;
- claim that a live capture is final.

## Disk preflight

Plaintext capture requires the configured staging multiplier, which defaults to
2.25 times the expanded estimate plus 256 MiB. Encrypted capture enforces at
least 3.25 times the estimate plus 256 MiB to cover staging, the verified ZIP,
and the encrypted partial output at peak.
