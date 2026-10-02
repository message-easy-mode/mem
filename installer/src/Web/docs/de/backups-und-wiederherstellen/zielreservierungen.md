---
id: "de/backups-restores/target-claims"
translationKey: "backups-restores/target-claims"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Zielreservierungen und Hostkonflikte lösen"
description: "Verstehen Sie Vorprüfung, atomare Reservierungen, bewahrte Matrix-Identität und sichere Konfliktlösung."
order: 80
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Zielreservierung", "Hostkonflikt", "Matrix-Identität", "Vorprüfung", "Routen"]
route: "/docs/de/backups-and-restores/zielreservierungen"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/zielreservierungen.md"
preserveLegacyBranding: false
---
# Zielreservierungen und Hostkonflikte lösen

MEM koordiniert Wiederherstellungsziele, damit zwei aktive Versuche nicht denselben Stack-Slug oder öffentlichen Host beanspruchen.

## Zielwerte

- **Matrix-Serveridentität / Matrix-Host** — aus der Sicherung bewahrt;
- **Ziel-Stack-Slug** — neue MEM-Laufzeitidentität, häufig als `<quelle>-restored` vorgeschlagen;
- **Element-Host** — öffentliche Element-Adresse, die bei Verfügbarkeit wiederverwendet oder neu gewählt werden kann.

Ein Matrix-Hostkonflikt wird nicht durch eine beliebig andere Matrix-Domain gelöst. Das würde eine andere Matrix-Identität erzeugen.

## Vorprüfungen

Die schreibgeschützte Prüfung bewertet:

- Gültigkeit und Verfügbarkeit der Katalogquelle;
- Bewahrung der Matrix-Identität;
- Verfügbarkeit von Stack-Manifest und aktivem Runtime;
- Stack-Slug-Reservierung;
- Matrix-Host-Runtime und -Reservierung;
- Element-Host-Runtime und -Reservierung;
- Routeneigentum und TURN-Anforderungen.

Ein bereites Ergebnis ist keine Reservierung. Die Ausführung prüft erneut und reserviert atomar.

## Häufige Konflikte

### Stack-Slug vorhanden

Anderen Ziel-Slug wählen. Löschen Sie keinen fremden Stack nur für eine erfolgreiche Vorprüfung.

### Matrix-Adresse noch aktiv

Alten Runtime über den unterstützten Lebenszyklus stoppen oder entfernen und bestätigen, dass die öffentliche Route den Matrix-Host nicht mehr besitzt. Alten Server für Backup- und Rückfallentscheidung erhalten, aber nicht parallel öffentlich betreiben.

### Host reserviert

Anderen aktiven Wiederherstellungsarbeitsbereich öffnen und Eigentum klären. Ein sicherer Abbruch gibt temporäre Reservierungen frei, sofern kein Vorgang läuft oder wartet.

### Element-Host belegt

Anderen genehmigten Element-Host wählen oder die konfliktbehaftete Route über ihren Stack-Lebenszyklus entfernen. NPM nicht ohne dokumentierten Notfallweg direkt bearbeiten.

## Reservierungsnachweise

Die Registerkarte **Konfiguration** zeigt Ressourcentyp, Wert, Status, Reservierungszeit, Freigabezeit und Freigabegrund.

Ein fertig erstellter Produktions-Stack wird durch Abbruch eines alten Arbeitsbereichs nicht gelöscht. Danach gelten normale Stack-Abläufe.
