---
title: Willkommen bei MEM
description: Starten Sie hier, um MEM 0.2.0 zu verstehen, den richtigen Weg zu wählen und die ersten Operator-Aufgaben zu finden.
section: Erste Schritte
order: 0
---

# Willkommen bei MEM

![Message Easy Mode Logo](../../../assets/brand/mem-logo-docs.png)

MEM steht für **Message Easy Mode**. MEM 0.2.0 ist eine Open-Source-Control-Plane für den Betrieb selbst gehosteter Matrix- und Element-Dienste auf einem herkömmlichen Linux- und Docker-Host.

MEM richtet sich an technisch versierte Betreiber, die ihre Kommunikationsplattform selbst kontrollieren möchten, ohne jede Datenbank, Proxy-Route, jedes Zertifikat sowie alle Backup-, Restore- und Migrationsschritte von Hand zusammensetzen zu müssen.

> [!IMPORTANT]
> MEM ist die Verwaltungsebene. Matrix bleibt das Kommunikationsprotokoll, Synapse der Homeserver und Element der primäre Web-Client.

## Wobei MEM hilft

Die MEM Control Plane bündelt die wichtigsten Betriebsabläufe in einer privaten Oberfläche:

- den Host vorbereiten und prüfen;
- Domains und Zertifikate konfigurieren;
- Matrix-Chatserver erstellen und untersuchen;
- Benutzer, Föderation und TURN-Verbindungen verwalten;
- Backups erstellen und katalogisieren;
- Wiederherstellungen privat testen und durchführen;
- eine unterstützte ältere MEM-0.1.0-Installation migrieren;
- Diagnosen, Vorfälle, Laufzeitnachweise und Supportberichte prüfen.

Der normale öffentliche Datenverkehr geht an Element, Synapse und TURN. Die MEM-Verwaltungsoberfläche sollte privat bleiben.

## Nächster Schritt

- [Was MEM ist](what-is-mem.md)
- [Passt MEM zu Ihnen?](is-mem-right-for-you.md)
- [Installieren oder migrieren?](install-or-migrate.md)
- [Anforderungen und unterstützte Umgebung](requirements.md)
- [MEM-Produktpakete](packages.md)
- [Bekannte Einschränkungen](known-limitations.md)
- [MEM-Glossar](glossary.md)

## Status dieser Dokumentation

Dieser Bereich **Erste Schritte** wurde für MEM 0.2.x geschrieben und orientiert sich an den aktuellen Verträgen für Control Plane, Einrichtung, Laufzeit, CLI, Migration, Wiederherstellung und Diagnose.

Der aktuelle nicht-legacy Dokumentationsbaum ist für MEM 0.2.x geprüft. Historisches Pre-0.2-Material wird ausdrücklich als Legacy erhalten und nicht mit aktueller Betriebsanleitung vermischt.
