---
id: "de/migrate/assess-and-select"
translationKey: "migrate/assess-and-select"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Quelle bewerten und einen Stack auswählen"
description: "Führen Sie die schreibgeschützte Quellbewertung aus, beheben Sie Blocker und binden Sie die Migration an genau einen alten Stack."
order: 30
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Quellbewertung", "ein Stack", "Kompatibilität", "Quellauswahl"]
route: "/docs/de/migrate/assess-and-select"
aliases: []
outputPath: "docs/de/migrieren/assess-and-select.md"
preserveLegacyBranding: false
---
# Quelle bewerten und einen Stack auswählen

Der Source Assistant muss die alte Installation verstehen, bevor Daten erfasst werden dürfen.

## Ergebnis

Die Quellbewertung bestätigt ein unterstütztes MEM-0.1.0-Profil, Blocker sind geklärt und genau ein Quell-Stack ist für diese Migration ausgewählt.

## Quellbewertung ausführen

Starten Sie im Source Assistant die Bewertung. Sie prüft die alte Anwendungsdatenbank, die Docker-Laufzeit, Matrix-Stack-Dateien und weitere erforderliche Quellnachweise.

Die Bewertung ist schreibgeschützt. Sie stoppt keine Container, veröffentlicht keine Routen, verändert die alte Datenbank nicht und schreibt die Quellkonfiguration nicht um.

Prüfen Sie Klassifizierung und Empfehlung. Ein bestätigtes unterstütztes MEM-0.1.0-Ergebnis kann fortfahren. Ein wahrscheinliches, reparierbares, blockiertes oder nicht unterstütztes Ergebnis erfordert die Prüfung der Details und die Behebung des genannten Zustands vor der Erfassung.

Behandeln Sie eine Warnung nicht als Erlaubnis, einen Blocker zu umgehen. Bewahren Sie Bewertungs-ID und Quellfingerabdruck auf.

## Beabsichtigten Stack auswählen

Werden mehrere Stacks gefunden, wählen Sie beim beabsichtigten Stack **Select stack**. Prüfen Sie:

- Stack-Slug;
- Matrix-Hostname;
- Element-Hostname;
- Container- und Datenidentität;
- Quellfingerabdruck;
- erwarteten Benutzer-, Raum- und Medienumfang, soweit angezeigt.

Die Auswahl ist maßgeblich. Erfassung und Paketerstellung sind an diesen Stack gebunden; die Ziel-Control-Plane darf bei der Aufnahme keinen anderen auswählen.

## Sicherheit und Auswirkung

Die Auswahl verändert den Stack nicht. Sie definiert die Grenze der späteren Erfassung. Das Paket enthält nur die erforderlichen Matrix-Daten, Konfiguration, Signaturidentität, Medien, Element-Konfiguration und minimale Herkunft des ausgewählten Stacks.

Wählen Sie keinen Test-Stack, nur um fortzufahren. Ein Paket mit falscher Matrix-Identität muss verworfen und nach korrekter Auswahl neu erstellt werden.

## Erfolg prüfen

Sie sehen einen unterstützten Bewertungszustand, genau einen ausgewählten Stack, keinen ungelösten Erfassungsblocker und einen nach Browser-Refresh stabilen Zustand.

Ändert sich die Quelle nach der Bewertung wesentlich, führen Sie vor der Erfassung eine neue Bewertung aus.

Weiter: [Zielanfrage importieren, erfassen und Paket erstellen](create-package.md).
