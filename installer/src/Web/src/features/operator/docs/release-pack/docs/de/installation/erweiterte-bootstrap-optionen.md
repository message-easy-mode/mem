---
title: Erweiterte Bootstrap-Optionen
description: Verwenden Sie unterstützte Bootstrap-Overrides für Entwicklung, Release-Prüfung, private Ports oder vorhandenes Docker.
section: Installation
order: 25
---

# Erweiterte Bootstrap-Optionen

Verwenden Sie das normale Stable-Bootstrap, außer Sie entwickeln MEM, prüfen ein Release oder ändern bewusst den privaten Control-Plane-Zugang.

## Verwaltungsmodus ändern

SSH/local-only ist Standard:

```bash
sudo ./install.sh --control-plane-access ssh
```

Trusted LAN wird ausdrücklich gewählt:

```bash
sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

Die Adresse muss eine konkrete RFC1918-IPv4-Adresse auf einer geeigneten Host-Schnittstelle sein. Wildcard- oder öffentlich routbare Bindungen werden nicht akzeptiert.

## Privaten Port ändern

```bash
sudo ./install.sh --control-plane-port 9443
```

Passen Sie SSH-Tunnel oder private LAN-URL an. Eine andere Portnummer erlaubt keine öffentliche Veröffentlichung.

## Vorhandene private Bindung ändern

Ein erneuter Bootstrap-Lauf behält eine sichere Loopback- oder Trusted-LAN-Bindung bei, solange keine ausdrückliche Änderung angefordert wird. Änderungen erfolgen als geprüfte Container-Neuerstellung mit erhaltenem Zustand und anschließender Bindungsprüfung.

## Weitere Release-/Entwicklungsoptionen

Dazu gehören `--channel`, `--control-plane-image`, CLI-Payload-Overrides, `--skip-docker-install`, `--allow-low-disk`, `--use-docker-convenience-script`, `--dry-run` und `--yes`.

`--yes` umgeht keine Validierung der privaten Adresse.

## Nicht unterstützte erweiterte Wege

MEM 0.2.0 unterstützt nicht:

- `0.0.0.0:<port>` oder `[::]:<port>` für die Control Plane;
- direkte Bindung an eine öffentliche/nicht-private IP;
- öffentlichen MEM-Admin-DNS oder eine öffentliche NPM-Route;
- Legacy-`.env` / `stack.sh` für die 0.2.0-Anwendung;
- getrennte manuelle `mem-api`- und `mem-web`-Starts;
- Umgehung der dauerhaften Installations- oder Exposure-Validierung.
