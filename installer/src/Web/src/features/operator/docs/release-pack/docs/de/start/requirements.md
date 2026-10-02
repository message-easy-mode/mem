---
title: Anforderungen und unterstützte Umgebung
description: Prüfen Sie vor der Installation die tatsächliche Bootstrap-Mindestgrenze, Ubuntu-Zielumgebung, Docker, Speicher, Netzwerk, DNS und privaten Operatorzugriff.
section: Erste Schritte
order: 50
---

# Anforderungen und unterstützte Umgebung

MEM führt mehrere Prüfungen aus. Bereiten Sie den Host jedoch nach der **Bootstrap-Mindestgrenze** vor und nicht nur nach den niedrigeren Browser-Preflight-Warnungen.

## Primäres Release-Ziel

Das normale MEM-0.2.0-Releaseziel ist **Ubuntu Server 24.04 LTS auf amd64/x86_64**.

Der Bootstrap kann weitere unterstützte Ubuntu-LTS-Versionen und Architekturen erkennen. Eine bestandene Architekturprüfung beweist jedoch nicht, dass jedes Upstream-Image oder Release-Artefakt für diese Architektur verfügbar ist. Prüfen Sie vor ARM-Einsatz die Release-Capability-Matrix.

## Bootstrap-Mindestgrenze

Der offizielle Bootstrap verwendet derzeit:

- weniger als **2 CPU-Kerne** → Warnung;
- weniger als **3.500 MB RAM** → Abbruch;
- etwa **7.800 MB RAM** → empfohlene Größe;
- weniger als **20.000 MB frei auf `/`** → Abbruch;
- etwa **50.000 MB frei auf `/`** → empfohlene Größe.

Der spätere Browser-Preflight kann niedrigere Warnschwellen für frühe Tests anzeigen. Diese ersetzen weder die Bootstrap-Grenze noch eine sinnvolle Produktionsdimensionierung.

Synapse-Historie, Medien, PostgreSQL, Images, Logs, Diagnosen, Backups, Restores und Migrationsarbeitsbereiche können deutlich mehr Kapazität benötigen.

Hinweise zu Ubuntu-LVM und Festplattenzuweisung finden Sie unter [Ubuntu Server für eine lokale MEM-Installation vorbereiten](../installation/ubuntu-server-on-prem.md).

## Docker

Docker muss installiert und über den Docker-Socket für die Control Plane erreichbar sein. Auf einem frischen Ubuntu-Host kann der Bootstrap die unterstützten Docker-Engine- und Compose-Plugin-Pakete installieren.

Docker-Socket-Zugriff ist hoch privilegiert. Halten Sie die Control Plane in einem vertrauenswürdigen LAN, VPN oder per SSH-Weiterleitung statt sie öffentlich freizugeben.

## Netzwerk und öffentliche Dienste

Für öffentliches Matrix- und Element-HTTPS wird TCP **443** benötigt. TCP **80** ist optional, wenn bewusst eine HTTP-/Weiterleitungs-Topologie verwendet wird; der aktuelle geführte deSEC-DNS-01-Zertifikatspfad benötigt Port 80 nicht für die Zertifikatsausstellung. Produktions-TURN verwendet zusätzlich:

```text
3478/tcp
3478/udp
49160-49200/udp
```

Die MEM Control Plane, die NPM-Verwaltung, PostgreSQL und weitere Verwaltungs-/Datenports sind private Oberflächen und sollten nicht breit aus dem Internet erreichbar sein.

## Domain, DNS und Zertifikate

Bereiten Sie Domain, DNS-Provider-Zugriff und ACME-Kontaktadresse vor. Der aktuelle geführte Pfad verwendet Nginx Proxy Manager und deSEC DNS-01 für Wildcard-Zertifikate.

Bei On-Premises-Betrieb sollte entschieden werden, ob interne Clients Split DNS verwenden sollen, damit öffentliche Dienstnamen im LAN direkt auf die private Serveradresse auflösen.

## Operator-Arbeitsplatz und Backups

Die Browserverwaltung benötigt Zugriff auf die private Control Plane. Unter Linux verwendet die Geräteanmeldung der MEM CLI `secret-tool` und einen funktionierenden Secret-Service-kompatiblen Schlüsselbund für den aufrufenden Nicht-Root-Benutzer.

Bewahren Sie mindestens eine portable Backup-Kopie außerhalb des MEM-Hosts auf. Ein Backup nur auf der ausgefallenen Serverplatte ist keine Disaster-Recovery-Kopie.
