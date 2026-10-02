---
title: Host und Chatserver prüfen
description: Verwenden Sie Hoststatus, Stack-Inventar, sichere Prüfung, Doctor-Checks und Vorgangsverlauf ohne nicht unterstützte Lifecycle-Befehle zu erfinden.
section: CLI und Automatisierung
order: 45
---

# Host und Chatserver prüfen

Die aktuelle CLI stellt Beobachtungs- und Diagnosebefehle für den Control-Plane-Host und verwaltete Chatserver bereit. Sie erstellt, startet, stoppt oder entfernt derzeit keine Stacks.

## Bereitschaft der Control Plane prüfen

```bash
mem host status --profile home
mem host status --profile home --json | jq
```

Exit-Code `0` verlangt den Hoststatus `ready`. Ein erreichbares, aber nicht bereites Ergebnis liefert `2`.

## Chatserver auflisten

```bash
mem stack list --profile home
mem stack list --profile home --json | jq
```

Die Liste enthält stabile Stack-Identität, letzten geprüften Zustand und bekannte öffentliche Matrix- und Element-URLs.

## Einen Stack sicher prüfen

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
```

Die CLI projiziert bewusst öffentliche Servicedaten statt roher Docker-Container-IDs, Hostpfade, Umgebungswerte oder geheimnishaltiger Laufzeitkonfiguration.

## Doctor-Checks ausführen

```bash
mem stack doctor <slug-or-id> --profile home
mem stack doctor <slug-or-id> --profile home --json | jq
```

Doctor erfasst oder liest einen servereigenen Diagnosevorgang. Exit-Code `0` bedeutet, dass alle gelieferten Checks bestanden. Exit-Code `2` bedeutet mindestens einen fehlgeschlagenen Check oder ein nicht erfolgreiches Betriebsergebnis.

Ein fehlgeschlagener Check ist ein Nachweis, keine Erlaubnis zur manuellen Docker-Mutation. Prüfen Sie Code, URL, Status, Vorgangs-ID, Bericht-ID und sichere Details.

## Vorgangsverlauf prüfen

```bash
mem stack operations <slug-or-id> --profile home --json | jq
```

Der Verlauf kann Aktion, Status, Anforderer, Mutationsstufe, aktuellen Schritt, Zeitstempel, Idempotency Key und sichere letzte Fehlerzusammenfassung zeigen.

## Nicht unterstützte Befehle

```text
mem stack create
mem stack start
mem stack stop
mem stack delete
mem stack compare
```

Verwenden Sie für Lifecycle-Änderungen die aktuelle Control Plane und den dokumentierten Operatorablauf.

## Nachweisbündel

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Teilen Sie nur notwendige Nachweise und niemals Credentials, Passwörter, TOTP- oder Wiederherstellungscodes, Signatur- oder private Schlüssel, rohe Backup-Inhalte oder Secret-Service-Ausgabe.

## Verwandte Dokumentation

- [JSON-Ausgabe und Skripting](json-und-skripting.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
- [Chatserver betreiben](../chat-servers/index.md)
