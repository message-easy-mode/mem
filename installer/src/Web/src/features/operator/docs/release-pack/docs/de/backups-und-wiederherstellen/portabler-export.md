---
title: Portable Sicherung exportieren
description: Erzeugen Sie ein neues portables ZIP aus Katalogmaterial und lagern Sie es außerhalb des Hosts.
section: Sichern und wiederherstellen
order: 30
---

# Portable Sicherung exportieren

Eine lokale Sicherung auf dem MEM-Host schützt vor Bedienfehlern, aber nicht vor Hostverlust, Datenträgerausfall, Diebstahl oder destruktiven Administratoraktionen. Exportieren Sie wichtige Katalogeinträge auf unabhängigen Speicher.

## Export erstellen

1. **Sicherungen** öffnen.
2. Einen Eintrag mit verfügbarem Payload auswählen.
3. Unter **Portabler Export** das ZIP erzeugen und herunterladen.
4. Katalog-ID, Dateiname, Größe und Zeitpunkt notieren.
5. ZIP auf geschützten externen Speicher kopieren.

MEM erzeugt das Archiv aus dem Katalog-Payload. Bei einer importierten Sicherung ist der neue Export nicht vom ursprünglichen Upload-ZIP abhängig.

## Inhalt

Das Exportmanifest beschreibt:

- Quell-Stack und Matrix-Serveridentität;
- PostgreSQL-Dump;
- Matrix-Konfiguration, Signaturschlüssel und Medien;
- Element-Konfiguration;
- Matrix- und Element-Routen;
- TURN-Zustand und Wiederherstellungsabsicht;
- Wiederherstellungsanforderungen;
- enthaltene Dateien, Warnungen und Prüfsummendatei.

Die Browserantwort enthält keine Host-Dateisystempfade.

## Sicherer Umgang

Das ZIP kann private Raum- und Kontodaten, Benutzermedien, Matrix-Konfiguration, Signaturschlüssel sowie Domain- und Topologieinformationen enthalten. Schützen Sie den Speicher, beschränken Sie Zugriff und laden Sie das Archiv nicht in normale Support-Tickets oder öffentliche Freigaben.

## Externe Kopie prüfen

Dateigröße nach dem Kopieren vergleichen und prüfen, ob die Datei vom Ziel gelesen werden kann. Der stärkste Nachweis ist ein Import auf einer nicht produktiven MEM-Umgebung mit anschließendem [privaten Test](privater-test.md).

Ein portables ZIP ist ein MEM-Wiederherstellungsartefakt und kein universelles Synapse-Migrationsformat. Kompatibilität hängt weiterhin von Manifest, Payload, Datenbank- und Laufzeitrichtlinie sowie Zielversion ab.

## Aufbewahrung

Die Löschung des Katalogeintrags kann auch serverseitige portable Exporte dieses Eintrags entfernen. Die heruntergeladene externe Kopie liegt außerhalb von MEM. Pflegen Sie eine eigene Aufbewahrungsrichtlinie und prüfen Sie Archive regelmäßig.
