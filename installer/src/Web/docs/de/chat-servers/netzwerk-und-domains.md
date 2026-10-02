---
id: "de/chat-servers/network-and-domains"
translationKey: "chat-servers/network-and-domains"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "Netzwerk und Domains prüfen"
description: "Verstehen Sie erfasste Matrix- und Element-Hosts, NPM-Routen, Zertifikatsreferenzen und interne Zustellung."
order: 50
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Netzwerk", "Domains", "Nginx Proxy Manager", "Routen", "Zertifikat"]
route: "/docs/de/chat-servers/network-and-domains"
aliases: []
outputPath: "docs/de/chat-servers/netzwerk-und-domains.md"
preserveLegacyBranding: false
---
# Netzwerk und Domains prüfen

## Ergebnis

Bestätigen Sie die für den Stack erfassten öffentlichen und internen Adressen und verwenden Sie Diagnose für eine aktuelle Routenprüfung.

Öffnen Sie im Stack-Arbeitsbereich **Netzwerk & Domains**.

## Öffentliche Namensgebung

Für den Slug `familie` unter `example.org` verwendet der normale Erstellungsplan:

- `matrix-familie.example.org` für den Matrix-Homeserver;
- `chat-familie.example.org` für den Element-Webclient.

Diese Hosts werden aus der aktiven Plattform-Domain abgeleitet. Die Matrix-Identität ist an den Matrix-Servernamen gebunden. Hostnamenänderungen sind daher keine gewöhnliche kosmetische Bearbeitung.

## Angezeigte Daten

Für Matrix und Element kann MEM melden:

- öffentliche Domain und HTTPS-Adresse;
- Nginx-Proxy-Manager-Routen-ID;
- NPM-Zertifikats-ID;
- interner Container-Hostname und URL;
- zuletzt erfasster Prüfzeitpunkt.

Die Werte stammen aus dem Laufzeitmanifest. Bei unvollständigem Manifest erfindet MEM keine Route.

## Schreibgeschützte Grenze

Die Seite prüft nicht direkt:

- aktuelle DNS-Provider-Einträge;
- Zertifikatsablauf;
- vollständige Live-Konfiguration von Nginx Proxy Manager;
- Erreichbarkeit aus jedem externen Netz.

Führen Sie **Diagnose** aus, um NPM, Routen, internes HTTP und öffentliches HTTPS aktuell zu prüfen. Wenn externe Benutzer weiterhin scheitern, prüfen Sie autoritatives DNS, Firewall- und NAT-Regeln sowie Zertifikatsstatus außerhalb dieser Projektion.

## Nicht verwaltete Änderungen vermeiden

Ändern Sie Matrix- oder Element-Hosts nicht beiläufig direkt in NPM oder Synapse. Eine externe Routen-, Zertifikats- oder Servernamenänderung kann Manifest-Drift erzeugen, Element-Erkennung brechen oder Matrix-Identität verändern.

Verwenden Sie Migration oder Wiederherstellung, wenn eine neue Hostname- oder Serveridentität beabsichtigt ist.
