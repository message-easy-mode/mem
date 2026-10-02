---
title: Rollback-Grenzen verstehen
description: Verstehen Sie, was MEM vor und nach der Routenumstellung zurücknehmen kann und warum der final eingefrorene Pfad stärkere Rollback-Nachweise bietet.
section: Von MEM 0.1.0 migrieren
order: 100
---

# Rollback-Grenzen verstehen

Rollback ist kein universeller Knopf. Die mögliche Aktion hängt von der Routenumschaltung, dem gewählten Quellautoritätspfad und bereits erzeugten neuen Zieldaten ab.

## Ergebnis

Sie unterscheiden sicheren Abbruch vor der Umschaltung, Ziel-Routenwiederherstellung und vollständige Quellwiederherstellung und verstehen den Datenverlust beim Rückwechsel.

## Vor öffentlicher Umschaltung

Vor der Routenveröffentlichung sind Kandidat und normale Ziel-Laufzeit privat. Abbruch oder Bereinigung auf dem Ziel verändert oder löscht die alte Quelle nicht. Dies ist der sicherste Stoppunkt.

## Nach Routenveröffentlichung

MEM kann bei fehlgeschlagener Route oder autorisiertem erweitertem Rollback den exakten vorherigen Nginx-Proxy-Manager-Zustand wiederherstellen. Dies verändert nur die Ziel-Routenhoheit. Die Ziel-Control-Plane verbindet sich nicht mit dem alten Host, um ihn zu starten, aufzutauen oder zu verändern.

Kann die Wiederherstellung nicht bewiesen werden, gilt die Routenhoheit als unbekannt. Prüfen Sie die Nachweise vor weiteren Aktionen.

## Normaler Snapshot-Pfad

Der normale geführte Pfad verwendet Paket und privaten Test als Snapshot. Er beweist nicht formal, dass die Quelle nach der Erfassung eingefroren blieb.

Ein Rückwechsel kann alle nach dem Snapshot auf dem Ziel entstandenen Nachrichten, Konto- und Mitgliedschaftsänderungen, Medien und Schlüsselereignisse verlieren. Das Ziel synchronisiert sie nicht zurück.

## Final eingefrorener Pfad

Der erweiterte Pfad verlangt ein finales Paket mit:

- finaler Paketart;
- `sourceFrozen=true`;
- `rehearsalOnly=false`;
- keiner unzulässigen Drift.

Ein Ziel-Rollback kann Routen wiederherstellen und das Ziel stoppen oder privatisieren, doch die Quelle muss eingefroren bleiben, bis der separate Quellwiederherstellungs-Handoff mit MEM Migrate auf dem alten Host angewendet wurde. Dies stärkt Nachweise, führt aber keine Zusammenführung neuer Zieldaten durch.

## Entscheidungsregel

- Vor Umschaltung stoppen und Nachweise erhalten.
- Nach Routenänderung ohne Zielnutzung den Routensnapshot und erweiterte Steuerung verwenden.
- Nach Zielnutzung ausdrücklich entscheiden, ob der Verlust neuer Änderungen akzeptabel ist.
- Bei unbekannter Routenhoheit zuerst diagnostizieren und nicht wiederholt hin- und herschalten.

> [!WARNING]
> Löschen Sie die alte Quelle niemals nur deshalb, weil der neue Server einmal geöffnet wurde. Bewahren Sie sie bis nach Produktionsprüfung, Annahme und Aufbewahrungszeitraum auf.

Verwandt: [Liveschaltung und Produktionsprüfung](go-live.md) sowie [Migrationsnachweise und Support](evidence-and-support.md).
