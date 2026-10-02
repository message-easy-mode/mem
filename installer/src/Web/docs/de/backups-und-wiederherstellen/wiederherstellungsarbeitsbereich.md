---
id: "de/backups-restores/restore-workspace"
translationKey: "backups-restores/restore-workspace"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Wiederherstellungsarbeitsbereich starten und verwenden"
description: "Erstellen oder öffnen Sie den dauerhaften kataloggebundenen Arbeitsbereich für einen Wiederherstellungsversuch."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Wiederherstellungsarbeitsbereich", "Restore-Sitzung", "Stufen", "Nachweise", "Audit"]
route: "/docs/de/backups-and-restores/wiederherstellungsarbeitsbereich"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/wiederherstellungsarbeitsbereich.md"
preserveLegacyBranding: false
---
# Wiederherstellungsarbeitsbereich starten und verwenden

Ein Wiederherstellungsarbeitsbereich ist die dauerhafte Steuerungsoberfläche für einen kataloggebundenen Wiederherstellungsversuch.

## Starten oder fortsetzen

1. **Sicherungen** öffnen und einen verfügbaren Katalogeintrag auswählen.
2. Herkunft, Payload-Zustand, Integrität, Hinweise und Matrix-Identität prüfen.
3. **Wiederherstellen** wählen.
4. Bei vorhandenem aktiven Arbeitsbereich wird dieselbe Aktion zu **Fortsetzen**.

MEM öffnet `/restores/<restoreSessionId>`. Die ID bleibt über Browseraktualisierungen hinweg stabil und gehört in Vorgangs- oder Incident-Notizen.

## Standardablauf

1. **Sicherung bereit** — Katalogquelle ist nutzbar.
2. **Privater Test** — optionale isolierte Datenbank- und Synapse-Prüfung.
3. **Wiederhergestellten Server wählen und erstellen** — Zieldaten vorprüfen und Standard-Neuerstellung ausführen.
4. **Wiederhergestellten Server prüfen** — öffentliche Matrix-, Element-, Routen- und Konnektivitätsprüfungen.
5. **Abschließen und übergeben** — verifizierten Dienst bestätigen und Auditdatensatz behalten.

Beim erneuten Öffnen wird die aktuelle, fehlgeschlagene, laufende oder zuletzt abgeschlossene Stufe angezeigt.

## Registerkarten

- **Standard** — geführte Wiederherstellung.
- **Aktivität** — dauerhafte Zeitleiste.
- **Logs** — strukturierte, filterbare Ereignisse.
- **Nachweise** — kuratierte Erfolge, Warnungen und Fehler.
- **Konfiguration** — sichere Quellen-, Ziel-, Reservierungs- und Vorgangsdaten.
- **Erweitert** — Sonderwerkzeuge mit expliziten Verfügbarkeitsgründen.

## Quelle und Ziel

Die Quellenkarte identifiziert den Katalog-Payload. Das Ziel ist zunächst nicht ausgewählt. Die Standard-Neuerstellung bewahrt die Matrix-Serveridentität und erlaubt einen neuen Stack-Slug sowie einen verfügbaren Element-Host.

## Dauerhafte Nachweise

Eine Browseraktualisierung erzeugt keinen neuen Versuch. Vorgänge, Zielreservierungen, Warnungen, Fehler, Nachweise und Logs bleiben an die Restore-ID gebunden.

Wird der Katalog-Payload später permanent gelöscht, bleibt der Arbeitsbereich für Audit und Support lesbar; Aktionen mit Quellenbedarf werden jedoch blockiert.

> [!IMPORTANT]
> Führen Sie keine Wiederherstellung von der Upload-Detailseite aus. Aufnahme und Archivverwaltung gehören zum Import. Die Ausführung beginnt im Sicherungskatalog und läuft im Wiederherstellungsarbeitsbereich weiter.
