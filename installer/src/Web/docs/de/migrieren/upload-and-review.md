---
id: "de/migrate/upload-and-review"
translationKey: "migrate/upload-and-review"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Paket hochladen und alten Server prüfen"
description: "Laden Sie das verschlüsselte Paket nach MEM 0.2.0 hoch, validieren Sie es und prüfen Sie Quellidentität und Warnungen vor der Konvertierung."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Paket-Upload", "Validierung", "alter Server", "Kompatibilität", "Step-up"]
route: "/docs/de/migrate/upload-and-review"
aliases: []
outputPath: "docs/de/migrieren/upload-and-review.md"
preserveLegacyBranding: false
---
# Paket hochladen und alten Server prüfen

Kehren Sie mit dem für diese Aufnahme erstellten verschlüsselten Paket zur MEM-0.2.0-Control-Plane zurück.

## Ergebnis

Das Ziel hat das Paket angenommen und validiert, die Migrationssitzung an den einzelnen Quell-Stack gebunden und Identität, Kompatibilität und Warnungen des alten Servers vor der Konvertierung angezeigt.

## Paket hochladen

Öffnen Sie die passende Migrationsaufnahme im Schritt **Paket erstellen und hochladen** und wählen Sie den Paket-Upload, schließen Sie gegebenenfalls Step-up ab und wählen Sie die Datei `.memmigration.zip.age`.

MEM prüft Aufnahmebindung, Empfängeridentität, Paketstruktur, Archivinhalte, Quellprofil, ausgewählte Stack-Identität und unterstützte Verträge. Entschlüsseltes Arbeitsmaterial bleibt innerhalb der Ziel-Migrationsgrenze.

Ein Paket für eine andere Aufnahme oder ein anderes Ziel muss geschlossen fehlschlagen. Erstellen Sie keine neue Zielauswahl als Umgehung, sondern erzeugen Sie auf der Quelle das korrekte Paket.

## Alten Server prüfen

Unter **Alten Server überprüfen** bestätigen Sie:

- älteres Produkt und Version;
- ausgewählte Stack-Identität;
- Matrix- und Element-Hostnamen;
- Quell- und Paketfingerabdrücke;
- erfassten Konfigurations- und Speicherumfang;
- Kompatibilitätsergebnis;
- Warnungen, Einschränkungen und Betreiberbestätigungen;
- normalen Vorschau-/vereinfachten Pfad oder erweiterten final eingefrorenen Handoff.

Beheben Sie Blocker vor der Konvertierung. Bewahren Sie Paketbericht und Zielvalidierungsnachweis auf.

## Was MEM speichert

Verschlüsseltes Paket, validiertes Quellarchiv, Hashes und sichere Herkunft bleiben gemäß Sitzungslebenszyklus als geschützte Migrationsnachweise auf dem Ziel. Sie erscheinen nicht als normale Sicherungskatalog-Einträge.

Der ausgewählte Quell-Stack ist bereits maßgeblich. Die Aufnahme bietet keine zweite Stack-Auswahl.

## Erfolg prüfen

Upload und Aufnahme passen zusammen, Validierung ist abgeschlossen, die Identität entspricht dem Plan, kein Kompatibilitätsblocker bleibt und die Sitzung kann zu **Vorbereiten und testen** wechseln.

Verändern Sie ein fehlgeschlagenes Paket nicht manuell. Vergleichen Sie Anfrage, Aufnahme-ID, Empfängerfingerabdruck, Paketbericht, Quellbewertung und Übertragungsprüfsumme und erstellen Sie das Paket bei Bedarf neu.

Weiter: [Konvertieren und privaten Test ausführen](private-test.md).
