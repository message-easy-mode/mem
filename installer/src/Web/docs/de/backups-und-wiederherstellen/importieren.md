---
id: "de/backups-restores/import-backup"
translationKey: "backups-restores/import-backup"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Portable Sicherung importieren und materialisieren"
description: "Validieren Sie ein ZIP, trennen Sie Aufnahmeidentität von Wiederherstellungsidentität und erzeugen Sie einen Katalogeintrag."
order: 40
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Import", "Validierung", "Materialisierung", "ZIP", "Herkunft"]
route: "/docs/de/backups-and-restores/importieren"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/importieren.md"
preserveLegacyBranding: false
---
# Portable Sicherung importieren und materialisieren

Unter **Sicherungen → ZIP importieren** bringen Sie eine portable MEM-Sicherung in die aktuelle Control Plane.

## Zuerst validieren

1. Portable `.zip`-Datei auswählen.
2. **Validieren** wählen.
3. Archivgröße, Eintragszahl, entpackte Größe, Manifest, Prüfsummen, Prüfungen, Warnungen und Fehler kontrollieren.
4. Nur fortfahren, wenn das Ergebnis gültig ist und das Manifest den erwarteten Stack identifiziert.

Die Validierung prüft Archivstruktur, Manifest, erforderliche Payload-Deklarationen und Dateiprüfsummen. Eine fehlgeschlagene Validierung erzeugt keine nutzbare Wiederherstellungsquelle.

## Automatische Materialisierung

Bei einem gültigen Upload materialisiert MEM den Payload normalerweise während der Aufnahme in den Sicherungskatalog und liefert eine Katalog-ID zurück.

Existiert ein aufbewahrter gültiger Upload ohne verknüpften Katalogeintrag, öffnen Sie die Upload-Detailseite und verwenden **Materialisieren**. Dadurch werden Wiederherstellungsdateien in Katalogspeicher kopiert; es startet keine Wiederherstellung.

## Identitäten trennen

```text
Validierungs-ID  → Herkunft des hochgeladenen ZIPs
Katalog-ID       → verwalteter wiederherstellbarer Payload
Restore-ID       → dauerhafter Wiederherstellungsarbeitsbereich
```

Eine Validierungs-ID gehört nicht in eine Restore-URL.

## Warnungen und Hinweise

Ein gültiges Archiv kann nicht blockierende Hinweise behalten, etwa zum sicheren Umgang mit dem Signaturschlüssel, zur notwendigen Abschaltung eines alten Servers oder zum Snapshot-Charakter von Routen und TURN. Hinweise sind keine Prüfsummenfehler, müssen aber gelesen werden.

## Lebenszyklus des Original-Uploads

Nach erfolgreicher Materialisierung besitzen Original-ZIP und Katalog-Payload getrennte Lebenszyklen. Die Löschung des Original-ZIPs löscht nicht:

- den Katalog-Payload;
- Wiederherstellungssitzungen, Logs, Nachweise oder Supportberichte;
- einen wiederhergestellten Produktions-Stack.

Bewahren oder löschen Sie den Original-Upload entsprechend Ihrer Herkunfts- und Speicherrichtlinie. Löschen Sie den Katalog-Payload nicht, solange Wiederherstellung benötigt wird.
