---
title: Kontostatus prüfen und abmelden
description: Prüfen Sie die benannte CLI-Sitzung sicher, verstehen Sie Ablaufzeiten, widerrufen Sie serverseitig und entfernen Sie das lokale Credential.
section: CLI und Automatisierung
order: 44
---

# Kontostatus prüfen und abmelden

## Aktuelle Sitzung prüfen

```bash
mem account show --profile home
mem account show --profile home --json | jq
```

Ein erfolgreicher Status darf nur sichere Sitzungsdaten zeigen:

- Profil und Server;
- Status `authenticated`;
- Anzeigename des Operators;
- zugewiesene Rollen;
- Inaktivitätsablauf;
- absoluter Ablauf.

Geräte-Credential, Browser-Cookie, Installationsidentität, Passwort, TOTP, Wiederherstellungscode, rohe Claims oder Secret-Store-Eintrag dürfen nicht erscheinen.

`account show` prüft sowohl das lokale Credential als auch dessen aktuelle Annahme durch den Server.

## Ergebnisse verstehen

| Status/Fehler | Bedeutung |
|---|---|
| `authenticated` | Server akzeptiert die gespeicherte Sitzung |
| `signed_out` | Kein Credential für Profil und Server |
| `unauthenticated` | Lokales Credential vorhanden, aber vom Server abgewiesen oder abgelaufen |
| `cli_account_secure_store_unavailable` | Lokaler Secret Store nicht sicher lesbar |
| `cli_account_unavailable` | Control Plane konnte die Sitzung nicht prüfen |

Abgemeldete oder abgewiesene Zustände liefern einen Nicht-null-Exit-Code, damit Skripte sie nicht als authentifiziert behandeln.

## Abmelden

```bash
mem logout --profile home
```

MEM versucht, die serverseitige Gerätesitzung zu widerrufen, und entfernt danach das lokale Secret-Service-Credential.

Mögliche Ergebnisse:

- `revoked` — serverseitiger Widerruf bestätigt;
- `unauthenticated` — Sitzung bereits abgewiesen oder abgelaufen;
- `unavailable` — Widerruf nicht bestätigbar, lokales Credential aber entfernt;
- keine gespeicherte Sitzung — idempotenter Erfolg ohne Serveranfrage.

Schlägt die lokale Löschung fehl, behauptet MEM nicht, das Credential entfernt zu haben.

## Abmeldung prüfen

```bash
mem account show --profile home --json | jq
mem host status --profile home --json | jq
```

Der Kontobefehl soll `signed_out` melden und Betriebsbefehle eine Geräteanmeldung verlangen. Es gibt keinen Fallback auf Installer-Token oder Agent-Secret.

## Veraltete Sitzung ersetzen

```bash
mem logout --profile home
mem login --device --profile home
mem account show --profile home
```

Eine Neuinstallation oder Änderung der aktuellen Installationsidentität macht alte CLI-Geräte-Credentials ungültig, auch bei gleicher URL.

## Verwandte Dokumentation

- [Mit Geräteanmeldung anmelden](geraeteanmeldung.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
