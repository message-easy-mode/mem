---
id: "de/backups-restores/cancel-retry-failure"
translationKey: "backups-restores/cancel-retry-failure"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Wiederherstellung abbrechen, wiederholen und Fehler behandeln"
description: "Verwenden Sie serverseitig freigegebenen Abbruch, bewahren Sie Nachweise und vermeiden Sie unsichere Bereinigung während laufender Vorgänge."
order: 100
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Wiederherstellung abbrechen", "Wiederholen", "Fehler", "Bereinigung", "Audit"]
route: "/docs/de/backups-and-restores/abbruch-und-fehler"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/abbruch-und-fehler.md"
preserveLegacyBranding: false
---
# Wiederherstellung abbrechen, wiederholen und Fehler behandeln

Eine fehlgeschlagene Wiederherstellung bleibt nur dann kontrollierbar, wenn Quelle und Nachweise erhalten bleiben und klar ist, welche Änderungen bereits abgeschlossen wurden.

## Sicher abbrechen

**Abbrechen** erscheint nur, wenn der Server den Abbruch als sicher meldet.

Abbruch ist nicht verfügbar, wenn:

- der Versuch bereits abgeschlossen, abgebrochen oder anderweitig terminal ist;
- ein Datei-, Datenbank-, Docker- oder Routenvorgang wartet oder läuft.

MEM bricht keine laufende Mutation mittendrin ab. Warten Sie auf Abschluss oder Fehler und aktualisieren Sie den Arbeitsbereich.

Ein erfolgreicher Abbruch:

- schließt den nicht terminalen Arbeitsbereich;
- gibt temporäre Zielreservierungen frei;
- bewahrt Sicherung, Logs, Nachweise, Supportmaterial und Auditverlauf;
- löscht keinen fertig erstellten Stack.

## Wiederholen

Nach Timeout nicht mehrfach klicken. Zuerst aktualisieren und dauerhaften Vorgangsstatus prüfen. Der Server kann die Arbeit abgeschlossen haben, obwohl der Browser keine Antwort erhielt.

Vor einem erneuten Versuch:

1. Stufenzusammenfassung und Blocker lesen.
2. **Aktivität**, **Nachweise** und **Logs** prüfen.
3. Vorgangs-ID und Ereigniscode notieren.
4. Prüfen, ob Datenbank, Container, Routen oder Reservierungen erstellt wurden.
5. Verfügbare geführte Bereinigung für fehlgeschlagene Standard-Neuerstellung verwenden.
6. Vorprüfung erneut ausführen.

## Quellen- und Zielfehler

Quellenfehler sind etwa fehlende Dateien, ungültige Prüfsummen, fehlende Signaturidentität oder fehlerhafter Datenbankimport. Zielfehler sind belegte Slugs/Hosts, nicht verfügbare Plattformdienste, Routeneigentum oder Startfehler.

Bearbeiten Sie keinen Katalog-Payload direkt. Erstellen Sie eine neue geprüfte Sicherung oder importieren Sie das Originalarchiv erneut.

## Kein automatischer Rollback

Die Standard-Neuerstellung verspricht keinen automatischen Rollback. Ein Fehler kann teilweise Produktionsressourcen hinterlassen. Löschen Sie keine Datenbanken, MEM-Verzeichnisse, Netze oder NPM-Routen allein aufgrund einer Fehlermeldung. Stellen Sie zuerst Eigentum aus Konfiguration, Aktivität und Nachweisen fest.

Vor Host-Bereinigung einen [Supportbericht](nachweise-logs-support.md) erzeugen und Restore-ID, Katalog-ID, Zielwerte, Fehlercode, Vorgangs-IDs und Bereinigungsergebnis behalten.
