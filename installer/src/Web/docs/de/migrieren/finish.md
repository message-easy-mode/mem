---
id: "de/migrate/finish"
translationKey: "migrate/finish"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Migration annehmen und abschließen"
description: "Nehmen Sie den geprüften Server an, legen Sie die Aufbewahrung des Altsystems fest, erstellen Sie die erste native Sicherung und wechseln Sie in den normalen MEM-Lebenszyklus."
order: 90
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Annahme", "Altserver-Aufbewahrung", "erste native Sicherung", "Abschluss", "Übergabe"]
route: "/docs/de/migrate/finish"
aliases: []
outputPath: "docs/de/migrieren/finish.md"
preserveLegacyBranding: false
---
# Migration annehmen und abschließen

Die Annahme ist die Grenze, an der der geprüfte MEM-0.2.0-Server zum maßgeblichen verwalteten Server wird und in den normalen Sicherungs- und Wiederherstellungslebenszyklus eintritt.

## Ergebnis

Die Migration ist dauerhaft angenommen, die alte Quelle besitzt einen Aufbewahrungszeitraum, die erste native MEM-Sicherung ist erstellt oder gezielt wiederholbar und die Staging-Bereinigung kann abgeschlossen werden.

## Voraussetzungen

Schließen Sie erst ab, wenn öffentliche Routenhoheit bekannt ist, die Produktionsprüfung bestanden und gültig ist, Matrix-Identität und Dienst korrekt sind, Betreiber fehlende Rücksynchronisation verstehen und die Quelle für den gewählten Zeitraum erhalten bleiben kann.

## Aufbewahrung der alten Quelle wählen

Wählen Sie die Dauer. Der Eintrag ist ein betrieblicher Sicherheitsnachweis und keine Anweisung an MEM, den Quellhost zu löschen. Halten Sie ihn gemäß Rollback-Plan ausgeschaltet, isoliert oder kontrolliert, bewahren Sie Daten und Nachweise jedoch auf.

## Migrierten Server annehmen

Schließen Sie Bestätigung und Step-up ab. Die Annahme zeichnet das Ziel als maßgeblich auf und schließt die normale Übernahmegrenze.

Danach ist die neue MEM-Laufzeit Produktion, neue Nachrichten und Kontoänderungen werden nicht zurückkopiert, die Quelle wird niemals automatisch gelöscht und Rohartefakte bleiben Migrationsnachweise.

## Erste native Sicherung

Der Abschluss startet die erste native MEM-Sicherung. Erst an dieser Grenze tritt der migrierte Server in Sicherungskatalog und Wiederherstellungsarbeitsbereich ein.

Scheitert die Basissicherung nach der Annahme, bleibt die Annahme dauerhaft. Wiederholen Sie nicht die Annahme, sondern nur die erste native Sicherung über die angebotene Aktion.

## Abschluss und Bereinigung

Laden oder bewahren Sie den Abschlussbericht auf. MEM versucht danach, erfolgreiche Staging-Ressourcen zu entfernen. Ein Bereinigungsfehler ist eine Folgeoperation und macht Annahme oder erfolgreiche Sicherung nicht rückgängig.

Prüfen Sie angenommenen/abgeschlossenen Status, normalen Chatserver-Arbeitsbereich, ersten Sicherungskatalog-Eintrag, sichtbaren Aufbewahrungsnachweis und abgeschlossene oder sicher wiederholbare Staging-Bereinigung.

Weiter: [Migrationsressourcen aufbewahren oder bereinigen](cleanup.md).
