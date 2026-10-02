---
title: Was MEM ist
description: Verstehen Sie die Rolle der MEM Control Plane und ihr Verhältnis zu Matrix, Synapse, Element, Docker und den Wiederherstellungswerkzeugen.
section: Erste Schritte
order: 10
---

# Was MEM ist

MEM ist eine unabhängige Open-Source-**Control-Plane für Betrieb, Wiederherstellung und Migration** selbst gehosteter Matrix-Umgebungen. MEM definiert kein neues Messaging-Protokoll und ersetzt Matrix nicht.

## Das zugrunde liegende Kommunikationssystem

Ein normaler, von MEM verwalteter Chatserver nutzt bekannte Upstream-Komponenten:

- **Matrix** stellt das offene Kommunikationsprotokoll bereit.
- **Synapse** ist der Matrix-Homeserver.
- **Element Web** ist der Messaging-Client im Browser.
- **PostgreSQL** speichert die Serverdaten von Synapse.
- **Nginx Proxy Manager** stellt HTTPS-Ingress und öffentliche Routen bereit.
- **coturn** stellt TURN-Relay-Dienste für Sprach- und Videoverbindungen bereit.

MEM koordiniert diese Komponenten und dokumentiert die Betriebsabläufe darum herum.

## Die Control Plane

Die MEM Control Plane ist die maßgebliche Verwaltungsanwendung. In MEM 0.2.0 arbeiten React-Oberfläche, ASP.NET-Core-API, Authentifizierung, dauerhafter Workflow-Zustand und privilegierte HostAgent-Dienste als eine Control-Plane-Anwendung. HostAgent ist eine privilegierte Bibliothek im selben Prozess und kein separater Remote-Daemon.

Die Control Plane kann MEM-eigene Docker-Ressourcen prüfen und ändern, genehmigte Host-Konfigurationen schreiben, Datenbanken bereitstellen, Routen erstellen oder prüfen, Workloads steuern und Betriebsnachweise aufbewahren.

Da sie Zugriff auf den Docker-Socket und MEM-Datenpfade hat, besitzt sie weitreichende Rechte. Halten Sie sie privat und schützen Sie Operator-Konten mit MFA und passenden Rollen.

## Die drei Ebenen

1. **Control Plane** — Operatoridentität, Workflows, Sollzustand, Diagnose und privilegierte Orchestrierung.
2. **Verwaltete Matrix-Datenebene** — Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn und stackbezogene Daten.
3. **Wiederherstellungs- und Migrationsebene** — Backup-Katalog, Restore Workspace, portable Artefakte, MEM Migrate, private Staging-Umgebungen und Produktionsübernahme.

Normale Matrix-Nachrichten laufen nicht durch die Verwaltungsoberfläche.

## Was MEM ergänzt

MEM ergänzt kontrollierte Abläufe rund um Infrastruktur, die sonst durch Compose-Dateien, Shell-Befehle, Datenbankkommandos, YAML, Proxy-Konfiguration und das Gedächtnis einzelner Betreiber gepflegt würde.

Ziel ist nicht, die Infrastruktur unsichtbar zu machen. Ziel ist, Eigentum, Absicht, Änderungen, Überprüfung, Wiederherstellung und Fehlernachweise verständlicher zu machen.
