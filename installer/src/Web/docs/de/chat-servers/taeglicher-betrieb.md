---
id: "de/chat-servers/daily-operations"
translationKey: "chat-servers/daily-operations"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "Täglichen Betrieb sicher durchführen"
description: "Verwenden Sie Bereitschaft, Diagnose, Vorgangsverlauf, Sicherungen und eigene Workflows für Routineverwaltung."
order: 90
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Täglicher Betrieb", "Diagnose", "Sicherung", "Vorgangsverlauf", "Sicherheit"]
route: "/docs/de/chat-servers/daily-operations"
aliases: []
outputPath: "docs/de/chat-servers/taeglicher-betrieb.md"
preserveLegacyBranding: false
---
# Täglichen Betrieb sicher durchführen

## Ergebnis

Betreiben Sie einen gesunden Chatserver, ohne jede Beobachtung in eine Containeränderung umzuwandeln.

## Routinecheckliste

Für den normalen Betrieb:

1. öffnen Sie **Chatserver** und prüfen Sie Status und letzten Prüfzeitpunkt;
2. öffnen Sie den Arbeitsbereich und aktualisieren Sie bei Bedarf;
3. verwenden Sie **Element öffnen** für eine echte Benutzerprüfung;
4. führen Sie **Diagnose** nach DNS-, Zertifikats-, Routen-, Firewall-, Abbild- oder Hoständerungen aus;
5. prüfen Sie aktuelle Vorgänge auf laufende, fehlgeschlagene oder zurückgesetzte Arbeit;
6. synchronisieren Sie Matrix-Benutzer vor Kontoverwaltung;
7. prüfen Sie TURN und Föderation nach passenden Plattformänderungen;
8. erstellen Sie vor zerstörerischen oder identitätsrelevanten Arbeiten eine Sicherung.

## Status richtig interpretieren

Ein grüner historischer Status beweist nur die letzte Beobachtung. Er ist kein kontinuierliches Monitoring. Auch das letzte Diagnoseergebnis kann nur in der aktuellen Browsersitzung vorliegen.

Bei einer Störungsmeldung erstellen Sie aktuelle Nachweise statt sich auf einen alten Zeitstempel zu verlassen.

## Eigene Workflows verwenden

Der Stack-Arbeitsbereich vermeidet absichtlich allgemeine Container-Start-, Stopp-, Neustart- und YAML-Bearbeitungsaktionen. Sichere Änderungen können Kandidatenprüfung, Step-up, dauerhaftes Vorgangsjournal, Verifikation und Rollback benötigen.

Verwenden Sie:

- Benutzer-Workflow für Matrix-Konten;
- Sprache & Video für TURN;
- Föderation für Richtlinien;
- Sicherungen und Wiederherstellung für Datenschutz;
- Diagnose für aktuelle Nachweise;
- Löschen für Laufzeitstilllegung.

Portainer oder Host-Docker-Befehle sind Notfallnachweise oder Ausfallwerkzeuge, nicht normale Quelle der Wahrheit für MEM-eigene Konfiguration.

## Verwaltungsoberfläche schützen

Halten Sie die Control Plane privat. Sie besitzt Docker-Socket- und Dateisystemautorität. Verwenden Sie vertrauenswürdiges LAN, VPN, Managementnetz oder SSH-Tunnel.

Fügen Sie keine Passwörter, TOTP-Geheimnisse, Wiederherstellungscodes, Matrix-Zugriffstoken, TURN-Geheimnisse, Signaturschlüssel oder privaten Konfigurationswerte in Supportnotizen ein.

## Vor geplanter Änderung

Erfassen Sie Stack-Slug, öffentliche Hosts, letzten Prüfzeitpunkt, aktuellen Vorgangszustand und letzte nutzbare Sicherung. Führen Sie danach Diagnose aus und bewahren Sie neue Vorgangs- oder Berichtsreferenz auf.
