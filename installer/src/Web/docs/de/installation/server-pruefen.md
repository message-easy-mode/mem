---
id: "de/installation/check-server"
translationKey: "installation/check-server"
locale: "de"
groupId: "installation-de"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Serverprüfung ausführen"
description: "Verstehen Sie Neuinstallation, Migration, Reparatur sowie Host-, Docker-, Speicher- und Portergebnisse."
order: 40
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Preflight", "Host-Prüfung", "Legacy-Erkennung", "Docker"]
route: "/docs/de/installation/server-pruefen"
aliases: []
outputPath: "docs/de/installation/server-pruefen.md"
preserveLegacyBranding: false
---
# Serverprüfung ausführen

Die Setup-Startseite klassifiziert den Host, bevor eine neue Installation angeboten wird.

Mögliche Ergebnisse:

- neue Installation;
- Legacy-Migration erforderlich;
- Reparatur oder vorhandene Ressourcen;
- bereits installiert;
- unbekannt, weil Docker oder Statusprüfung fehlgeschlagen ist.

## Legacy-Erkennung bedeutet Stopp

Wenn `mem-api` oder `mem-web` ohne aktuellen Installationsdatensatz erkannt wird, behandelt MEM den Host als Legacy-MEM-0.1.0-Quelle.

Führen Sie keine neue Installation aus. Lassen Sie die Quelle bestehen und verwenden Sie `mem-migrate`.

Die alte Installation kann außerdem PostgreSQL, NPM, Compose-Dateien, `.env`, `stack.sh`, Matrix-Container, Datenpfade und Zertifikate enthalten, die nicht unbedacht überschrieben werden dürfen.

## Vorhandene Ressourcen ohne Datensatz

Wenn MEM-Container, Netzwerke oder Volumes vorhanden sind, aber kein aktueller Installationsdatensatz existiert, bietet MEM einen Reparatur- oder Untersuchungsweg.

Prüfen Sie die Ressourcen und Diagnosen. Löschen Sie nichts nur, um eine Warnung zu beseitigen.

## Prüfung starten

Wählen Sie im Setup **Serverprüfungen ausführen**. Während der aktiven Ersteinrichtung erfasst MEM gruppierte Host-Prüfnachweise und übernimmt eine kompakte Zusammenfassung in den Installationsplan für die spätere Überprüfung und Supportnachweise.

Die Prüfungen umfassen:

### Host

- Ubuntu;
- CPU-Architektur;
- CPU-Anzahl;
- RAM.

### Docker

- Docker- und Daemon-Erreichbarkeit;
- Compose-Plugin;
- Docker-Datenpfad;
- Docker-Speichernutzung;
- vorhandene MEM-Container;
- Netzwerke und Volumes.

### Speicher

- freier Speicher auf `/`;
- freier Speicher im Docker-Datenpfad.

### Ports

Beobachtet werden:

```text
80
443
8443
8080
5432
```

8443 darf vom Installer belegt sein. Andere Listener müssen verstanden werden.

## Kompakte Ergebnisse richtig lesen

Die Seite Serverprüfungen lässt Blocker, Warnungen, nicht verfügbare Prüfungen und andere Punkte mit Handlungsbedarf sichtbar. Routineprüfungen mit erfolgreichem Ergebnis sind pro Gruppe eingeklappt, damit Entscheidungen im Vordergrund stehen. Öffnen Sie **Technische Details** und **Rohdaten** nur, wenn Sie Begründung oder begrenzte Befehlsnachweise benötigen.

Die meisten Browser-Prüfungen sind Hinweise. Eine Warnung ist kein automatisches Verbot; ein erfolgreiches Ergebnis ist keine Produktionsgarantie. Das Host-Bootstrap hat bereits seine harten Ubuntu-, RAM- und Speicherregeln angewendet; die Browser-Prüfung liefert eine zweite, laufzeitbewusste Betriebssicht.

Nach Abschluss der Einrichtung ist die Entwicklungsvorschau schreibgeschützt. Detaillierte Prüfläufe gehören zur aktiven Einrichtungssitzung und müssen einen Neustart der Control Plane nicht überleben; die Installation behält die kompakte Prüfübersicht für Review und Supportberichte.

## Weiter

Fahren Sie nur fort, wenn Docker erreichbar ist, keine Legacy-Migration erforderlich ist, vorhandene Ressourcen verstanden sind und private Zugriffs- sowie Speicherwarnungen akzeptabel sind.
