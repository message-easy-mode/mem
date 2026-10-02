---
id: "de/backups-restores/private-test"
translationKey: "backups-restores/private-test"
locale: "de"
groupId: "backups-restores-de"
groupKey: "backups-restores"
groupLabel: "Sichern und wiederherstellen"
groupOrder: 18
title: "Privaten Wiederherstellungstest ausführen"
description: "Prüfen Sie Datenbankimport und Synapse-Start in einem internen Docker-Netz ohne öffentliche Routen."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Privater Test", "Staging", "internes Docker-Netz", "Synapse-Health", "sichere Wiederherstellung"]
route: "/docs/de/backups-and-restores/privater-test"
aliases: []
outputPath: "docs/de/backups-und-wiederherstellen/privater-test.md"
preserveLegacyBranding: false
---
# Privaten Wiederherstellungstest ausführen

Der private Test ist optional, aber der sicherste Weg, einen beschädigten oder inkompatiblen Payload vor Änderungen an Produktions-PostgreSQL, Docker oder öffentlichen Routen zu erkennen.

## Sicherheitsgrenze

Der Test erstellt wegwerfbare Infrastruktur in einem internen Docker-Netz. Die Nachweise bestätigen:

- nur privaten Betrieb;
- internes Docker-Netz;
- keine öffentlichen Routen;
- keine DNS- oder Zertifikatsänderung;
- keine Berührung von Produktionscontainern oder Produktionsdatenbanken;
- getrennte Prüfung von Datenbankimport und Synapse-Health.

Er ist keine öffentliche Vorschau. Fügen Sie keine manuellen Nginx-Proxy-Manager-Routen hinzu.

## Test ausführen

1. Im Arbeitsbereich **Standard** öffnen.
2. **Privater Test** erweitern.
3. Blocker prüfen und den Test starten.
4. Abschluss abwarten.
5. Datenbankimport, Synapse-Health, Matrix-Identität, Zeitpunkt, Nachweise und Logs prüfen.

Ein erfolgreicher Test beweist Import und Health des isolierten Synapse-Runtimes. Er beweist nicht öffentliche DNS-Auflösung, Zertifikate, NPM-Routen, Föderation, Element-Benutzererlebnis oder reale Sprach-/Videoanrufe.

## Aufbewahrtes Staging

Ein erfolgreicher oder bewusst aufbewahrter Test kann explizite Zerstörung erfordern. Verwenden Sie **Privaten Test entfernen**, nachdem die Nachweise geprüft wurden. MEM entfernt Container, internes Netz und Wegwerf-Arbeitsbereich, behält aber das sichere historische Ergebnis.

Entfernen Sie Staging-Container nicht manuell, außer die Control Plane ist nicht verfügbar und Staging-ID sowie Vorgangsnachweise wurden gesichert.

## Fehlerbehandlung

Bei Fehlern:

- sichere Fehlerzusammenfassung lesen;
- **Nachweise** und **Logs** öffnen;
- Restore-ID, Vorgangs-ID, Ereigniscode und Staging-ID notieren;
- Voraussetzungen korrigieren statt den materialisierten Payload direkt zu bearbeiten;
- erst nach geklärter Ursache erneut ausführen.

Das Überspringen ist zulässig, verlagert aber zusätzliches Risiko in die Standard-Neuerstellung und sollte eine bewusste Betreiberentscheidung sein.
