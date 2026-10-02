---
id: "de/cli/backup-befehle"
translationKey: "cli/backup-commands"
locale: "de"
groupId: "cli-de"
groupKey: "cli"
groupLabel: "CLI und Automatisierung"
groupOrder: 80
title: "Befehle für den Sicherungskatalog verwenden"
description: "Prüfen, exportieren, importieren und entfernen Sie Sicherungsmaterial und trennen Sie Katalogidentität von Upload-Herkunft."
order: 46
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "Sicherungskatalog", "portabler Export", "Import", "Löschen"]
route: "/docs/de/cli/backup-befehle"
aliases: []
outputPath: "docs/de/cli/backup-befehle.md"
preserveLegacyBranding: false
---
# Befehle für den Sicherungskatalog verwenden

Die CLI arbeitet mit vorhandenem Material im Sicherungskatalog. Sie erstellt derzeit keine neue Sicherungserfassung; erstellen Sie Sicherungen in der Control Plane.

## Identitäten trennen

```text
validationId       Herkunft eines aufbewahrten Upload-ZIPs
catalogEntryId     dauerhafte Quelle im Sicherungskatalog
restoreSessionId   Identität des Wiederherstellungsarbeitsbereichs
```

Ein validiertes portables ZIP muss zu einem Katalogeintrag materialisiert werden, bevor es Restore-Quelle ist.

## Katalogeinträge auflisten und prüfen

```bash
mem backups list --profile home
mem backups list --profile home --json | jq

mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

`lifecycle` ist die sicherste Prüfung vor dem Löschen. Sie zeigt, ob die Quelle entfernt werden darf und ob ein aktiver Wiederherstellungsarbeitsbereich blockiert.

`mem backups list --stack ...` wird nicht unterstützt. Listen Sie den Katalog auf und prüfen Sie dann den gewählten Eintrag.

## Portables Backup exportieren

```bash
mem backups export <catalog-entry-id> \
  --out ./mem-backup.zip \
  --profile home \
  --json | jq
```

MEM erstellt serverseitig einen portablen Export, lädt ihn herunter, erstellt bei Bedarf das lokale Elternverzeichnis und schreibt den gewünschten Pfad. Wählen Sie ihn sorgfältig; eine bestehende Datei kann ersetzt werden.

Prüfen Sie Ausgabepfad, geschriebene Bytes, Hinweise, Quell-Stack und Katalog-ID, bevor Sie das ZIP extern ablegen.

## Portables ZIP importieren

```bash
mem backups import ./mem-backup.zip --profile home --json | jq
```

Exit-Code `0` verlangt sowohl gültige Aufnahme als auch eine materialisierte Katalog-ID. Ein Validierungsdatensatz ohne Katalogeintrag ist nicht restore-bereit.

Upload-Herkunft prüfen:

```bash
mem backups uploads inspect <validation-id> --profile home --json | jq
```

Nur das aufbewahrte Upload-ZIP löschen:

```bash
mem backups uploads delete <validation-id> --yes --profile home --json | jq
```

Dies löscht weder den materialisierten Katalog-Payload noch einen Wiederherstellungsarbeitsbereich.

## Katalogeintrag dauerhaft löschen

```bash
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
mem backups delete <catalog-entry-id> --yes --profile home --json | jq
```

Das Löschen ist irreversibel und benötigt `--yes`. Die CLI liest zuerst den Lebenszyklus und verweigert die Servermutation, wenn eine aktive Restore-Sitzung blockiert. Das Ergebnis weist Payload, Originalarchiv, portable Exporte und getrennte terminale Restore-Historie separat aus.

Der Server kann aktuelle Step-up-Bestätigung verlangen. Die CLI schlägt bei HTTP 403 geschlossen fehl und akzeptiert keine Passwörter, TOTP-Codes, Wiederherstellungscodes, Bearer-Tokens oder Geräte-Credentials als Optionen.

## Erfolg prüfen

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem restores list --profile home --json | jq
```

## Verwandte Dokumentation

- [Chatserver sichern und wiederherstellen](../backups-und-wiederherstellen/index.md)
- [Restore-Befehle](restore-befehle.md)
- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
