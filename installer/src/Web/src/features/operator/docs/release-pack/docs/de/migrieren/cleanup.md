---
title: Migrationsressourcen aufbewahren oder bereinigen
description: Bewahren Sie den alten Quellserver für den gewählten Zeitraum auf und entfernen Sie Pakete, Erfassungen und Staging-Ressourcen nur an sicheren Grenzen.
section: Von MEM 0.1.0 migrieren
order: 110
---

# Migrationsressourcen aufbewahren oder bereinigen

Eine Migration erzeugt Erfassungen, verschlüsselte Pakete, Zielarbeitsmaterial, Konvertierungsausgaben, privates Staging, eine normale Ziel-Laufzeit und dauerhafte Nachweise. Nicht alles hat dieselbe Löschgrenze.

## Ergebnis

Der angenommene Server und erforderliche Nachweise bleiben erhalten, temporäre Ressourcen werden sicher entfernt und die alte Quelle bleibt für den festgelegten Zeitraum verfügbar.

## Alte Quelle

MEM 0.2.0 löscht niemals automatisch den alten Host oder dessen Matrix-Daten. Nach der Annahme:

- für den aufgezeichneten Zeitraum aufbewahren;
- versehentliche normale Nutzung und Routenhoheit verhindern;
- Bewertung, Erfassung, Paketbericht und gegebenenfalls final eingefrorene Nachweise erhalten;
- verantwortliche Person für endgültige Entsorgung dokumentieren;
- erst entsorgen, wenn Rollback nicht mehr erforderlich ist.

## Material im Source Assistant

Der Source Assistant kann ein fertiges verschlüsseltes Paket und Paketberichte löschen. Quellerfassung, Bewertungshistorie, Journal, aktive Altdaten und Host bleiben bestehen.

Entfernen Sie eine Erfassung nur, wenn sie für Paketerstellung, Nachweise und Rollback-Planung nicht mehr benötigt wird und die angebotene Aktion ihren Umfang eindeutig beschreibt.

## Paketaufbewahrung auf dem Ziel

Bei frühem Abbruch wählen Sie Aufbewahrung oder Entfernung des verschlüsselten Pakets. Entschlüsseltes Zielarbeitsmaterial wird entfernt. Nach Löschen der Entschlüsselungsidentität kann ein behaltenes Paket die abgebrochene Sitzung nicht einfach fortsetzen.

Bei abgeschlossenen Migrationen gilt die Sitzungsrichtlinie; Hashes und sichere Herkunft bleiben auch nach späterer Payload-Entfernung erhalten.

## Staging-Bereinigung

Privater Test und Konvertierungsressourcen bleiben bis Annahme und erster nativer Sicherung migrationsgebunden. Danach versucht MEM automatische Bereinigung.

Bei Fehlern löschen Sie keine Container oder Verzeichnisse, bevor Migrations-ID und Eigentum bestätigt sind. Verwenden Sie die explizite Wiederholungs- oder Bereinigungsaktion, prüfen Sie die gesunde Produktionslaufzeit und bewahren Sie das Operationslog auf.

Ein Staging-Bereinigungsfehler macht den angenommenen Server nicht ungültig.

## Erfolg prüfen

Produktions-Stack gesund, erste native Sicherung im Katalog, keine öffentliche Route für Staging, Aufbewahrungsnachweis intakt und Entscheidungen zu Paket, Erfassung und Nachweisen dokumentiert.

Verwandt: [Sitzungen fortsetzen, abbrechen, archivieren und wiederherstellen](session-lifecycle.md).
