---
id: "de/cli/fehlerbehebung"
translationKey: "cli/troubleshooting"
locale: "de"
groupId: "cli-de"
groupKey: "cli"
groupLabel: "CLI und Automatisierung"
groupOrder: 80
title: "MEM CLI fehlerbeheben"
description: "Arbeiten Sie von Installation über Profil, Schlüsselbund, Login, Erreichbarkeit und Autorisierung bis zu ressourcenspezifischen Nachweisen."
order: 49
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "Fehlerbehebung", "Schlüsselbund", "Autorisierung", "Verbindung"]
route: "/docs/de/cli/fehlerbehebung"
aliases: []
outputPath: "docs/de/cli/fehlerbehebung.md"
preserveLegacyBranding: false
---
# MEM CLI fehlerbeheben

Arbeiten Sie vom lokalen Befehl nach außen. Beginnen Sie nicht mit destruktiven Backup- oder Restore-Aktionen.

## 1. Installierte Binärdatei prüfen

```bash
command -v mem
readlink -f "$(command -v mem)"
mem --version
mem --help
```

Fehlt der Befehl, beachten Sie: Die aktuelle Quelle enthält Installer-Integration, lässt sie aber standardmäßig deaktiviert, solange das Release-Packaging keine Binärdatei liefert und aktiviert. Lesen Sie [MEM CLI installieren](installieren.md).

Bei gecachtem alten Befehl:

```bash
hash -r
```

## 2. Lokale Konfiguration prüfen

```bash
mem profile list --json | jq
mem config get default-profile --json | jq
mem config get language --json | jq
```

```bash
mem profile create home --server https://mem.example.internal
mem profile select home
```

Profilfehler umfassen ungültige Namen, Remote-URLs ohne HTTPS, fehlende Profile, fehlerhaftes JSON, unbekanntes Schema, unsichere symbolische Links und nicht lesbare Konfiguration.

## 3. Schlüsselbund des Nicht-root-Benutzers prüfen

```bash
command -v secret-tool
secret-tool --help
```

Bei Bedarf:

```bash
sudo apt install libsecret-tools
```

Danach zum normalen Benutzer zurückkehren. Login nicht mit `sudo` ausführen. Eine Headless-SSH-Sitzung kann `secret-tool`, aber keine entsperrte Secret-Service-Sitzung besitzen. MEM schreibt bewusst kein Klartext-Credential.

## 4. Kontostatus prüfen

```bash
mem account show --profile home --json | jq
```

Bei Abmeldung oder Ablehnung:

```bash
mem logout --profile home
mem login --device --profile home
mem account show --profile home
```

Nach Neuinstallation der Control Plane müssen installationsgebundene alte Credentials ersetzt werden.

## 5. Private Erreichbarkeit prüfen

```bash
mem host status --profile home --json | jq
```

Prüfen Sie Profil-URL, privates DNS, VPN oder Managementnetz, TLS-Zertifikat und Reverse Proxy. Verwenden Sie keine öffentliche Matrix- oder Element-URL.

HTTP 401 bedeutet abgewiesene Gerätesitzung. HTTP 403 bedeutet unzureichende Rolle oder aktuelle Verifikation. Die CLI akzeptiert keine Passwort- oder TOTP-Step-up-Daten in der Shell.

## 6. Auf die betroffene Ressource eingrenzen

Chatserver:

```bash
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Sicherungskatalog:

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

Wiederherstellungsarbeitsbereich:

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

## Häufige Symptome

### `cli_profile_required`

Betriebs-Credentials sind an benanntes Profil und Server gebunden. Ein Profil erstellen/auswählen; `--server` allein genügt nicht.

### `cli_secret_store_unavailable`

Schlüsselbund fehlt, ist gesperrt oder nicht mit der aktuellen Shell verbunden. Secret Service für den aufrufenden Nicht-root-Benutzer reparieren.

### `cli_device_login_required`

Kein Credential für das gewählte Profil. Geräteanmeldung ausführen.

### `cli_device_credential_invalid`

Ungültige lokale Sitzung mit `mem logout` entfernen und neu anmelden.

### `agent_secret_retired` oder `installer_token_retired`

Abgelöste Option entfernen und nicht durch Umgebungsvariable ersetzen. Geräteanmeldung verwenden.

### Exit-Code `2`

Syntax und lokale Autorität waren gültig, aber das Betriebsergebnis erfüllte den Erfolg nicht. Status, Hinweise, Checks und Details prüfen.

### UI nicht verfügbar, CLI funktioniert

Zuerst nur lesbare Nachweise sammeln. Die CLI bietet derzeit keine globale Diagnose-, Seq-, Stack-Lifecycle-, Sicherungserstellungs- oder Migrationssitzungsbefehle. Erfinden Sie keine Ersatzbefehle und mutieren Sie Docker nicht außerhalb dokumentierter Grenzen.

## Verwandte Dokumentation

- [JSON-Ausgabe und Skripting](json-und-skripting.md)
- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
- [Host und Chatserver prüfen](host-und-stack-befehle.md)
