---
title: MEM 0.2.0 installieren
description: Unterstützter Neuinstallationsweg vom Ubuntu-Bootstrap bis zur Übergabe der verwalteten Plattform.
section: Installation
order: 0
---

# MEM 0.2.0 installieren

Diese Anleitung beschreibt eine **neue MEM-0.2.0-Installation** auf einem unterstützten Ubuntu- und Docker-Host.

MEM 0.2.0 verwendet zwei Installationsstufen:

1. Das Host-Bootstrap bereitet Ubuntu, Docker, die private MEM Control Plane, ihren privaten Verwaltungszugang und den einmaligen Setup-Code vor.
2. Der First-Owner-Bootstrap erstellt den benannten Platform Owner mit MFA. Danach prüft die authentifizierte Control Plane den Host, installiert und verifiziert die verwaltete Plattform und übergibt in den normalen Operatorbetrieb.

> [!IMPORTANT]
> **MEM 0.2.0 unterstützt keine direkte Veröffentlichung der Control Plane auf einer öffentlich routbaren Netzwerkschnittstelle.**
>
> In ausdrücklich ausgewählten vertrauenswürdigen privaten Netzen ist direkter LAN-Zugriff unterstützt. Ansonsten ist SSH-Tunneling der unterstützte Weg für Remote-Verwaltung.

## Zustand der Control Plane und vorhandene MEM-0.2.0-Laufzeiten

Ein neues MEM-0.2.0-Bootstrap speichert den dauerhaften Zustand der Control Plane im Docker-Volume `mem-control-plane-data`. Benennen, kopieren oder ersetzen Sie dieses Volume nicht manuell; der Zustand muss Container-Neuerstellung und Upgrades überstehen.

Frühere Entwicklungs- und Vorschauinstallationen von MEM 0.2.0 verwendeten den Laufzeitnamen `mem-installer` und das Volume `mem-installer-data`. Das aktuelle Bootstrap erkennt genau diese Legacy-Laufzeit, zeigt eine geprüfte Migration zu `mem-control-plane`, verwendet das vorhandene Volume und die Zertifikatspfade weiter, wendet die aktuelle Richtlinie für private Administration an und verifiziert die Ersatzlaufzeit, bevor der alte Container-Eintrag entfernt wird. Eine aktualisierte Installation kann daher den Legacy-Volume-Namen `mem-installer-data` dauerhaft weiterverwenden.

Schlägt die Verifikation fehl, verwendet das Bootstrap die geprüfte Rollback-Richtlinie. Eine Wildcard- oder öffentlich routbare Control-Plane-Bindung darf nicht stillschweigend erneut geöffnet werden, nur um die Verfügbarkeit wiederherzustellen.

## Installationsablauf

1. Bei einem lokalen VM-/Server-Setup zuerst [Ubuntu Server für eine lokale MEM-Installation vorbereiten](ubuntu-server-on-prem.md), danach [Ubuntu-Host vorbereiten](host-vorbereiten.md)
2. [Bootstrap-Installer ausführen](bootstrap-installer.md)
3. [Private Control Plane öffnen und Setup-Code verwenden](privater-zugriff-und-setup-code.md)
4. [Ersten Platform Owner erstellen](erster-platform-owner.md)
5. [Serverprüfung ausführen](server-pruefen.md)
6. [Öffentliche Domain und Zertifikatsplan wählen](domain-und-zertifikat.md)
7. [Installationsplan prüfen und ausführen](pruefen-und-installieren.md)
8. [Plattform verifizieren und Übergabe abschließen](verifizieren-und-uebergeben.md)

## Wo Produktionsdaten liegen

Das unterstützte Serverlayout trennt drei Bereiche:

```text
/data                         privater Control-Plane-Zustand in mem-control-plane-data
/var/lib/message-easy-mode    hostsichtbare MEM-Laufzeit-/Stackdaten
/opt/mem                      installierte Software und Laufzeitpakete
```

Die Same-Path-Bindung von `/var/lib/message-easy-mode` gehört zum Produktionsvertrag zwischen Docker-Host und Control Plane. Neue Produktionsressourcen sollten nicht im Home-Verzeichnis eines Login-Benutzers angelegt werden.

## Was die Neuinstallation erstellt

Das Host-Bootstrap erstellt die private `mem-control-plane`-Laufzeit und deren dauerhaften Zustand. Die authentifizierte Ersteinrichtung bereitet unter anderem `mem-gateway`, `mem-postgres`, `mem-npm`, den gemeinsamen Coturn-Dienst, Volumes, Zertifikate sowie Installations- und Diagnosenachweise vor.

Getrennte `mem-api`- oder `mem-web`-Anwendungscontainer werden nicht erstellt. Der erste Matrix- und Element-Stack wird nach der Plattformübergabe erstellt.

## Sicherheitsgrenze

Die Control Plane besitzt Zugriff auf den Docker-Socket und ist hoch privilegiert. MEM behandelt ihre Netzwerkkonnektivität daher als Release-Sicherheitsgrenze.

```text
Internet/VPS
→ nur 127.0.0.1
→ SSH-Tunnel

Ausdrücklich vertrauenswürdiges LAN
→ eine ausgewählte RFC1918-Hostadresse
→ direktes privates HTTPS
```

Ein öffentlicher MEM-Admin-Hostname oder eine öffentliche NPM-Route für die Control Plane ist nicht erforderlich.
