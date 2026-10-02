---
title: MEM CLI installieren
description: Prüfen oder installieren Sie den root-eigenen mem-Hostbefehl, stellen Sie Secret Service bereit und beachten Sie die aktuelle Release-Packaging-Grenze.
section: CLI und Automatisierung
order: 41
---

# MEM CLI installieren

## Ergebnis

Eine korrekte Installation stellt einen stabilen Befehl bereit:

```text
/opt/mem/cli/<version>/mem
/usr/local/bin/mem -> /opt/mem/cli/<version>/mem
```

Die versionierte Binärdatei gehört `root` und ist ausführbar. Normale Operatoren führen `mem` ohne `sudo` aus.

## Prüfen, ob das Release die CLI installiert hat

```bash
command -v mem
mem --version
mem --help
ls -l /usr/local/bin/mem
```

Nehmen Sie nicht an, dass die CLI allein durch die Control-Plane-Installation vorhanden ist. Im aktuellen Quellstand existiert die Installer-Integration, ist aber standardmäßig deaktiviert, bis das Release-Packaging eine vorgebaute CLI-Binärdatei liefert und die Installation aktiviert. Das öffentliche Release-Paket muss geprüft werden, bevor diese Seite als Zusage einer automatischen Installation gilt.

## Linux-Voraussetzung für den Credential Store

Die Geräteanmeldung speichert ihr undurchsichtiges Credential über Secret Service und `secret-tool`:

```bash
sudo apt install libsecret-tools
secret-tool --help
```

Das Paket allein genügt nicht. Das Nicht-root-Konto, das `mem login --device` ausführt, benötigt einen nutzbaren und entsperrten Secret-Service-kompatiblen Schlüsselbund. In einer Headless-SSH-Sitzung fehlt dieser möglicherweise.

MEM schlägt geschlossen fehl, wenn der sichere Speicher nicht verfügbar ist. Es gibt keinen Fallback in Klartextdatei, Profil, Umgebungsvariable, Befehlsargument oder stdin.

## Gelieferte Release-Binärdatei installieren

Verwenden Sie den CLI-eigenen Installer, wenn Release-Binärdatei und passendes Skript vorliegen:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --dry-run \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0
```

Das Skript prüft `secret-tool`, installiert die Binärdatei mit Modus `0755` und aktualisiert `/usr/local/bin/mem` auf die gewählte Version.

## Nur für Entwicklung aus Quellcode veröffentlichen

Vom Repository-Stamm auf einem Entwicklungsrechner mit .NET-8-SDK:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --dry-run \
  --version dev

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --version dev
```

Dadurch wird vor der Installation eine eigenständige `linux-x64`-Single-File-Binärdatei veröffentlicht. Das ersetzt nicht den Nachweis des offiziellen Release-Artefakts.

## Erfolg prüfen

```bash
command -v mem
readlink -f /usr/local/bin/mem
ls -l /opt/mem/cli/<version>/mem
mem --version
mem --help
```

Wenn die Shell noch einen alten Befehl auflöst:

```bash
hash -r
```

## Sicherheit und Fehlerbehandlung

- `sudo` nur für Paketinstallation oder Aktualisierung des root-eigenen Befehls verwenden.
- Profil-, Login-, Backup- und Restore-Befehle nicht mit `sudo` ausführen; root besitzt einen anderen Konfigurations- und Schlüsselbundkontext.
- Die Binärdatei nicht in einen für Operatoren beschreibbaren Produktionspfad kopieren.
- Die `secret-tool`-Prüfung bei normaler Installation nicht überspringen.
- Die vorige Version behalten, bis `mem --version`, `mem --help` und eine Geräteanmeldung bestanden haben.

## Verwandte Dokumentation

- [Profile und Server](profile.md)
- [Mit Geräteanmeldung anmelden](geraeteanmeldung.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
