---
id: "de/installation/ubuntu-server-on-prem"
translationKey: "installation/ubuntu-server-on-prem"
locale: "de"
groupId: "installation-de"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Ubuntu Server für eine lokale MEM-Installation vorbereiten"
description: "Bereiten Sie Ubuntu-Server-Speicher, Netzwerk, DNS und Firewall für eine produktionsnahe lokale MEM-Installation vor."
order: 5
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Ubuntu Server", "On-Premises", "LVM", "DNS", "TURN", "Firewall"]
route: "/docs/de/installation/ubuntu-server-on-prem"
aliases: []
outputPath: "docs/de/installation/ubuntu-server-on-prem.md"
preserveLegacyBranding: false
---
# Ubuntu Server für eine lokale MEM-Installation vorbereiten

Diese Seite fasst die praktischen Vorbereitungen zusammen, die vor einer echten MEM-Installation auf Ubuntu Server wichtig sind. Sie ergänzt [Ubuntu-Host vorbereiten](host-vorbereiten.md) um Speicher- und LAN-Details, die bei einer frischen VM leicht übersehen werden.

## Empfohlener Ausgangspunkt

Für den normalen MEM-0.2.0-Releasepfad verwenden Sie:

- Ubuntu Server **24.04 LTS** auf `amd64` / `x86_64`;
- mindestens **3.500 MB RAM** für den Bootstrap, etwa **7.800 MB** empfohlen;
- mindestens **20.000 MB freien Speicher auf `/`**, etwa **50.000 MB** empfohlen;
- eine stabile private Serveradresse bei Trusted-LAN-Verwaltung;
- funktionierendes öffentliches DNS und bei On-Premises-Installationen einen bewussten internen DNS-Plan.

## Ubuntu Guided LVM: sicherstellen, dass `/` die Platte wirklich nutzt

Die geführte LVM-Installation von Ubuntu Server kann fast die gesamte virtuelle Platte der Volume Group zuweisen, aber nur einen Teil davon dem Root Logical Volume. Eine 50-GB-Test-VM kann dann ungefähr so aussehen:

```text
virtuelle Platte             50 GB
ubuntu-vg                    ~48 GB
ubuntu-lv unter /            ~24 GB
frei innerhalb ubuntu-vg     ~24 GB
```

MEM prüft den freien Speicher, der auf `/` sichtbar ist. Nicht zugewiesener Platz innerhalb der Volume Group zählt dafür nicht.

Prüfen Sie auf der Ubuntu-Seite **Storage configuration** vor **Done** das Logical Volume, das unter `/` eingebunden wird. Für einen einfachen dedizierten MEM-Server sollte `ubuntu-lv` im Wesentlichen die gesamte verfügbare Kapazität von `ubuntu-vg` verwenden.

Nach der Installation prüfen Sie:

```bash
df -hT /
sudo vgs
sudo lvs
```

Wenn das Root Logical Volume auf einem dedizierten Server bereits zu klein angelegt wurde und die Volume Group noch freie Extents besitzt, lautet die übliche LVM-Erweiterung:

```bash
sudo lvextend -l +100%FREE -r /dev/ubuntu-vg/ubuntu-lv
```

Prüfen Sie die Gerätenamen vorher. Verwenden Sie den Befehl nicht ungeprüft auf einem Host mit anderer LVM-Struktur.

## Produktions-Dateisystemmodell

Eine unterstützte Produktionsinstallation trennt privaten Control-Plane-Zustand, hostsichtbare Laufzeitdaten und installierte Software:

```text
/data
    privater Control-Plane-Zustand im persistenten
    Docker-Volume mem-control-plane-data

/var/lib/message-easy-mode
    servereigene, hostsichtbare MEM-Daten
    ├── mem-data
    ├── instances
    ├── platform/coturn
    └── seq

/opt/mem
    installierte MEM-Software und Laufzeitpakete
```

Die offizielle Produktionslaufzeit bindet `/var/lib/message-easy-mode` unter demselben absoluten Pfad in die Control Plane ein. So adressieren Control Plane und Host-Docker-Daemon dieselben physischen Stack-Dateien.

Eine neue Produktionsinstallation darf für neue Laufzeitdaten nicht vom Home-Verzeichnis des Login-Benutzers abhängen. Leiten Sie Produktions-Stackdaten nicht auf `/home/<user>/mem-data` um.

## Privater Control-Plane-Zugriff

Für einen lokalen Server verwenden Sie einen unterstützten privaten Verwaltungsmodus:

- **Trusted LAN** — Bindung an genau eine ausgewählte RFC1918-Adresse;
- **SSH / local-only** — Bindung an Loopback und Zugriff per SSH-Portweiterleitung.

Veröffentlichen Sie den Control-Plane-Port nicht über öffentliches DNS, Internet-NAT oder Nginx Proxy Manager.

## Öffentliche Dienstports

Der verwaltete öffentliche Dienst benötigt normalerweise:

```text
443/tcp                HTTPS Matrix und Element
3478/tcp               TURN
3478/udp               TURN
49160-49200/udp        TURN-Relay-Bereich
```

TCP 80 ist optional, wenn bewusst ein HTTP-/Weiterleitungspfad verwendet wird. Der aktuelle geführte deSEC-DNS-01-Zertifikatspfad benötigt öffentliches Port 80 nicht für die ACME-Ausstellung.

Die Nginx-Proxy-Manager-Verwaltung auf TCP 81 und die MEM Control Plane auf TCP 8443 sind Verwaltungsoberflächen und sollten privat bzw. eingeschränkt bleiben.

## DNS und Split DNS

Öffentliches DNS sollte Matrix-, Element- und TURN-Namen auf die Adresse zeigen lassen, über die Internet-Clients den Server erreichen.

Im internen LAN ist Split DNS oft sinnvoll, damit derselbe Dienstname direkt auf die private Serveradresse zeigt, zum Beispiel:

```text
öffentliches DNS
turn.example.org  -> öffentliche WAN-Adresse

interner Resolver
turn.example.org  -> 10.10.0.198
```

Dadurch hängen lokale Dienstprüfungen nicht unnötig von NAT-Reflection/Hairpin-Verhalten ab.

Eine DNS-Überschreibung ändert nur die Namensauflösung. Sie öffnet **keine** Firewall-Ports und richtet **keine** NAT-/Portweiterleitung ein.

## Vor dem MEM-Bootstrap

Prüfen Sie die Basisdaten direkt auf dem neuen Server:

```bash
. /etc/os-release
echo "$PRETTY_NAME"
dpkg --print-architecture
df -hT /
ip -4 -br addr
getent ahostsv4 turn.example.org
```

Wenn Docker auf einem wirklich frischen Host noch nicht installiert ist, ist das erwartbar; das MEM-Bootstrap kann die unterstützten Docker-Pakete installieren.
