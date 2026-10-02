---
title: Neuen Server live schalten und Produktion prüfen
description: Prüfen Sie die Routenhoheit, veröffentlichen Sie die neuen Matrix- und Element-Routen und verifizieren Sie den öffentlichen Produktionsdienst.
section: Von MEM 0.1.0 migrieren
order: 80
---

# Neuen Server live schalten und Produktion prüfen

Die Liveschaltung überträgt die öffentlichen Proxy-Routen vom alten Dienstpfad auf die neue MEM-0.2.0-Laufzeit. Dies ist der folgenreichste Schritt des geführten Ablaufs.

## Ergebnis

Die neuen Matrix- und Element-Dienste besitzen die vorgesehenen öffentlichen Routen und bestehen die dauerhafte Produktionsprüfung. Die alte Quelle bleibt für Rollback und Annahme erhalten.

## Bevor Sie beginnen

Bestätigen Sie:

- neue normale Laufzeit privat gesund;
- Matrix-Hostname korrekt und unverändert;
- Element-Hostname korrekt;
- DNS zeigt bereits auf den Ziel-Ingress;
- aktives Zielzertifikat deckt die Hostnamen ab;
- aktueller Eigentümer der Nginx-Proxy-Manager-Routen ist bekannt;
- normale Nutzung des alten Servers ist gestoppt oder die Quelle final eingefroren;
- [Rollback-Grenzen](rollback.md) sind gelesen.

MEM ändert Nginx-Proxy-Manager-Routen. Es erstellt keine DNS-Einträge und verspricht während der Umschaltung kein fehlendes Zertifikat.

## Bereitschaft prüfen

Der Arbeitsbereich erstellt ein zeitlich begrenztes Bereitschafts- oder Vorschauergebnis. Prüfen Sie Blocker und Warnungen unmittelbar vor der Anwendung. Aktualisieren Sie eine abgelaufene Vorschau oder einen nach Zustandsänderung veralteten Nachweis.

Schließen Sie bei Bedarf Step-up ab.

## Routen veröffentlichen

MEM sichert den vorherigen Routenzustand, wählt das aktive Zertifikat und wendet die Matrix- und Element-Routen für die neue Laufzeit an.

Scheitert die Veröffentlichung, versucht MEM den vorherigen Zustand wiederherzustellen. Bei erfolgreicher Wiederherstellung bleibt der neue Server privat. Kann die Routenhoheit nicht bestätigt werden, stoppen Sie normale Wiederholungen und bestimmen Sie anhand erweiterter Wiederherstellungsnachweise, welche Laufzeit öffentlich ist.

## Produktion prüfen

Führen Sie die angezeigten Produktionsprüfungen aus. Prüfen Sie mindestens:

- Zielcontainer und Laufzeiteigentum;
- Nginx-Proxy-Manager-Ziele und Zertifikatsauswahl;
- öffentliche Matrix-Bereitschaft;
- öffentliche Element-Erreichbarkeit;
- Datenbank- und Migrationsnachweise;
- Warnungen zur Quellautorität oder unvollständigen Prüfungen.

## Grenze bei fehlgeschlagener Prüfung

Eine Routenänderung kann erfolgreich sein, obwohl eine spätere Prüfung scheitert. MEM führt nicht allein deshalb automatisch Rollback aus, weil dadurch ein erreichbarer neuer Server durch eine unsichere alte Route ersetzt werden könnte.

Bestimmen Sie zuerst die öffentliche Laufzeit. Wiederholen Sie sichere Prüfungen oder verwenden Sie erweiterte Wiederherstellungssteuerung. Drücken Sie Liveschaltung nicht wiederholt bei unbekannter Routenhoheit.

Weiter: [Migration annehmen und abschließen](finish.md).
