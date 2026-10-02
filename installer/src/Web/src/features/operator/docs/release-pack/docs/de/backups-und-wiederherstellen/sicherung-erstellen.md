---
title: Sicherung erstellen und prüfen
description: Erfassen Sie Datenbank, Matrix-Identität, Medien, Konfiguration sowie Routen- und TURN-Nachweise eines Stacks.
section: Sichern und wiederherstellen
order: 10
---

# Sicherung erstellen und prüfen

Erstellen Sie vor Upgrades, Föderationsänderungen, TURN-Änderungen, Speicherarbeiten, Kontowiederherstellung oder anderen riskanten Vorgängen eine Sicherung.

## Voraussetzungen

Prüfen Sie:

- Der Stack ist unter **Chatserver** sichtbar.
- Die MEM Control Plane erreicht Docker und `mem-postgres`.
- Der Host besitzt genügend freien Speicher für Datenbankdump und Medienkopie.
- Keine parallele Host-Wartung verändert dieselben Dateien.

## Sicherung erstellen

1. **Chatserver** öffnen und den Stack auswählen.
2. **Sicherungen & Wiederherstellung** öffnen oder im Kopfbereich **Sicherung erstellen** verwenden.
3. Auf die Erfolgsmeldung warten und Sicherungs-ID sowie Zeitpunkt notieren.
4. Die Wiederherstellungsquelle im Sicherungskatalog öffnen.

Die Erfassung liest Produktions-PostgreSQL und kopiert Wiederherstellungsmaterial in MEM-verwalteten Speicher. Sie stoppt den Stack nicht und ändert keine öffentlichen Routen.

## Erfasste Bestandteile

Eine native MEM-Sicherung kann enthalten:

- PostgreSQL-Dump der Synapse-Datenbank;
- `homeserver.yaml`;
- Matrix-Signaturschlüssel;
- Matrix-Verzeichnis `media_store`;
- Element-Datei `config.json`;
- Manifest mit Stack-Identität, Dateistatistik und Warnungen;
- Snapshot der öffentlichen Matrix- und Element-Routen;
- wirksamen TURN-Zustand aus der erfassten Synapse-Konfiguration.

Routen und TURN sind Nachweise zum Erfassungszeitpunkt. MEM rekonstruiert sie nicht später aus dem dann aktuellen Laufzeitstatus.

## Erfolg prüfen

Im Sicherungskatalog bestätigen:

- **Herkunft** ist `local-captured`;
- **Payload** ist verfügbar;
- Quell-Stack und Sicherungs-ID stimmen;
- Zeitpunkt und Größe sind plausibel;
- Integrität ist gültig oder jede Warnung wurde verstanden;
- Matrix-Servername, Matrix-Host und Element-Host gehören zum richtigen Stack.

Ein fehlender Signaturschlüssel ist kritisch für die Matrix-Föderationsidentität. Fehlende Medien können dazu führen, dass Nachrichten vorhanden sind, hochgeladene Dateien und Vorschaubilder jedoch fehlen.

## Nachweise aufbewahren

Sicherungs-ID, Katalog-ID, Vorgangs-ID, Warnungen, Gesamtgröße und Erfassungszeit notieren. Wenn der Payload erfolgreich erfasst wurde, aber kein erwarteter Katalogeintrag erscheint, verschieben oder benennen Sie das Sicherungsverzeichnis nicht manuell um. Bewahren Sie die Vorgangsnachweise auf und verwenden Sie den unterstützten Katalog-Backfill- oder Diagnoseweg.

> [!NOTE]
> Matrix-Clients verwalten eigene Ende-zu-Ende-Verschlüsselungsschlüssel. Eine erfolgreiche Serversicherung beweist nicht, dass jeder Benutzer nach Geräteverlust oder Passwortzurücksetzung alte verschlüsselte Räume lesen kann.
