---
title: Bekannte Einschränkungen
description: Verstehen Sie die bewusst gesetzte Capability-Grenze von MEM 0.2.0, bevor Sie sich auf die Plattform verlassen.
section: Erste Schritte
order: 60
---

# Bekannte Einschränkungen

MEM 0.2.0 besitzt für das erste umfangreiche Release eine bewusst begrenzte Capability-Grenze. Prüfen Sie diese Grenzen, bevor Sie wichtige Kommunikation auf der Plattform betreiben.

## Nur Matrix

MEM 0.2.0 verwaltet Matrix- und Element-Umgebungen. XMPP, IRC, E-Mail, Mattermost und andere Messaging-Systeme werden derzeit nicht verwaltet. Multi-Protokoll-Unterstützung ist keine Funktion von 0.2.0.

## Docker-Modell auf einem Host

Das Release ist für einen herkömmlichen Docker-Host ausgelegt. Es bietet keine Clusterbildung über mehrere Knoten, automatische Ausfallsicherung, Kubernetes-Planung, hochverfügbare PostgreSQL-Cluster oder geografische Replikation. Getrennte Stacks teilen weiterhin den Ausfallbereich des Hosts und ausgewählter Plattformdienste.

## Migrationsumfang

Der aktuelle Migrationsadapter unterstützt ein definiertes älteres MEM-0.1.0-Quellprofil. Er ist kein allgemeiner Importer für jede Synapse-Version, beliebige Compose-Layouts, ESS Community, manuell aufgebaute Matrix-Installationen oder fremde Proxy-Konventionen.

## Clientseitige Verschlüsselungsschlüssel

MEM kann serverseitige Matrix-Daten sichern und wiederherstellen. Ende-zu-Ende-Schlüssel, die Benutzer weder auf Geräten noch im Key-Backup bewahrt haben, kann MEM nicht neu erzeugen. Passwort-Resets und Serverumzüge können deshalb alten verschlüsselten Verlauf unlesbar machen.

## Rechte der Control Plane

Die Control Plane kann Docker-Ressourcen und MEM-eigene Dateien verändern. Eine Kompromittierung der Control Plane kann die verwaltete Umgebung kompromittieren. Halten Sie die Verwaltungsoberfläche privat.

## Meinungsstarker Ingress

Der aktuelle geführte Installer unterstützt Nginx Proxy Manager und einen deSEC-basierten DNS-01-Workflow. Beliebige bestehende Proxies und DNS-Provider sind noch keine erstklassigen geführten Optionen.

## Allgemeine Dienste-Konsole

Die umfassende Services Operations Console gehört nicht zur normalen Oberfläche von 0.2.0. Spezialisierte coturn- und stackbezogene Steuerungen bleiben verfügbar; allgemeiner Docker-Notfallzugriff kann weiterhin Portainer oder Host-Befehle erfordern.

## Dimensionierung und Upstream-Verhalten

Vorprüfungswerte sind kein universeller Größenrechner. Föderation, öffentliche Räume, Medienaufbewahrung, Backups, Migrations-Workspaces und mehrere Stacks können den Ressourcenbedarf deutlich erhöhen.

MEM hängt außerdem vom Verhalten von Matrix, Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn und Docker ab. Prüfen Sie Release Notes und Capability-Status für den exakt unterstützten Build.
