---
title: Source Assistant installieren und privat öffnen
description: Installieren Sie MEM Migrate auf dem alten Host, starten Sie den Source Assistant und greifen Sie per Loopback oder SSH-Tunnel zu.
section: Von MEM 0.1.0 migrieren
order: 20
---

# Source Assistant installieren und privat öffnen

Der MEM-Migrate-Bootstrap installiert eine verifizierte Version auf dem alten Quellhost, ohne Git, .NET, Node.js, npm oder einen Source-Checkout zu benötigen.

## Ergebnis

MEM Migrate ist unter `/opt/mem/migrate` installiert, dauerhafter Arbeitszustand bleibt unter `/var/lib/mem-migrate`, und der Source Assistant ist nur über eine private lokale Verbindung erreichbar.

## Unterstützter Bootstrap-Host

Der aktuelle Bootstrap unterstützt Ubuntu Server 24.04 auf amd64/x86_64. Ein bestehender erreichbarer Docker-Daemon ist erforderlich. Der Bootstrap prüft Docker, installiert oder verändert Docker jedoch nicht, startet keine Container neu und verändert den alten Stack nicht.

Ein funktionierender `age`-Befehl bleibt erhalten; andernfalls wird bei Bedarf das unterstützte Ubuntu-Paket installiert.

## Gehosteten Bootstrap ausführen

Verwenden Sie den MEM-Migrate-Release-Host, der mit den MEM-0.2.0-Release-Artefakten angegeben wird. Erfinden oder ersetzen Sie die Download-Adresse nicht durch eine nicht vertrauenswürdige Quelle.

Zuerst den nicht verändernden Plan ausführen:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host> \
      --dry-run
```

Prüfen Sie Betriebssystem, Architektur, Docker-Erreichbarkeit, `age`-Zustand, angeforderte Version und Installationsplan. Danach dieselbe Version anwenden:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host>
```

Der Installer prüft Manifest, äußere und innere Prüfsummen, Archivwurzel, Eintragstypen und Payload, bevor der aktuelle Release-Link atomar geändert wird. Zustand unter `/var/lib/mem-migrate` bleibt erhalten.

## Source Assistant starten

Auf dem Quellhost in einem Terminal:

```bash
sudo /opt/mem/migrate/current/mem-migrate-web
```

Die sicheren Standardwerte lauschen auf `127.0.0.1:7391`. Das Terminal zeigt den Start-Zugriffscode. Lassen Sie dieses Terminal während der Nutzung verfügbar.

Die aktuelle Version verspricht noch keine stabilen Host-Befehle `mem-migrate-start`, `mem-migrate-stop` oder `mem-migrate-status`. Verlassen Sie sich nicht auf diese Namen, solange die installierte Version sie nicht bereitstellt. Beenden Sie den Vordergrundprozess nach Abschluss der Quellarbeit mit `Ctrl+C`.

## Über SSH-Tunnel öffnen

Auf Ihrem Betreiber-Arbeitsplatz:

```bash
ssh -N \
  -L 7391:127.0.0.1:7391 \
  <operator>@<source-host>
```

Dann öffnen:

```text
http://localhost:7391
```

Geben Sie im Feld **Access code** den vom Source-Assistant-Prozess ausgegebenen Zugriffscode ein.

> [!WARNING]
> Öffentliche Internetfreigabe wird nicht unterstützt. Binden Sie den Source Assistant nicht an eine Wildcard-Adresse und erstellen Sie keine öffentliche Reverse-Proxy-Route.

## Erfolg prüfen

- Der Browser zeigt die Zugriffsseite des Source Assistant.
- Der Zugriffscode öffnet den Arbeitsbereich.
- Die erste normale Aktion ist die Quellbewertung.
- Ein Browser-Refresh löscht keine dauerhaften Bewertungs-, Erfassungs-, Paket- oder Journalzustände.

Wenn keine Verbindung möglich ist, prüfen Sie den laufenden Quellprozess, die offene SSH-Sitzung und eine mögliche Belegung des lokalen Ports `7391`.

Weiter: [Quelle bewerten und einen Stack auswählen](assess-and-select.md).
