---
title: Neuen Server privat erstellen
description: Wählen Sie die Ziel-Stack-Identität, bestehen Sie Konfliktprüfungen und erstellen Sie die normale MEM-Laufzeit ohne Veröffentlichung.
section: Von MEM 0.1.0 migrieren
order: 70
---

# Neuen Server privat erstellen

Nach erfolgreichem privaten Test kann MEM den Kandidaten als normalen verwalteten Chatserver materialisieren und zunächst privat halten.

## Ergebnis

Das Ziel besitzt eine privat gesunde normale MEM-Laufzeit mit bewahrter Matrix-Identität, gewähltem Stack-Namen und Element-Hostname, aber ohne öffentliche Routenhoheit.

## Identitätsentscheidungen

Die migrierte öffentliche Matrix-Adresse ist auf die erfasste Quellidentität festgelegt. Eine Änderung würde einen anderen Matrix-Server erzeugen und gehört nicht zu dieser Migration.

Wählen und prüfen Sie:

- normalen MEM-Stack-Namen oder Slug;
- bewahrten Matrix-Hostname;
- zu veröffentlichenden Element-Hostname;
- Zieldatenbank und Laufzeiteigentum;
- Nutzung normaler gemeinsamer Ziel-Dienste und Richtlinien.

## Ziel-Vorprüfung ausführen

MEM prüft vor Änderungen Konflikte bei Stack-Name, Datenbank, Matrix- und Element-Hostnamen, Hostpfaden, Containern, Laufzeiteigentum, bestehenden öffentlichen Routen sowie aktiven Migrations- oder Wiederherstellungsansprüchen.

Lösen Sie Konflikte bewusst. Löschen Sie keinen vorhandenen Produktions-Stack oder eine Route nur für eine grüne Vorprüfung, solange Sie Obsoleszenz und separaten Wiederherstellungsweg nicht nachgewiesen haben.

## Quellautorität wählen

Der normale geführte Pfad verwendet Paket und privaten Test als maßgeblichen Snapshot. Er ist einfacher, enthält aber keine späteren Änderungen am alten Server und bietet weniger formale Einfrier- und Wiederherstellungsnachweise.

Der erweiterte **final eingefrorene** Pfad erstellt nach formellem Einfrieren der Quelle ein finales Paket. Er liefert stärkere Drift- und Rollback-Nachweise, benötigt aber zusätzliche Quellschritte. Lesen Sie [Rollback-Grenzen](rollback.md).

## Server erstellen

Schließen Sie Bestätigung und Step-up ab. MEM erstellt normale Stack-Datenbank, Matrix- und Element-Dienste, Konfiguration und Eigentumsnachweise. Öffentliche Routen werden in diesem Schritt nicht veröffentlicht.

Bestätigen Sie eine gesunde private normale Laufzeit. Fahren Sie bei unklarer öffentlicher Routenhoheit nicht fort.

## Nutzung der Quelle

Beim normalen Snapshot-Pfad beenden Sie die normale Nutzung des alten Servers vor der Liveschaltung. Beim final eingefrorenen Pfad bleibt die Quelle eingefroren. Bewahren Sie den Host in beiden Fällen bis nach Produktionsprüfung und Annahme auf.

Weiter: [Neuen Server live schalten und Produktion prüfen](go-live.md).
