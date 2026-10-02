---
id: "de/backups-restores/verify-and-complete"
translationKey: "backups-restores/verify-and-complete"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Wiederhergestellten Server prüfen und übergeben"
description: "Führen Sie öffentliche Bereitschaftsprüfungen aus, kontrollieren Sie Nachweise und schließen Sie die Übergabe ab."
order: 90
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Prüfung", "Übergabe", "Matrix", "Element", "Doctor"]
route: "/docs/de/backups-and-restores/pruefen-und-abschliessen"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/pruefen-und-abschliessen.md"
preserveLegacyBranding: false
---
# Wiederhergestellten Server prüfen und übergeben

Die Standard-Neuerstellung erstellt den Stack. Der Arbeitsbereich bleibt aktiv, bis öffentliche Prüfungen bestehen und der Betreiber die Übergabe bestätigt.

## Prüfungen ausführen

Unter **Wiederhergestellten Server prüfen** eine aktuelle Prüfung starten. MEM verwendet den Doctor des registrierten Stacks und projiziert sichere Matrix-, Element-, Routen- und Konnektivitätsergebnisse in den Arbeitsbereich.

Eine gespeicherte URL ist kein Erreichbarkeitsnachweis. Nach DNS-, NPM-, Zertifikats- oder Netzwerkänderungen erneut prüfen.

## Ergebnis kontrollieren

Bestätigen Sie:

- Stack ist unter **Chatserver** registriert;
- Matrix- und Element-Container sind gesund;
- öffentliche Routen zeigen auf das beabsichtigte Ziel;
- öffentliche Bereitschaftsprüfungen bestehen;
- Matrix-Serveridentität entspricht der Sicherung;
- erwartete Benutzer sind nach Synchronisierung sichtbar;
- benötigte Medien sind vorhanden;
- Föderationsrichtlinie und TURN-Zustand entsprechen dem Plan.

Melden Sie sich zusätzlich mit einem bekannten Matrix-Konto bei Element an. Prüfen Sie Raumverlauf, neue Nachrichten, Medien sowie gegebenenfalls Föderation und Sprache/Video. Automatische Prüfungen beweisen keine benutzerseitigen Verschlüsselungsschlüssel.

## Übergabe abschließen

1. **Abschließen und übergeben** erweitern.
2. Letzte Prüfung und Warnungen lesen.
3. Abschluss bestätigen.
4. Wiederhergestellten Stack aus dem Arbeitsbereich öffnen.
5. Restore-ID im Change- oder Incident-Datensatz behalten.

Die Übergabe markiert den Arbeitsbereich als abgeschlossen. Quelle, privater Testverlauf, Logs, Nachweise und Supportbericht werden nicht gelöscht.

## Danach

- Neue Sicherung des wiederhergestellten Stacks erstellen.
- Katalogeintrag prüfen.
- Aufbewahrung von alter Quelle und gestopptem Runtime entscheiden.
- Benutzer, Föderation, TURN, Speicher und Diagnosen im normalen Stack-Arbeitsbereich prüfen.

Ein abgeschlossener Arbeitsbereich kann nicht abgebrochen werden.
