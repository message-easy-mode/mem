---
id: "de/migrate/session-lifecycle"
translationKey: "migrate/session-lifecycle"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Sitzungen fortsetzen, abbrechen, archivieren und wiederherstellen"
description: "Verwalten Sie dauerhafte Migrationssitzungen, ohne Nachweise zu löschen oder Archivierung mit Abbruch zu verwechseln."
order: 120
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Migrationssitzung", "fortsetzen", "abbrechen", "archivieren", "dauerhaft löschen"]
route: "/docs/de/migrate/session-lifecycle"
aliases: []
outputPath: "docs/de/migrieren/session-lifecycle.md"
preserveLegacyBranding: false
---
# Sitzungen fortsetzen, abbrechen, archivieren und wiederherstellen

Migrationssitzungen sind dauerhaft. Das Verlassen der Seite oder ein Browser-Neustart verwandelt eine laufende Operation nicht in eine neue Migration.

## Ergebnis

Sie kehren zu bestehenden Sitzungen zurück, brechen nur sicher ab, archivieren terminale Historie, stellen sie in der Liste wieder her und verstehen die eingeschränkte dauerhafte Löschung.

## Nach Refresh oder Unterbrechung fortsetzen

Öffnen Sie **Migrationen**, suchen Sie die bestehende Sitzung und fahren Sie an ihrem aktuellen Schritt fort. Erfassung, Konvertierung, Staging, Zielerstellung, Umschaltung, Sicherung und Bereinigung sind serverseitige Operationen. Prüfen Sie den Zustand, bevor Sie etwas erneut starten.

Erstellen Sie keine zweite Aufnahme nur wegen geschlossener Seite oder abgelaufener Vorschau. Aktualisieren Sie Nachweis oder Bereitschaft in derselben Sitzung.

## Frühe Sitzung abbrechen

Abbruch ist nur verfügbar, solange das Ziel serverseitig als wirklich entbehrlich gilt. Die Prüfung verlangt eine ausdrückliche Wahl zur Aufbewahrung des verschlüsselten Pakets und die Bestätigung, dass die Quelle außerhalb der Zielkontrolle liegt.

Der Zielabbruch verändert oder löscht die Quelle nicht, entfernt entschlüsseltes Arbeitsmaterial, löscht die Ziel-Entschlüsselungsidentität, erhält redigiertes Audit und sichere Hashes und behält oder entfernt das verschlüsselte Paket gemäß Auswahl.

Ein behaltenes Paket kann nach Löschen der Identität die abgebrochene Sitzung nicht fortsetzen.

## Archivieren und in aktive Liste zurückstellen

Terminale Sitzungen können archiviert werden. Archivierung ändert die Sichtbarkeit, nicht Ressourcen oder Nachweise. Verwenden Sie den Archivfilter und **In aktive Liste zurückstellen**, wenn normale Sichtbarkeit wieder benötigt wird.

Archivierung ist weder Abbruch noch dauerhafte Löschung.

## Dauerhaft löschen

Dauerhafte Löschung ist auf serverseitig nachgewiesene entbehrliche frühe Sitzungen beschränkt und verlangt die genaue Migrations-ID. Sitzungsdatensatz, Paketrevisionen, Validierungshistorie und verifizierte Zielpaketreste können entfernt werden; ein redigierter Auditdatensatz bleibt.

Nicht entbehrlich sind Sitzungen mit Konvertierung, Staging, Ziel-Laufzeit, Produktionsrouten, Annahme, erster nativer Sicherung, Sicherungskatalog-/Restore-Herkunft, Quellqualifizierung oder Altserver-Aufbewahrung.

Angenommene oder abgeschlossene Sitzungen und ihre Grenze der ersten nativen Sicherung müssen erhalten bleiben.

## Aktion prüfen

Bestätigen Sie nach jeder Lebenszyklusaktion Filter, Status, Archivzustand und angezeigte Folgen. Schließen Sie aus einer fehlenden Zeile in der Standardliste niemals auf Löschung.

Verwandt: [Migrationsnachweise, Logs und Supportinformationen verwenden](evidence-and-support.md).
