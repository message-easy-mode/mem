---
title: Wiederherstellungsmaterial sicher löschen und aufbewahren
description: Unterscheiden Sie Katalog-Payload, Original-Upload, portable Exporte, privates Staging, Arbeitsbereich und Produktions-Stack.
section: Sichern und wiederherstellen
order: 120
---

# Wiederherstellungsmaterial sicher löschen und aufbewahren

MEM trennt Wiederherstellungsobjekte, damit die Löschung eines Objekts nicht fälschlich als vollständige Bereinigung verstanden wird.

## Getrennte Lebenszyklen

- **Katalogeintrag und Payload** — Quelle für Restore-Aktionen.
- **Original-Upload-ZIP** — Aufnahme- und Herkunftsmaterial.
- **Portable Exporte** — erzeugte Downloads eines Katalogeintrags.
- **Privates Staging** — wegwerfbare Container, Netz und Arbeitsbereich.
- **Wiederherstellungsarbeitsbereich** — Versuch, Reservierungen, Vorgänge, Logs, Nachweise und Supportverlauf.
- **Wiederhergestellter Produktions-Stack** — normal verwalteter Stack nach Standard-Neuerstellung.

## Original-ZIP löschen

Die Löschung des Original-Uploads löscht nicht Katalog-Payload, Arbeitsbereiche, Logs, Nachweise, Supportberichte oder Produktions-Stack. Verwenden Sie dies, wenn der Transport-Upload nicht mehr benötigt wird, die Wiederherstellungsquelle aber erhalten bleiben soll.

## Privates Staging entfernen

**Privaten Test entfernen** löscht Wegwerf-Container, internes Netz und privaten Arbeitsbereich. Das sichere historische Testergebnis bleibt sichtbar.

## Katalogeintrag permanent löschen

Die permanente Löschung ist nur ohne aktiven Restore-Blocker möglich und kann entfernen:

- verwalteten Payload;
- verknüpften Original-Upload;
- serverseitige portable Exporte;
- Katalogverknüpfungen historischer Restore-Versuche.

Die Aktion ist irreversibel. Eine heruntergeladene externe Kopie liegt außerhalb von MEM.

Historische Arbeitsbereiche können nach Quellenlöschung lesbar bleiben, aber geführte Aktionen werden blockiert.

## Fertige Produktions-Stacks

Abbruch oder Löschung eines Arbeitsbereichs entfernt keinen fertig erstellten Stack. Verwalten Sie ihn über normale Stack-Abläufe und erstellen Sie vor Entfernung eine neue Sicherung.

## Empfohlene Aufbewahrung

Mindestens behalten:

- mehrere aktuelle lokale Sicherungen;
- mindestens einen getesteten externen Export;
- die Sicherung vor einer Änderung bis zur stabilen Bestätigung;
- Restore-Nachweise und Supportberichte entsprechend Ihrer Richtlinie.

Testen Sie Wiederherstellungen regelmäßig. Aufbewahrung ohne Wiederherstellungsnachweis bleibt eine Annahme.
