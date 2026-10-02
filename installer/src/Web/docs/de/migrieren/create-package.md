---
id: "de/migrate/create-package"
translationKey: "migrate/create-package"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Zielanfrage importieren, erfassen und Paket erstellen"
description: "Importieren Sie die öffentliche Zielanfrage, bestätigen Sie die Verschlüsselungsbereitschaft, erfassen Sie den Stack und laden Sie das verschlüsselte Paket herunter."
order: 40
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Migrationsanfrage", "Erfassung", "verschlüsseltes Paket", "Paketbericht", "E2EE"]
route: "/docs/de/migrate/create-package"
aliases: []
outputPath: "docs/de/migrieren/create-package.md"
preserveLegacyBranding: false
---
# Zielanfrage importieren, erfassen und Paket erstellen

Die Ziel-Control-Plane erstellt die öffentliche Verschlüsselungsanfrage. Der Source Assistant erzeugt damit ein Paket, das nur das Ziel entschlüsseln kann.

## Ergebnis

Eine frische Erfassung des ausgewählten Stacks liegt als verschlüsselte Datei `.memmigration.zip.age` mit Paketbericht und Prüfsummennachweis für die Übertragung nach MEM 0.2.0 vor.

## Ziel-Migrationsanfrage erstellen

Öffnen Sie auf dem MEM-0.2.0-Ziel **Migrationen** und erstellen Sie eine sichere Migrationsaufnahme. Schließen Sie bei Aufforderung die Step-up-Authentifizierung ab.

Laden Sie die Migrationsanfrage als JSON herunter. Sie enthält Aufnahmeidentität und öffentliche `age`-Empfängerinformationen. Die private Entschlüsselungsidentität verbleibt in der Ziel-Control-Plane und wird nie an den Source Assistant übertragen.

Übertragen Sie die JSON-Datei über Ihren normalen sicheren Administrationsweg zum Arbeitsplatz oder Quellhost.

## Anfrage importieren

Im Source-Assistant-Arbeitsbereich:

1. **Import request** wählen.
2. JSON-Datei hochladen oder vollständigen JSON-Inhalt einfügen.
3. Aufnahme-ID, Empfängerfingerabdruck, Anfrageart und Gültigkeit prüfen.
4. Unerwartetes Ziel, Fingerabdruckabweichung, fehlerhafte oder abgelaufene Anfrage ablehnen.

## Verschlüsselungsbereitschaft bestätigen

Vor der Erfassung verlangt der Source Assistant die Bestätigung, dass betroffene Benutzer ihre verschlüsselte Historie schützen sollen. Bestätigen Sie erst, nachdem die Benutzer aufgefordert wurden, angemeldet zu bleiben und ein anderes Gerät, Secure Backup samt Geheimnis oder exportierte Raumschlüssel zu prüfen.

## Erfassung erstellen

Wählen Sie eine frische Erfassung, außer der Source Assistant bietet ausdrücklich eine geeignete aufbewahrte Erfassung mit passender Identität und Zweckbindung an.

Die Erfassung ist eine dauerhafte serverseitige Operation. Schließen oder Aktualisieren des Browsers bricht sie nicht ab. Warten Sie auf Abschluss und prüfen Sie Warnungen.

Die aktive Quelle wird dabei nur gelesen. Arbeitsmaterial entsteht im Zustandsbereich des Source Assistant.

## Paket erstellen und herunterladen

Führen Sie aus:

1. **Create capture** ausführen und den Abschluss prüfen;
2. **Create package** wählen;
3. auf Verschlüsselung und Paketprüfung warten;
4. **Download package** wählen;
5. **Download package report** wählen.

Bewahren Sie Paket und Bericht zusammen auf. Notieren oder prüfen Sie nach jeder Übertragung die gemeldete SHA-256-Prüfsumme.

> [!IMPORTANT]
> Das verschlüsselte Paket ist keine normale MEM-Sicherung. Benennen Sie es nicht wie einen Sicherungskatalog-Export um und entpacken oder verändern Sie es nicht manuell.

## Lokale Löschgrenze

Das Löschen des fertigen lokalen Pakets entfernt das verschlüsselte Paket und die Paketberichte. Quellbewertung, Erfassungsjournal, Klartext-Erfassung, aktive Matrix-Daten und alter Server bleiben erhalten.

Weiter: [Paket hochladen und alten Server prüfen](upload-and-review.md).
