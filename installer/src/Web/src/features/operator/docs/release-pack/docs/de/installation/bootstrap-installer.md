---
title: Bootstrap-Installer ausführen
description: Validieren Sie Ubuntu, installieren Sie Docker-Voraussetzungen und starten Sie die private Control Plane.
section: Installation
order: 20
---

# Bootstrap-Installer ausführen

Das Host-Bootstrap bereitet den Rechner vor und startet die private MEM Control Plane. Die Installation der verwalteten Plattform erfolgt anschließend im Browser.

## Zuerst trocken prüfen

```bash
sudo ./install.sh --dry-run
```

Der Dry Run prüft Betriebssystem, Ressourcen, Pakete, Docker, Compose, vorhandene Control-Plane-Laufzeit und den privaten Verwaltungsplan ohne Hoständerungen.

## Stabile Control Plane starten

```bash
sudo ./install.sh
```

Das Bootstrap kann unter anderem den dauerhaften Zustand vorbereiten, ein selbstsigniertes Control-Plane-Zertifikat und den Setup-Code erstellen, den privaten Verwaltungsweg auswählen, `mem-control-plane` starten und anschließend Gesundheit sowie die exakte Docker-Hostbindung verifizieren.

## Privaten Verwaltungsweg wählen

Standard ist SSH/local-only:

```text
127.0.0.1:8443 -> 8443/tcp
```

Interaktiv kann MEM geeignete RFC1918-Adressen als ausdrücklich gewähltes Trusted LAN anbieten. Es bindet nur an die ausgewählte Adresse.

Nicht-interaktive Beispiele:

```bash
sudo ./install.sh --control-plane-access ssh

sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

Wildcard-Adressen, öffentliche IPv4-Adressen, Link-Local-Adressen und beliebige nicht zugewiesene private Adressen werden nicht akzeptiert.

## Vorhandene Control Plane

MEM liest die tatsächliche Docker-`HostIp` einer vorhandenen Control Plane.

- Sichere Loopback- und zugewiesene Trusted-LAN-Bindungen werden ohne ausdrückliche Änderung beibehalten.
- Unsichere Wildcard-, öffentliche, veraltete private oder mehrdeutige Bindungen erfordern eine geprüfte Container-Neuerstellung.
- `/data`, Zertifikat, Setup-Autorität und Host-Daten bleiben erhalten.
- Gesundheit und exakte Docker-Hostbindung werden geprüft, bevor der alte Container-Eintrag entfernt wird.

Scheitert die Härtung, darf eine vorher sichere private Laufzeit wieder gestartet werden. Eine vorher unsicher veröffentlichte Laufzeit bleibt für Recovery erhalten, wird aber nicht automatisch wieder öffentlich gestartet.

## Nützliche Optionen

```bash
sudo ./install.sh --dry-run
sudo ./install.sh --yes
sudo ./install.sh --control-plane-access ssh
sudo ./install.sh --control-plane-access trusted-lan --control-plane-bind-address 192.168.10.20
sudo ./install.sh --control-plane-port 9443
sudo ./install.sh --show-setup-token
```

Ein realer Bootstrap-Lauf speichert begrenzte root-only Nachweise unter `/var/log/mem/bootstrap/`. Setup-Code und typische Zugangsdaten werden redigiert; private Schlüssel werden nicht als Nachweis ausgegeben.

## Nicht Teil des unterstützten 0.2.0-Wegs

Das Bootstrap veröffentlicht die Control Plane nicht direkt im Internet, erstellt keine öffentliche NPM-Route für MEM-Administration und benötigt keinen öffentlichen MEM-Admin-DNS-Namen.
