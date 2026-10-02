---
title: Migration, Sicherung und Wiederherstellung oder Reparatur wählen
description: Wählen Sie den richtigen MEM-Ablauf und verstehen Sie, wann Migrationsmaterial zu einer normalen Quelle im Sicherungskatalog wird.
section: Von MEM 0.1.0 migrieren
order: 140
---

# Migration, Sicherung und Wiederherstellung oder Reparatur wählen

Migration und Sicherung/Wiederherstellung teilen Validierungs- und Laufzeitfähigkeiten, sind aber getrennte Betreiberabläufe mit unterschiedlichen Quellen und Nachweisen.

## Ergebnis

Sie wählen den richtigen Ablauf und stellen nicht angenommenes Migrationsmaterial nicht in den Sicherungskatalog.

## Migration verwenden

- Quelle ist eine unterstützte ältere MEM-0.1.0-Installation.
- Quelle muss bewertet und konvertiert werden, bevor sie ein normaler MEM-0.2.0-Stack wird.
- Source-Assistant-Erfassung, verschlüsselte Aufnahme, privates Staging, geführte Routenumstellung und Annahme sind erforderlich.

Migration besitzt Rohpakete, Konvertierungsversuche, Kandidaten, Staging, Produktionsübernahme, Rollback-Nachweise und Quellaufbewahrung.

## Sicherung und Wiederherstellung verwenden

- Quelle ist ein nativer MEM-Sicherungskatalog-Eintrag oder unterstützter portabler MEM-Export.
- Ein verwalteter MEM-Stack wird aus einem nativen Recovery-Payload wiederhergestellt oder neu erstellt.
- Keine Quellbewertung oder Konvertierung ist nötig.

Ein Wiederherstellungsarbeitsbereich akzeptiert kein Roh-Migrationspaket.

## Normale Reparatur verwenden

- Der bestehende verwaltete Stack bleibt der beabsichtigte Server.
- Das Problem betrifft Container, Zertifikat, DNS, TURN, Föderation, Speicher, Konten oder Laufzeitgesundheit.
- Weder Neuerstellung noch Übernahme einer anderen Quelle ist nötig.

Verwenden Sie Stack-Diagnose und normale Operationen.

## Annahmegrenze

Vor der Annahme bleiben verschlüsseltes Paket, validiertes Quellarchiv, Konvertierungsausgabe, fehlgeschlagener oder temporärer Kandidat, private Staging-Laufzeit und nicht angenommene normale Ziel-Laufzeit ausschließlich Migrationsmaterial. Sie dürfen nicht als normale Katalogeinträge erscheinen.

Nach Produktionsprüfung und Annahme erstellt MEM die erste native Sicherung. Erst dann treten Stack und Sicherung in den normalen Sicherungskatalog- und Wiederherstellungslebenszyklus ein.

## Entscheidungstabelle

| Situation | Richtiger Ablauf |
|---|---|
| Unterstütztes MEM 0.1.0 nach MEM 0.2.0 verschieben | Migration |
| MEM 0.2.0 aus nativer portabler Sicherung wiederherstellen | Sicherung und Wiederherstellung |
| Sicherungskatalog-Quelle privat testen | Privater Test im Wiederherstellungsarbeitsbereich |
| TURN, Föderation, DNS oder gestoppten Container reparieren | Normale Operation oder Diagnose |
| Beliebigen Synapse-Server importieren | Von diesem Migrationsleitfaden nicht unterstützt |

Verwandt: [Chatserver sichern und wiederherstellen](../backups-und-wiederherstellen/index.md).
