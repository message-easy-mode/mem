---
id: "de/start/packages"
translationKey: "start/packages"
locale: "de"
groupId: "start-de"
groupKey: "start"
groupLabel: "Erste Schritte"
groupOrder: 0
title: "MEM-Produktpakete"
description: "Unterscheiden Sie MEM Control Plane, MEM CLI, mem-migrate CLI und MEM Migrate Source Assistant als separate Auslieferungen."
order: 30
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Pakete", "Control Plane", "MEM CLI", "MEM Migrate", "Source Assistant"]
route: "/docs/de/start/packages"
aliases: []
outputPath: "docs/de/start/packages.md"
preserveLegacyBranding: false
---
# MEM-Produktpakete

MEM 0.2.0 wird als kleine Gruppe zusammenarbeitender Produkte ausgeliefert und nicht als ein universelles Programm.

## MEM Control Plane

Die Control Plane ist das maßgebliche serverseitige Paket auf dem Zielhost. Sie enthält Web-Oberfläche, ASP.NET-Core-API, Operatoridentität, Einrichtungs- und Betriebsworkflows, privilegierten HostAgent-Zugriff, SQLite-Zustand, Diagnose und den lokalen Dokumentationsleser.

## MEM CLI

Der installierte Operatorbefehl lautet:

```bash
mem
```

MEM CLI ist ein Remote-Client für die Control Plane. Docker- oder Wiederherstellungslogik wird nicht lokal neu implementiert. Die CLI unterstützt Profile, eine im Browser genehmigte Geräteanmeldung, englische oder deutsche Ausgabe und stabile englische JSON-Felder.

Normale CLI-Credentials werden über den Secret Service des Betriebssystems gespeichert. Lokale Ausführung oder SSH umgehen keine serverseitigen Rollen, Audit- oder Step-up-Regeln.

## MEM Migrate CLI

Der Migrationsbefehl lautet:

```bash
mem-migrate
```

MEM Migrate ist ein separates, versionsspezifisches Produkt mit Wissen über die unterstützte MEM-0.1.0-Quellstruktur. Es bewertet und erfasst die alte Quelle, prüft Artefakte, erstellt Pakete und führt Worker-seitige Konvertierungsaufgaben aus.

Auf dem Ziel startet die Control Plane die installierte Datei als Worker und validiert strukturierte Ereignisse, Berichte, Pfade und Ausgabe-Hashes. Legacy-Migrationscode wird nicht in die normale Control-Plane-Laufzeit eingebunden.

## MEM Migrate Source Assistant

Der Source Assistant ist eine temporäre lokale ASP.NET-Core- und React-Anwendung auf dem alten Server. Er führt durch Anmeldung mit Zugriffscode, Quellenbewertung, Stack-Auswahl, Import der Zielanfrage, Erfassung, Erstellung des verschlüsselten Pakets, Download und lokalen Lebenszyklus.

Der sichere Standard ist Loopback-Zugriff. Der Source Assistant ist nicht die permanente Verwaltungsoberfläche des Zielservers.

## Welches Paket benötige ich?

| Ziel | Paket |
|---|---|
| MEM 0.2.0 installieren und betreiben | MEM Control Plane |
| Die Control Plane skripten oder untersuchen | MEM CLI |
| Von unterstütztem MEM 0.1.0 migrieren | MEM Migrate |
| Einen geführten Browser auf dem alten Server verwenden | MEM Migrate Source Assistant |
