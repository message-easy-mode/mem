---
title: Private Control Plane öffnen und Setup-Code verwenden
description: Öffnen Sie die selbstsignierte private Control Plane sicher und verwenden Sie den einmaligen Setup-Code für den First-Owner-Bootstrap.
section: Installation
order: 30
---

# Private Control Plane öffnen und Setup-Code verwenden

Nach dem Start von `mem-control-plane` zeigt das Bootstrap den unterstützten privaten Verwaltungsweg, die Browser-Adresse, bei Bedarf den SSH-Befehl sowie den SHA-256-Fingerprint des Control-Plane-Zertifikats an.

## SSH-Tunnel / local-only — Standard

Für einen Internet-/VPS-Host bindet MEM die Control Plane ausschließlich an Host-Loopback.

```bash
ssh -N -o ExitOnForwardFailure=yes \
  -L 127.0.0.1:8443:127.0.0.1:8443 \
  <operator>@<server>
```

Danach öffnen Sie:

```text
https://127.0.0.1:8443
```

Auch die lokale Seite des SSH-Forwards ist an Loopback gebunden. In diesem Modus gibt es keine unterstützte direkte öffentliche Control-Plane-URL.

## Trusted LAN — ausdrückliche Auswahl

Auf einem On-Premises-Host, einer Proxmox-VM, im Home-Lab oder Management-VLAN kann MEM an genau eine ausgewählte RFC1918-Adresse binden, zum Beispiel:

```text
https://192.168.10.20:8443
```

Verwenden Sie dafür möglichst eine statische Adresse oder DHCP-Reservierung. Veröffentlichen Sie diesen Port nicht über Internet-NAT, öffentliche DNS-Namen oder Nginx Proxy Manager.

SSH bleibt als Break-Glass-/privater Tunnelweg verfügbar.

## Erwartete Browserwarnung

MEM verwendet für die private Control Plane ein lokal erzeugtes selbstsigniertes Zertifikat. Eine Browserwarnung ist normal.

Vergleichen Sie vor dem Akzeptieren den vom Bootstrap angezeigten SHA-256-Fingerprint.

Dieses Zertifikat ist unabhängig vom öffentlichen Wildcard-Zertifikat für Matrix und Element.

## MEM-Anmeldung bleibt erforderlich

Der private Netzwerkzugang ersetzt nicht die MEM-Authentifizierung. Benannte Anmeldung, TOTP-MFA, Rollen, Sitzungsregeln und Step-up für Hochrisikoaktionen gelten weiterhin.

## Setup-Code verwenden

Geben Sie den starken `mem_...`-Code beim First-Owner-Bootstrap ein und behandeln Sie ihn wie ein Passwort.

Er kann nur einen kurzlebigen Bootstrap-Grant erzeugen, solange noch kein abgeschlossener Platform Owner existiert. Erstellen Sie danach sofort den Platform Owner, richten Sie TOTP ein und speichern Sie Recovery-Codes sicher.

## Aktuelle Exposition prüfen

Home → Host status, Diagnostics → Control Plane runtime und System Information zeigen die tatsächlich beobachtete Docker-Hostbindung.

Wildcard- oder öffentliche Drift wird nicht still akzeptiert, sondern als Diagnostics-Fehlerincident gemeldet.
