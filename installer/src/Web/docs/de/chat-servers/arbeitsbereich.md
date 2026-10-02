---
id: "de/chat-servers/workspace"
translationKey: "chat-servers/workspace"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "Stack-Arbeitsbereich verwenden"
description: "Verstehen Sie Bereitschaft, Dienste, Aktionen, Bereiche und erfasste Vorgangsnachweise eines Stacks."
order: 20
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Stack-Arbeitsbereich", "Bereitschaft", "Diagnose", "Vorgänge", "Element"]
route: "/docs/de/chat-servers/workspace"
aliases: []
outputPath: "docs/de/chat-servers/arbeitsbereich.md"
preserveLegacyBranding: false
---
# Stack-Arbeitsbereich verwenden

## Ergebnis

Verwenden Sie den Stack-Arbeitsbereich als normale Betriebsoberfläche für einen Matrix- und Element-Stack.

Öffnen Sie **Chatserver** und wählen Sie den Stack-Namen oder **Prüfen**.

## Aktionen in der Kopfzeile

Die Kopfzeile bietet Stack-bezogene Aktionen:

- **Element öffnen** öffnet die erfasste öffentliche Element-URL.
- **Matrix API** öffnet die erfasste Homeserver-Adresse.
- **Aktualisieren** lädt die aktuellen Projektionen neu.
- **Diagnose ausführen** prüft Nginx Proxy Manager, konfigurierte Routen, internes HTTP und öffentliches HTTPS.
- **Sicherung erstellen** startet den Stack-Sicherungsworkflow.

Eine erfasste URL ist keine aktuelle Live-Prüfung. Verwenden Sie Aktualisieren und Diagnose, wenn der gegenwärtige Zustand wichtig ist.

## Überblick

Der Überblick kombiniert:

- letzten bekannten Laufzeitstatus und Prüfzeitpunkt;
- öffentliche Matrix- und Element-Hosts;
- das letzte in der aktuellen Browsersitzung behaltene Diagnoseergebnis;
- den zuletzt lokal erfassten Sicherungsvorgang;
- aktuelle Control-Plane-Vorgänge mit angefordertem, aktuellem und abgeschlossenem Zustand.

**Zuletzt geprüft** ist historischer Nachweis. Der Wert bedeutet nicht, dass der Endpunkt beim Öffnen der Seite erneut geprüft wurde.

## Bereiche des Arbeitsbereichs

**Dienste** zeigt Fakten aus dem Laufzeitmanifest zu Matrix und Element, darunter Container, interne Adressen, öffentliche Routen, Zertifikatskennungen, Datenbankmetadaten und Pfade. Die meisten Werte sind schreibgeschützte Nachweise.

**Speicher & Medien** zeigt wiederherstellungsrelevante Dateien und Mediennutzung.

**Benutzer** synchronisiert Matrix-Konten und bietet geschützte Kontoworkflows.

**Sicherungen** und **Wiederherstellung** öffnen die eigene Recovery Plane.

**Föderation** verwaltet öffentliche, eingeschränkte und lokale Föderation über geprüfte Vorgänge.

**Netzwerk & Domains** zeigt erfasste öffentliche und interne Zustellungsdaten.

**Sprache & Video** prüft und verwaltet die TURN-Zuordnung des Stacks.

**Diagnose** zeigt Nachweise der aktuellen Browsersitzung und verweist auf die breitere Diagnoseoberfläche.

## Schreibgeschützter Nachweis oder Änderung

Viele Bereiche bieten absichtlich keine allgemeinen Bearbeitungs-, Start-, Stopp- oder Neustartaktionen. MEM verwendet eigene geprüfte Workflows für Änderungen an Identität, Verfügbarkeit, Föderation, Anrufen oder Wiederherstellung.

Bearbeiten Sie Synapse-YAML, Element-Konfiguration, NPM-Routen oder MEM-eigene Datenbankeinträge nicht nur deshalb direkt, weil ihre Pfade oder Kennungen angezeigt werden. Änderungen außerhalb von MEM können Drift erzeugen und sichere Automatisierung blockieren.
