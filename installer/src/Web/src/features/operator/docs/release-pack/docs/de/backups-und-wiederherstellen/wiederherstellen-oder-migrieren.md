---
title: Wiederherstellen, migrieren oder reparieren?
description: Wählen Sie den richtigen Ablauf für native Recovery, MEM-0.1.0-Übernahme, Hostwechsel und Konfigurationsreparatur.
section: Sichern und wiederherstellen
order: 130
---

# Wiederherstellen, migrieren oder reparieren?

Backup/Restore und Migration sind getrennte Bereiche in MEM.

## Wiederherstellung verwenden

Verwenden Sie Sicherungskatalog und Wiederherstellungsarbeitsbereich bei einem unterstützten nativen MEM-Payload, wenn ein Matrix- und Element-Stack mit derselben gesicherten Matrix-Identität neu erstellt werden soll.

Typische Fälle:

- Host- oder Datenträgerverlust;
- beschädigter oder entfernter MEM-0.2.x-Stack;
- Übertragung eines nativen portablen Backups auf eine kompatible MEM-Installation;
- Recovery-Nachweis durch privaten Test.

## MEM Migrate verwenden

MEM Migrate ist für ältere oder unterschiedliche Quellen gedacht, die bewertet, erfasst, konvertiert, privat bereitgestellt, übernommen oder umgeschaltet werden müssen.

Für MEM 0.2.0 ist das primäre unterstützte Quellprofil die definierte MEM-0.1.0-Installation. Ein alter Server mit `mem-api` und `mem-web` wird nicht durch den Import eines beliebigen Verzeichnisses als Backup konvertiert.

Migration besitzt Quellbewertung, Paketerfassung, Kompatibilität, Transformation, privates Staging, Cutover, Abnahme, Rückfallplanung und Legacy-Aufbewahrung. Erst nach Abnahme gelangt der migrierte Stack durch eine native Basissicherung in den normalen Sicherungskatalog.

## Normale Reparatur verwenden

Wenn der Runtime noch existiert und das Problem eine begrenzte Konfigurations-, Routen-, Zertifikats-, TURN-, Föderations- oder Containerstörung ist, verwenden Sie den zuständigen Stack-, Plattform- oder Diagnoseablauf.

Stellen Sie nicht nur wegen einer einzelnen NPM-Route oder eines temporären Docker-Ausfalls wieder her. Eine Wiederherstellung erstellt einen neuen Produktions-Stack und kann mehr Risiko verursachen als eine gezielte Reparatur.

## Entscheidungsfragen

1. Ist die Quelle bereits ein unterstützter Katalogeintrag?
2. Muss die Matrix-Identität exakt gleich bleiben?
3. Benötigt die Quelle Konvertierung oder Kompatibilitätsbewertung?
4. Ist der aktuelle Runtime noch intakt und reparierbar?
5. Bedient ein alter öffentlicher Homeserver noch dieselbe Matrix-Identität?
6. Gibt es ein getestetes externes Backup und einen Plan für Benutzerschlüssel?

Bei Unklarheit vor Produktionsmutation stoppen, Quelle erhalten, Diagnosen sammeln und den Ablauf wählen, der das tatsächliche Problem besitzt.
