---
id: "de/migrate"
translationKey: "migrate"
locale: "de"
groupId: "migrate-de"
groupKey: "migrate"
groupLabel: "Von MEM 0.1.0 migrieren"
groupOrder: 19
title: "Von MEM 0.1.0 migrieren"
description: "Migrieren Sie einen älteren MEM-0.1.0-Chatserver verschlüsselt, gestuft und geprüft nach MEM 0.2.0."
order: 0
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Migration", "MEM 0.1.0", "MEM Migrate", "Source Assistant", "Liveschaltung"]
route: "/docs/de/migrate"
aliases: []
outputPath: "docs/de/migrieren/index.md"
preserveLegacyBranding: false
---
# Von MEM 0.1.0 migrieren

MEM Migrate überführt einen unterstützten älteren MEM-0.1.0-Chatserver nach MEM 0.2.0, ohne den alten Server als normale Sicherung zu behandeln.

Der Ablauf bewahrt die ausgewählte Matrix-Identität, verschlüsselt die Übertragung für die Ziel-Control-Plane, erstellt einen privaten Kandidaten, prüft die neue Laufzeit vor der öffentlichen Umschaltung und zeichnet durchgehend dauerhafte Nachweise auf.

## Ergebnis

Nach diesem Abschnitt können Sie:

- den MEM Migrate Source Assistant auf dem alten Host installieren und privat öffnen;
- die Quelle ohne Änderungen bewerten;
- genau einen Quell-Stack auswählen;
- die Ziel-Migrationsanfrage importieren und ein verschlüsseltes Paket erstellen;
- das Paket in MEM 0.2.0 hochladen, validieren, konvertieren und privat testen;
- den neuen normalen MEM-Server erstellen, ohne ihn zu veröffentlichen;
- die geführte Routenumstellung und Produktionsprüfung durchführen;
- den migrierten Server annehmen, die Quelle aufbewahren und die erste native MEM-Sicherung erstellen;
- Abbruch-, Rollback-, Bereinigungs- und Nachweisgrenzen verstehen.

## Die beiden Arbeitsbereiche

```text
Alter MEM-0.1.0-Host                         MEM-0.2.0-Ziel
────────────────────                         ──────────────
MEM Migrate Source Assistant                 Migrationsarbeitsbereich der Control Plane
  1. Import request (Anfrage importieren)       1. Paket erstellen und hochladen
  2. Confirm readiness (Bereitschaft)           2. Alten Server überprüfen
  3. Create capture (Erfassung)                 3. Vorbereiten und testen
  4. Create package (Paket)                     4. Neuen Server erstellen
  5. Download package (Download)                5. Neuen Server live schalten
                                                6. Migration abschließen
```

Die Oberfläche des Source Assistant verwendet derzeit englische Aktionsbezeichnungen. Der Source Assistant liest und verpackt den ausgewählten alten Stack. Die Ziel-Control-Plane besitzt Entschlüsselung, Konvertierung, privates Staging, Routenumstellung, Annahme und erste native Sicherung.

## Zentrale Sicherheitsregeln

- Migrieren Sie **jeweils genau einen Stack**. Die Quellauswahl wird in das Paket geschrieben und ist maßgeblich.
- Bewahren Sie die alte Quelle bis nach Produktionsprüfung, Annahme und dem gewählten Aufbewahrungszeitraum auf.
- Stellen Sie den Source Assistant nicht ins öffentliche Internet. Verwenden Sie Loopback oder einen SSH-Tunnel.
- Schützen Sie vor der Erfassung die Verschlüsselungswiederherstellung der Benutzer. Ein Passwort-Reset kann fehlende historische Nachrichtenschlüssel nicht neu erzeugen.
- Privates Staging besitzt keine öffentlichen Matrix- oder Element-Routen.
- Routenveröffentlichung ist keine DNS-Verwaltung. DNS-Einträge und ein verwendbares Zertifikat müssen bereits vorhanden sein.
- Eine fehlgeschlagene Live-Prüfung schaltet den alten Server nicht automatisch wieder öffentlich.
- Migrationsmaterial gelangt erst nach Annahme und erster nativer Sicherung in den Sicherungskatalog.

> [!IMPORTANT]
> Installieren Sie MEM 0.2.0 nicht als Abkürzung über den alten Host. Verwenden Sie ein separates MEM-0.2.0-Ziel und den geführten Migrationspfad.

## Einstieg

1. [Migration entscheiden und sicher vorbereiten](decide-and-prepare.md).
2. [Source Assistant installieren und privat öffnen](install-source-assistant.md).
3. [Quelle bewerten und einen Stack auswählen](assess-and-select.md).
4. [Verschlüsseltes Migrationspaket erstellen](create-package.md).
5. In der Ziel-Control-Plane mit [Paket hochladen und alten Server prüfen](upload-and-review.md) fortfahren.
6. Vor der Produktionsumschaltung [Rollback-Grenzen](rollback.md) lesen.
