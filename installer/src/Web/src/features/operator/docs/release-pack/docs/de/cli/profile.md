---
title: Profile und Server konfigurieren
description: Erstellen Sie nicht geheime Profile für private Control-Plane-Endpunkte und verwenden Sie eine vorhersehbare Server- und Sprachreihenfolge.
section: CLI und Automatisierung
order: 42
---

# Profile und Server konfigurieren

Ein Profil bezeichnet eine MEM Control Plane und optional die Sprache der lesbaren Ausgabe. Es authentifiziert den Operator nicht.

## Inhalt eines Profils

Ein Profil darf nur enthalten:

- normalisierten Profilnamen;
- Server-URL der Control Plane;
- optional `en` oder `de`;
- Auswahl des Standardprofils.

Es darf keine Passwörter, TOTP-Werte, Wiederherstellungscodes, Browser-Cookies, Installer-Tokens, Bearer-Credentials, Secret-Service-Werte, rohen Antworten oder Laufzeitnachweise enthalten.

Die normale Linux-Konfigurationsdatei ist typischerweise:

```text
~/.config/mem/config.json
```

`XDG_CONFIG_HOME` hat Vorrang. MEM schreibt Verzeichnis und Datei nur für den Benutzer, lehnt symbolische Links ab und aktualisiert atomar.

## Profil erstellen und auswählen

```bash
mem profile create home \
  --server https://mem.example.internal \
  --language de

mem profile select home
mem profile list
```

Profilnamen werden kleingeschrieben, müssen 1–32 Zeichen lang sein, mit Buchstabe oder Zahl beginnen und enden und dürfen nur Buchstaben, Zahlen und Bindestriche enthalten.

## Regeln für Server-URLs

Entfernte und produktive Endpunkte müssen HTTPS verwenden:

```bash
mem profile create home --server https://mem.example.internal
```

HTTP ist nur für explizite Loopback-Entwicklung zulässig:

```bash
mem profile create local --server http://127.0.0.1:7105
```

Die URL darf nur Schema, Host und optional Port enthalten. Benutzerinformation, Query, Fragment und Nicht-root-Pfade werden abgewiesen. Verwenden Sie die Control-Plane-Adresse, nicht die öffentliche Matrix- oder Element-URL.

## Standardprofil

Beide Befehle wählen das Standardprofil:

```bash
mem profile select home
mem config set default-profile home
```

Ein Profil nur für einen Befehl verwenden:

```bash
mem host status --profile lab
```

Beim Entfernen des Standardprofils wird auch die Standardauswahl gelöscht:

```bash
mem profile remove old-lab
```

Das Entfernen der Profilmetadaten widerruft oder löscht kein zugehöriges Secret-Service-Credential. Führen Sie bei vorhandener Gerätesitzung zuerst `mem logout --profile <name>` aus.

## Reihenfolge der Serverauswahl

```text
--server <url>
MEM_SERVER_URL
ausgewähltes oder Standardprofil
http://localhost:7105 als Entwicklungsstandard
```

Die versteckten Kompatibilitätseingaben `--host-agent-url` und `MEM_HOST_AGENT_URL` werden für alte Skripte noch geparst, gehören aber nicht in neue Dokumentation oder Automatisierung.

Betriebsbefehle benötigen weiterhin ein benanntes Profil, weil das sichere Credential an Profil und Server gebunden ist. `--server` allein hebt diese Pflicht nicht auf.

## Reihenfolge der Sprachauswahl

```text
--language <en|de>
MEM_CLI_LANGUAGE
Sprache des ausgewählten/Standardprofils
globale lokale Spracheinstellung
Systemsprache
Englisch
```

```bash
mem config set language de
mem config get language
mem host status --profile home --language en
MEM_CLI_LANGUAGE=de mem --help
```

JSON-Feldnamen und Maschinenwerte bleiben unabhängig von der Sprache Englisch.

## Erfolg prüfen

```bash
mem profile list --json | jq
mem config get default-profile --json | jq
mem config get language --json | jq
```

Prüfen Sie vor jedem destruktiven Backup- oder Restore-Befehl die Server-URL.

## Verwandte Dokumentation

- [Mit Geräteanmeldung anmelden](geraeteanmeldung.md)
- [Kontostatus und Abmeldung](konto-und-abmeldung.md)
- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
