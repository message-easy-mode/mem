---
id: "de/installation/prepare-host"
translationKey: "installation/prepare-host"
locale: "de"
groupId: "installation-de"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Ubuntu-Host vorbereiten"
description: "Bereiten Sie Ubuntu, Ressourcen, Docker, Ports, DNS und privaten Operatorzugriff vor."
order: 10
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Ubuntu", "Docker", "Anforderungen", "Ports", "DNS"]
route: "/docs/de/installation/host-vorbereiten"
aliases: []
outputPath: "docs/de/installation/host-vorbereiten.md"
preserveLegacyBranding: false
---
# Ubuntu-Host vorbereiten

Bereiten Sie den Host vor dem MEM-Bootstrap vor. Das Bootstrap führt eigene Prüfungen durch und kann fehlende Pakete installieren. Speicher, Ports, DNS und der private Zugriff sollten trotzdem vorher geplant sein.

## Unterstützter Bootstrap-Host

Das mitgelieferte MEM-0.2.0-Bootstrap akzeptiert derzeit:

- Ubuntu 22.04, 24.04 oder 26.04;
- `amd64` / `x86_64`;
- `arm64` / `aarch64` nur, wenn alle benötigten Release-Images für diese Architektur verfügbar sind.

Ubuntu 24.04 LTS ist das normale Release-Ziel.

Das Skript muss über `sudo` als root ausgeführt werden.

## Ressourcenanforderungen des Bootstraps

Das Bootstrap verwendet strengere Grenzwerte als die spätere Browser-Prüfung:

- **2 CPU-Kerne** werden empfohlen; weniger erzeugt eine Warnung;
- **3.500 MB RAM** sind das harte Minimum;
- ungefähr **7.800 MB RAM** werden empfohlen;
- **20.000 MB freier Speicher** auf `/` sind das harte Minimum;
- ungefähr **50.000 MB freier Speicher** werden empfohlen.

Die Browser-Prüfung meldet zusätzlich niedrigere Schwellen für frühe Tests. Ein dortiger Erfolg ersetzt weder die Bootstrap-Grenzen noch eine echte Kapazitätsplanung.

Matrix-Historie, Medien, PostgreSQL, Images, Diagnosen, Backups, Restores und Migrationsarbeitsbereiche können deutlich mehr Speicher benötigen.

## Ubuntu-Server-Speicher prüfen

Bei Ubuntu Server mit geführtem LVM muss geprüft werden, ob das unter `/` eingebundene Logical Volume die beabsichtigte Plattenkapazität tatsächlich nutzt. Bei einer 50-GB-Platte kann sonst ungefähr die Hälfte der Volume Group unzugewiesen bleiben, während `/` nur etwa 24 GB erhält. MEM bewertet korrekt den auf `/` sichtbaren freien Speicher und nicht ungenutzte LVM-Extents.

Bearbeiten Sie vor Abschluss der Ubuntu-Installation auf der Seite **Storage configuration** `ubuntu-lv`, damit ein einfacher dedizierter MEM-Server im Wesentlichen die gesamte Kapazität von `ubuntu-vg` nutzt. Nach der Installation prüfen Sie `df -hT /`, `sudo vgs` und `sudo lvs`.

Die vollständigen Hinweise zu LVM, Dateisystem, Firewall und Split DNS finden Sie unter [Ubuntu Server für eine lokale MEM-Installation vorbereiten](ubuntu-server-on-prem.md).

## Produktions-Datenpfade

Das offizielle Servermodell trennt:

```text
/data                         privater Control-Plane-Zustand im Docker-Volume
/var/lib/message-easy-mode    hostsichtbare MEM-Betriebsdaten
/opt/mem                      installierte Software/Laufzeitpakete
```

Neue Produktions-Stackdaten dürfen nicht vom Home-Verzeichnis des Installationsbenutzers abhängen.

## Host-Pakete

Das Bootstrap prüft und installiert bei Bedarf:

```text
ca-certificates
curl
gnupg
lsb-release
jq
dnsutils
iproute2
net-tools
libsecret-tools
```

`libsecret-tools` stellt `secret-tool` bereit. Die Linux-MEM-CLI verwendet es zusammen mit einem Secret-Service-kompatiblen Schlüsselbund.

## Docker

Das Bootstrap kann Docker Engine, containerd, Buildx und das Docker-Compose-Plugin aus dem offiziellen Docker-Apt-Repository installieren.

Bei vorhandenem Docker prüfen Sie:

```bash
sudo docker info
sudo docker compose version
```

Verwenden Sie `--skip-docker-install` nur, wenn Docker und Compose bereits vollständig vorhanden sind und das Bootstrap bei Fehlen abbrechen soll.

Der Docker-Convenience-Script-Weg ist für Entwicklung und Tests vorgesehen. Das offizielle Apt-Repository ist der normale Weg.

## Ports und privater Zugriff

Planen Sie:

- den privaten Installer-Port, standardmäßig TCP **8443**;
- öffentliches HTTPS auf TCP **443** für Matrix und Element über Nginx Proxy Manager;
- optional öffentliches TCP **80** nur dann, wenn bewusst eine HTTP-/Weiterleitungs-Topologie gewünscht ist; der geführte deSEC-DNS-01-Pfad benötigt Port 80 nicht für die Zertifikatsausstellung;
- TCP **81** für die NPM-Verwaltung, ebenfalls eingeschränkt;
- TURN auf TCP/UDP **3478** sowie UDP **49160-49200** für Produktions-Relay von Sprache und Video.

Die Browser-Prüfung beobachtet außerdem 8080 und 5432. PostgreSQL und weitere Verwaltungs-/Datenports bleiben privat.

Legen Sie vorher fest, wie der Operator den privaten Port erreicht:

- vertrauenswürdiges LAN;
- Tailscale, WireGuard oder anderes VPN;
- genehmigtes Management-Netz;
- lokaler SSH-Tunnel.

Eine öffentliche IP und eine geheime URL sind keine ausreichende Sicherheitsgrenze.

## On-Premises-DNS und NAT

Für öffentliche Dienste muss DNS auf die Adresse zeigen, über die Clients den Server erreichen können. Im internen LAN kann Split DNS dieselben TURN-/Matrix-/Element-Namen bewusst direkt auf die private Serveradresse auflösen. Dadurch hängen lokale Funktionsprüfungen nicht von NAT-Reflection ab.

DNS öffnet keine Ports und erstellt keine NAT-Regeln. Befindet sich der Server hinter Firewall/Router, benötigt öffentliche TURN-Erreichbarkeit weiterhin die passende Firewall-/NAT-Regel für TCP/UDP 3478 und UDP 49160-49200.

## Domain und DNS

Bereiten Sie vor:

- eine kontrollierte Basisdomain;
- ein deSEC-Konto und Token mit Berechtigung für die Zone;
- eine ACME-Kontaktadresse;
- Kontrolle über spätere A-/AAAA-Einträge für Matrix und Element.

Der geführte Weg unterstützt derzeit deSEC DNS-01 und ein Wildcard-Zertifikat. Andere DNS-Anbieter und vorhandene Reverse Proxies sind in MEM 0.2.0 keine vollständig geführten Optionen.

## Bereits verwendeter Host

Dokumentieren Sie vor Änderungen:

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

Wenn `mem-api`, `mem-web` oder ein älteres Matrix-Easy-Mode-Compose-System vorhanden ist, stoppen Sie und verwenden Sie die Migration.
