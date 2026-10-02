---
id: "de/cli/restore-befehle"
translationKey: "cli/restore-commands"
locale: "de"
groupId: "cli-de"
groupKey: "cli"
groupLabel: "CLI und Automatisierung"
groupOrder: 80
title: "Befehle für Wiederherstellungsarbeitsbereiche verwenden"
description: "Prüfen Sie dauerhafte Restore-Sitzungen, führen Sie private Tests aus, prüfen Sie Standard-Neuerstellung und bestätigen Sie Produktionsmutationen explizit."
order: 47
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "Wiederherstellungsarbeitsbereich", "privater Test", "Standard-Neuerstellung", "Supportbericht"]
route: "/docs/de/cli/restore-befehle"
aliases: []
outputPath: "docs/de/cli/restore-befehle.md"
preserveLegacyBranding: false
---
# Befehle für Wiederherstellungsarbeitsbereiche verwenden

Restore-Befehle sind katalogzentriert und sitzungsbasiert. Die Quelle trägt `catalogEntryId`; alle späteren Arbeiten verwenden `restoreSessionId`.

## Restore-Sitzungen auflisten

```bash
mem restores list --profile home
mem restores list --page 1 --page-size 25 --profile home --json | jq
```

Filter:

```bash
mem restores list --search <text> --profile home --json | jq
mem restores list --status active --profile home --json | jq
mem restores list --target-stack demo --profile home --json | jq
mem restores list --sort-by updatedAtUtc --sort-direction desc --profile home --json | jq
```

`--page` und `--page-size` müssen positive Ganzzahlen sein. Der Server prüft unterstützte Filter und Sortierung.

## Arbeitsbereich erstellen oder fortsetzen

```bash
mem restores create <catalog-entry-id> --profile home --json | jq
```

Der Server kann den vorhandenen aktiven Arbeitsbereich derselben Quelle zurückgeben. Notieren Sie die Restore-Sitzungs-ID; ersetzen Sie sie nicht durch Katalog- oder Validierungs-ID.

## Zustand und Nachweise prüfen

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Logs unterstützen Seite, Seitengröße, Schweregrad, Stufe und Textsuche. `support-report` liest den vorhandenen Bericht und erzeugt keinen implizit.

Terminale Restore-Historie bleibt auch nach späterer Löschung des Quellkatalogeintrags lesbar. Die Quelle wird ehrlich als gelöschtes Backup dargestellt.

## Privaten Test ausführen

```bash
mem restores private-test <restore-session-id> --profile home --json | jq
```

Erfolg liefert `ready`. Der private Test erstellt isoliertes Staging, keinen öffentlichen Produktions-Stack.

Wenn Nachweise die Löschung des aufbewahrten Stagings erlauben:

```bash
mem restores private-test destroy <restore-session-id> \
  --yes \
  --profile home \
  --json | jq
```

Dies entfernt nur Container, Netzwerk und Arbeitsbereich des privaten Tests. Katalogquelle, Produktions-Stack und dauerhafte Restore-Nachweise bleiben erhalten.

## Standard-Neuerstellung vorprüfen

```bash
mem restores recreate preflight <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --matrix-host <matrix-host> \
  --profile home \
  --json | jq
```

`--matrix-host` und `--requested-domain-id` sind optional. Prüfen Sie jeden Check, jede Zielreservierung, jeden Hostnamenkonflikt und Hinweis.

## Standard-Neuerstellung ausführen

```bash
mem restores recreate execute <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --execute-production-recreate \
  --acknowledge-creates-real-stack \
  --acknowledge-mutates-production-postgres \
  --acknowledge-mutates-npm-routes \
  --acknowledge-no-automatic-rollback \
  --profile home \
  --json | jq
```

Verwenden Sie dieselben Zielwerte wie in der Vorprüfung. Wenn dort `--matrix-host` oder `--requested-domain-id` angegeben wurde, geben Sie diese bei der Ausführung erneut an.

Die Aktion kann einen echten Stack erstellen, Produktions-PostgreSQL importieren, Produktionscontainer starten und Nginx-Proxy-Manager-Routen ändern. Es gibt kein automatisches Rollback. Alle vier Bestätigungsflags sind verpflichtend.

Serverrollen und Step-up-Policy bleiben maßgeblich. Bei HTTP 403 schlägt die CLI geschlossen fehl; verwenden Sie den geschützten Browserablauf statt Secrets in der Shell.

## Abbrechen oder Übergabe abschließen

```bash
mem restores cancel <restore-session-id> --yes --profile home --json | jq
mem restores handover complete <restore-session-id> --yes --profile home --json | jq
```

Abbruch gibt temporäre Reservierungen nur frei, wenn kein laufender Vorgang halb mutiert bleiben kann. Katalogquelle und Auditverlauf bleiben. Die Übergabe markiert einen verifizierten Arbeitsbereich als abgeschlossen und löscht den Datensatz nicht.

## Verwandte Dokumentation

- [Wiederherstellungsarbeitsbereich starten](../backups-und-wiederherstellen/wiederherstellungsarbeitsbereich.md)
- [Standard-Neuerstellung](../backups-und-wiederherstellen/standard-neuerstellung.md)
- [JSON-Ausgabe und Skripting](json-und-skripting.md)
