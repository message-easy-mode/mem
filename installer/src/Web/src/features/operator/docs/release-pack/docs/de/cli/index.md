---
title: MEM CLI verwenden
description: Verwenden Sie den versionsgebundenen mem-Befehl für benannte Operatoraktionen, Backup-Übertragung, Restore-Arbeit, JSON-Nachweise und browserunabhängige Fehlerbehebung.
section: CLI und Automatisierung
order: 40
---

# MEM CLI verwenden

Die MEM CLI ist die skriptfähige Operatorschnittstelle der MEM Control Plane. Der installierte Befehl lautet:

```bash
mem
```

## Ergebnis

Nach diesem Abschnitt können Sie:

- die CLI auf die richtige private Control Plane ausrichten;
- ein benanntes CLI-Gerät im Browser autorisieren;
- Konto-, Host- und Chatserverzustand prüfen;
- Material des Sicherungskatalogs übertragen und untersuchen;
- unterstützte Aktionen im Wiederherstellungsarbeitsbereich ausführen;
- stabile JSON-Nachweise sammeln, ohne von der React-Oberfläche abhängig zu sein.

## Aktuelle Grenze von MEM 0.2.0

Die CLI verwendet dieselbe serverseitige Identität, Rollen-, Capability-, Audit- und Step-up-Policy wie der Browser. Ausführung auf dem Ubuntu-Host, per SSH oder auf einer privaten Arbeitsstation ist keine Vertrauensabkürzung.

Die aktuelle Befehlsoberfläche ist bewusst kleiner als die Browseroberfläche:

| Verfügbar | Derzeit nicht verfügbar |
|---|---|
| Profile, Sprache, Geräteanmeldung, Kontostatus, Abmeldung | Chatserver erstellen oder entfernen |
| Hoststatus und Stack-Prüfung, Doctor und Verlauf | Stack starten oder stoppen |
| Sicherungskatalog auflisten, prüfen, exportieren, importieren und löschen | Neue Sicherung erfassen |
| Wiederherstellungsarbeitsbereich prüfen und unterstützte Aktionen ausführen | Migrationssitzungen steuern |
| Stabile JSON-Ausgabe | Globale Diagnose und Seq steuern |

Verwenden Sie für Aufgaben, die `mem --help` nicht aufführt, die Control Plane. Erfinden Sie keine Befehle aus UI-Beschriftungen.

## Erster Operatorablauf

```bash
mem --version

mem profile create home \
  --server https://mem.example.internal

mem profile select home
mem login --device --profile home
mem account show --profile home
mem host status --profile home
```

Für lokale Entwicklung auf dem Control-Plane-Host ist explizites Loopback-HTTP zulässig:

```bash
mem profile create local --server http://127.0.0.1:7105
```

Produktive und private entfernte Profile müssen HTTPS verwenden.

## Befehlsfamilien

```text
mem config ...
mem profile ...
mem login --device
mem account show
mem logout
mem host status
mem stack ...
mem backups ...
mem restores ...
mem --version
```

Die Singularwurzeln `backup` und `restore` bleiben Parser-Kompatibilitätsaliasse. Neue Dokumentation und Skripte sollen `backups` und `restores` verwenden.

## Menschliche Ausgabe und JSON

Lesbare Ausgabe kann Englisch oder Deutsch sein. Befehlsnamen, Optionen, Kennungen, JSON-Feldnamen, Maschinenstatus und Fehlercodes bleiben stabiles Englisch.

```bash
mem host status --profile home --language de
mem host status --profile home --json | jq
```

`mem login --device` ist interaktiv und unterstützt bewusst kein `--json`.

## Bei einer Störung

Beginnen Sie mit nur lesbaren Nachweisen:

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

Exit-Code `2` bedeutet meist: Der Befehl lief, aber das angeforderte Betriebsergebnis war nicht gesund, bereit, gefunden oder abgeschlossen. Bewahren Sie JSON und Exit-Code auf.

## Als Nächstes lesen

1. [MEM CLI installieren](installieren.md).
2. [Profile anlegen](profile.md).
3. [Mit Geräteanmeldung anmelden](geraeteanmeldung.md).
4. [JSON sicher verwenden](json-und-skripting.md).
5. [CLI-Sicherheitsmodell verstehen](sicherheitsmodell.md).
