---
id: "de/chat-servers"
translationKey: "chat-servers"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "Chatserver erstellen und betreiben"
description: "Erstellen, prüfen, verwalten und entfernen Sie Matrix- und Element-Stacks sicher über die MEM Control Plane."
order: 0
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Chatserver", "Matrix", "Element", "Betrieb", "Stack-Arbeitsbereich"]
route: "/docs/de/chat-servers"
aliases: []
outputPath: "docs/de/chat-servers/index.md"
preserveLegacyBranding: false
---
# Chatserver erstellen und betreiben

Ein **Chatserver** in MEM besteht aus einem verwalteten Matrix-Homeserver und einem Element-Webclient. MEM bezeichnet dieses Paar als **Runtime Stack** oder kurz **Stack**.

## Ergebnis

Nach diesem Abschnitt können Sie:

- einen neuen Matrix- und Element-Stack auf einer installierten MEM-Plattform erstellen;
- öffentliche und interne Bereitschaft prüfen;
- den ersten Matrix-Administrator und weitere Benutzer anlegen;
- Dienste, Routen, Speicher, TURN, Föderation und Vorgangsverlauf prüfen;
- Routineprüfungen durchführen und vor riskanten Arbeiten eine Sicherung erstellen;
- die aktive Laufzeit entfernen, ohne fälschlich von gelöschten Daten auszugehen.

## Was zu einem Stack gehört

MEM erstellt und erfasst eine Stack-bezogene Matrix-Identität, Synapse-Konfiguration, Element-Konfiguration, Matrix- und Element-Container, PostgreSQL-Datenbank und Rolle, Dateispeicher, Nginx-Proxy-Manager-Routen, Geheimnisse, Bereitschaftsnachweise und Vorgangsverlauf.

Plattformdienste wie PostgreSQL, Nginx Proxy Manager und coturn werden gemeinsam genutzt. Matrix-Benutzer, Räume, Nachrichten, Medien, Föderationsrichtlinie und die meisten Laufzeitnachweise gehören zum jeweiligen Stack.

> [!IMPORTANT]
> Matrix-Konten sind keine MEM-Control-Plane-Konten. Ein Platform Owner meldet sich zur Verwaltung von MEM an. Ein Matrix-Benutzer meldet sich bei Element oder einem anderen Matrix-Client an.

## Empfohlener Ablauf für den ersten Server

1. [Chatserver erstellen](erstellen.md).
2. [Stack-Arbeitsbereich und Status verstehen](arbeitsbereich.md).
3. [Matrix-Benutzer synchronisieren und erstellen](benutzer.md).
4. Element öffnen und mit dem ersten Matrix-Administrator anmelden.
5. [Netzwerk und Domains](netzwerk-und-domains.md), [Sprache und Video](sprache-und-video.md) sowie [Föderation](foederation.md) prüfen.
6. Einen [sicheren täglichen Betriebsablauf](taeglicher-betrieb.md) festlegen.

## Anleitungen in diesem Abschnitt

- [Chatserver erstellen](erstellen.md)
- [Stack-Arbeitsbereich verwenden](arbeitsbereich.md)
- [Matrix-Benutzer verwalten](benutzer.md)
- [Passwörter und Kontolebenszyklus verwalten](passwoerter-und-konten.md)
- [Netzwerk und Domains prüfen](netzwerk-und-domains.md)
- [TURN für Sprache und Video konfigurieren](sprache-und-video.md)
- [Föderation verwalten](foederation.md)
- [Speicher und Medien verstehen](speicher-und-medien.md)
- [Täglichen Betrieb sicher durchführen](taeglicher-betrieb.md)
- [Chatserver entfernen](entfernen.md)
- [Chatserver-Fehler beheben](fehlerbehebung.md)

Sicherung, Wiederherstellung, Migration und vollständige Diagnose besitzen eigene abgegrenzte Workflows. Verwenden Sie die Links im Stack-Arbeitsbereich, statt diese Aufgaben als gewöhnliche Container-Aktionen zu behandeln.
