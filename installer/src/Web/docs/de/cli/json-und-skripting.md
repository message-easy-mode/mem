---
id: "de/cli/json-und-skripting"
translationKey: "cli/json-and-scripting"
locale: "de"
groupId: "cli-de"
groupKey: "cli"
groupLabel: "CLI und Automatisierung"
groupOrder: 80
title: "JSON-Ausgabe und Skripting verwenden"
description: "Verarbeiten Sie stabiles englisches JSON, bewahren Sie Exit-Codes, sammeln Sie begrenzte Nachweise und halten Sie Secrets aus Shell-Automatisierung."
order: 48
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "JSON", "Skripting", "Exit-Codes", "KI-Unterstützung"]
route: "/docs/de/cli/json-und-skripting"
aliases: []
outputPath: "docs/de/cli/json-und-skripting.md"
preserveLegacyBranding: false
---
# JSON-Ausgabe und Skripting verwenden

Verwenden Sie `--json`, wenn Programme, Supportabläufe oder KI-gestützte Diagnose stabile Struktur benötigen.

```bash
mem host status --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

## Stabile Maschinensprache

JSON-Feldnamen, Statuswerte, Fehlercodes, IDs und Optionen bleiben Englisch, auch bei deutscher lesbarer Ausgabe:

```bash
mem host status --profile home --language de --json | jq
```

Parsen Sie keine menschenlesbare Ausgabe in Automatisierung.

## Exit-Code bewahren

Eine Pipeline kann den CLI-Status verdecken. Mit Bash und `jq`:

```bash
set -o pipefail
mem host status --profile home --json | jq
status=$?
printf 'mem status: %s\n' "$status"
```

Aktuelle Konvention:

| Exit-Code | Bedeutung |
|---:|---|
| `0` | Ergebnis erfüllt den Erfolgsvertrag |
| `1` | Nutzung, lokale Konfiguration, Profil, Login, Secret Store oder Autorisierungseingabe fehlerhaft |
| `2` | Anfrage lief, aber Betriebsergebnis war nicht bereit, gesund, gefunden, gültig oder abgeschlossen |

Lesen Sie JSON-`status`, `error`, Hinweise und Details zusammen mit dem Prozessstatus.

## Interaktive Ausnahme

`mem login --device` unterstützt kein `--json`. Automatisierung darf die interaktive Ausgabe nicht scrapen oder Geräte automatisch autorisieren.

## Minimales sicheres Nachweisbündel

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

Nur die betroffene Ressource ergänzen:

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Logseiten begrenzen. Den redigierten Supportbericht bevorzugen, wenn er die nötigen Nachweise enthält.

## Häufige sichere Fehlercodes

```text
cli_profile_required
cli_device_login_required
cli_secret_store_unavailable
cli_device_credential_invalid
agent_secret_retired
installer_token_retired
cli_secret_material_rejected
cli_recovery_arm_not_available
```

Der genaue Fehlercode ist teilbar. Rohe Transportantworten und Exception-Texte werden bewusst nicht ausgegeben.

## Regeln für Secrets

Niemals in Argumente, Umgebungsvariablen, JSON-Dateien, stdin, Logs, Tickets oder Prompts schreiben:

- Passwörter oder TOTP-Werte;
- Wiederherstellungscodes;
- CLI-Geräte-Credentials oder Bearer-Tokens;
- Browser-Cookies;
- Matrix-Signatur- oder private Schlüssel;
- rohe Datenbankdumps oder Backup-Payloads.

MEM lehnt `--password`, `--totp`, `--totp-code`, `--recovery-code`, `--device-credential` und `--bearer-token` explizit ab.

## Verwandte Dokumentation

- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
- [Restore-Befehle](restore-befehle.md)
