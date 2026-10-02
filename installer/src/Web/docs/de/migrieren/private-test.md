---
id: "de/migrate/private-test"
translationKey: "migrate/private-test"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Konvertieren und privaten Test ausführen"
description: "Konvertieren Sie das alte Payload in einen migrationsgebundenen Kandidaten und prüfen Sie ihn in privaten Matrix- und Element-Laufzeiten."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Konvertierung", "privater Test", "Staging", "Kandidat", "keine öffentlichen Routen"]
route: "/docs/de/migrate/private-test"
aliases: []
outputPath: "docs/de/migrieren/private-test.md"
preserveLegacyBranding: false
---
# Konvertieren und privaten Test ausführen

Das Ziel konvertiert das validierte alte Payload in einen migrationsgebundenen Kandidaten, bevor der normale Produktionsserver erstellt wird.

## Ergebnis

Die Konvertierung ist erfolgreich und der Kandidat läuft als private Matrix- und Element-Dienste ohne öffentliche Routen. Der Betreiber bestätigt Startfähigkeit und erwartete Daten.

## Konvertierung ausführen

Starten Sie unter **Vorbereiten und testen** die Konvertierung. MEM verwendet den aktuellen Worker-Vertrag, validiert strukturierte Fortschrittsereignisse und Ausgabe-Hashes und zeichnet einen dauerhaften Versuch auf.

Die Konvertierung kann dauern und läuft serverseitig weiter, wenn Sie die Seite verlassen oder aktualisieren. Kehren Sie zur selben Migrationssitzung zurück und prüfen Sie Fortschritt, Logs, Warnungen und Ergebnis.

Eine fehlgeschlagene Konvertierung erstellt keinen normalen Chatserver. Beheben Sie das gemeldete Problem oder erzeugen Sie ein kompatibles neues Paket, statt Teilergebnisse in einen Stack zu kopieren.

## Grenze des migrationsgebundenen Kandidaten

Ein erfolgreicher Kandidat ist kein Sicherungskatalog-Eintrag, kein Wiederherstellungsarbeitsbereich, keine normale verwaltete Laufzeit und nicht öffentlich erreichbar.

Rohpakete, temporäre Konvertierungsdateien, fehlgeschlagene Kandidaten und nicht angenommene Kandidaten bleiben im Migrationskontext.

## Privaten Test starten

Erstellen Sie die private Staging-Laufzeit. MEM importiert die konvertierte Datenbank, startet private Matrix- und Element-Dienste und prüft sie in einem internen Docker-Netzwerk.

Der private Test darf **keine öffentlichen Matrix- oder Element-Routen** erstellen und keine Produktionshostnamen übernehmen.

Prüfen Sie:

- Datenbankimport und Schema;
- Synapse-Start und Gesundheit;
- Element-Konfiguration und privaten Zugriff;
- erwartete Quellidentität;
- angezeigte repräsentative Benutzer-, Raum-, Ereignis- und Mediennachweise;
- fehlende öffentliche Routenhoheit.

## Fehlerbehandlung

Prüfen Sie bei Fehlern Konvertierungs- und Staging-Logs vor einem neuen Versuch. Fahren Sie nicht mit der Zielerstellung fort, solange die private Laufzeit ungesund oder ihre Identität unsicher ist.

Ein Refresh bricht keine laufende Staging-Operation ab. Verwenden Sie explizite Operationssteuerung, sofern Abbruch unterstützt wird.

## Aufbewahrungsgrenze

Kandidat und Staging bleiben migrationsgebunden. MEM bewahrt sie normalerweise bis Annahme und erster nativer Sicherung auf und versucht danach die Bereinigung. Ein Bereinigungsfehler kann erneut versucht werden, ohne die Annahme ungültig zu machen.

Weiter: [Neuen Server privat erstellen](create-new-server.md).
