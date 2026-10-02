---
id: "de/backups-restores/backup-catalog"
translationKey: "backups-restores/backup-catalog"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Sicherungskatalog verstehen"
description: "Lesen Sie Identität, Herkunft, Payload-Zustand, Integrität, Hinweise und Lebenszyklus einer Wiederherstellungsquelle."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Sicherungskatalog", "Herkunft", "Integrität", "Payload", "Lebenszyklus"]
route: "/docs/de/backups-and-restores/sicherungskatalog"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/sicherungskatalog.md"
preserveLegacyBranding: false
---
# Sicherungskatalog verstehen

Unter **Sicherungen** sehen Sie alle MEM bekannten, wiederherstellbaren Quellen. Lokale Erfassungen und importierte ZIPs verwenden denselben Katalog und denselben Wiederherstellungsablauf.

## Herkunft und Identität

Jeder Eintrag besitzt eine stabile Katalog-ID und eine Herkunft:

- `local-captured` — direkt von einem verwalteten Stack erfasst;
- `imported-zip` — aus einem validierten portablen MEM-Export materialisiert.

Die Detailseite kann Quell-Stack, Sicherungs-ID, Upload-Validierungs-ID, Manifestversion, MEM-Version, Matrix-Servername, Matrix-Host, Element-Host sowie Erfassungs- und Importzeit anzeigen.

## Payload-Zustand

- `available` — der Payload kann für die Wiederherstellung aufgelöst werden;
- `materialising` — ein validiertes Archiv wird in Katalogspeicher kopiert;
- `failed` — Materialisierung oder Vorbereitung ist fehlgeschlagen;
- `removed` — der Payload wurde bewusst gelöscht.

Ein Katalogdatensatz kann nach Payload-Löschung lesbar bleiben, damit Herkunft und Auditverlauf erhalten bleiben.

## Integrität und Hinweise

Integrität kann `valid`, `warning`, `invalid` oder `unknown` sein.

Importierte Archive können zusätzlich **Hinweise** enthalten. Diese sind nicht blockierende Betreiberinformationen, etwa zum sicheren Umgang mit Signaturmaterial, zur Identitätsgefahr durch einen alten Server oder zu Routen- und TURN-Snapshots. Sie sind bewusst von Struktur- oder Prüfsummenfehlern getrennt.

Lesen Sie Integritätszusammenfassung und alle Hinweise, auch wenn die Wiederherstellungsaktion verfügbar ist.

## Suche und Filter

Der Katalog kann nach Text, Herkunft, Stack und Sortierung gefiltert werden. Verlassen Sie sich nicht nur auf den Anzeigenamen. Ähnliche Einträge können unterschiedliche Sicherungs-IDs, Zeiten, Zustände oder Matrix-Identitäten besitzen.

## Grenze zur Wiederherstellungssitzung

Bei einem verfügbaren Eintrag **Wiederherstellen** wählen. MEM erstellt oder öffnet einen dauerhaften Wiederherstellungsarbeitsbereich unter `/restores/<restoreSessionId>`.

Der Katalogeintrag bleibt die Quelle. Die Wiederherstellungssitzungs-ID ist die Arbeitsbereichsidentität. Eine Upload-Validierungs-ID ist keines von beidem.

## Lebenszyklus

Eine aktive Wiederherstellung blockiert die permanente Löschung ihrer Quelle. Die Detailseite zeigt Payload-Verfügbarkeit, aktiven Arbeitsbereich und Löschblocker.

Die permanente Kataloglöschung ist irreversibel und kann Payload, Original-Upload, erzeugte portable Exporte sowie Katalogverknüpfungen älterer Versuche entfernen. Lesen Sie vorher [Wiederherstellungsmaterial sicher löschen und aufbewahren](loeschen-und-aufbewahren.md).
