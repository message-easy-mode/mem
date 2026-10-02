---
id: "de/cli/geraeteanmeldung"
translationKey: "cli/device-login"
locale: "de"
groupId: "cli-de"
groupKey: "cli"
groupLabel: "CLI und Automatisierung"
groupOrder: 80
title: "Mit Geräteanmeldung anmelden"
description: "Autorisieren Sie ein benanntes CLI-Gerät über die private Browseroberfläche und speichern Sie das undurchsichtige Sitzungs-Credential im Betriebssystem-Schlüsselspeicher."
order: 43
status: "supported"
appliesTo: ["0.2.x"]
tags: ["CLI", "Geräteanmeldung", "Browserfreigabe", "Secret Service", "MFA"]
route: "/docs/de/cli/geraeteanmeldung"
aliases: []
outputPath: "docs/de/cli/geraeteanmeldung.md"
preserveLegacyBranding: false
---
# Mit Geräteanmeldung anmelden

Die Geräteanmeldung ist der normale Autoritätspfad der MEM CLI. Sie verbindet ein lokales Profil mit einem benannten MEM-Operator, ohne Passwort, TOTP-Code oder Bearer-Credential in die Befehlszeile zu legen.

## Voraussetzungen

- Gültiges Profil erstellen oder auswählen.
- Erreichbarkeit der privaten Control Plane prüfen.
- Als normaler Nicht-root-Operator arbeiten.
- `secret-tool` und einen entsperrten Secret-Service-kompatiblen Schlüsselbund bereitstellen.
- Eine bestehende Browsersitzung eines benannten MEM-Operators mit TOTP besitzen.

## Anmeldung starten

```bash
mem login --device --profile home
```

`--json` wird bewusst nicht unterstützt, weil dies ein interaktiver mehrstufiger Browserablauf ist.

## Ablauf

1. Profil und Server werden geprüft.
2. MEM führt eine Schreiben-Lesen-Löschen-Probe im OS-Schlüsselspeicher aus.
3. Ein vorhandenes gültiges Credential wird wiederverwendet, ohne neue Serverautorisierung.
4. Ein hochentropischer Verifier bleibt nur im Speicher des CLI-Prozesses.
5. Der Server liefert private Browser-URL und Kurzcode.
6. Die CLI zeigt `/cli/authorize` und den Code an.
7. Die CLI wartet auf Prüfung und Freigabe im Browser.
8. Das Credential wird erst gespeichert, nachdem die ursprüngliche CLI ihren Verifier nachgewiesen hat.

Der Browser erhält das Geräte-Credential nie. Der sichtbare Kurzcode ersetzt den Verifier nicht.

## Im Browser freigeben

Öffnen Sie die angezeigte URL, geben Sie den Kurzcode ein, prüfen Sie Gerätebezeichnung und Ablauf und autorisieren Sie nur das von Ihnen gestartete Gerät. MEM verwendet den aktuell angemeldeten Browseroperator und fordert bei Bedarf eine neue Identitätsbestätigung an.

Der aktuelle Serverstandard gibt einer ausstehenden Autorisierung 10 Minuten. Der Server besitzt diese Grenze. Bei Ablauf starten Sie die Anmeldung neu.

## Erfolg prüfen

```bash
mem account show --profile home
mem host status --profile home
```

Aktuelle Serverstandards:

- maximal 8 Stunden Inaktivität;
- maximal 7 Tage absolut.

Die CLI zeigt die tatsächlich vom Server gelieferten Ablaufzeiten an.

## Häufige Fehler

| Ergebnis | Bedeutung | Maßnahme |
|---|---|---|
| `cli_login_profile_required` | Kein benanntes Profil | Profil erstellen oder auswählen |
| `cli_login_secure_store_unavailable` | Secret-Service-Probe oder Lesen fehlgeschlagen | Schlüsselbund des Nicht-root-Benutzers entsperren/konfigurieren |
| `cli_login_rate_limited` | Gemeinsames Startbudget überschritten | Warten und einmal erneut versuchen |
| `cli_login_authorization_denied` | Gerät im Browser abgelehnt | Gerät prüfen und neu starten |
| `cli_login_authorization_expired` | Freigabefenster abgelaufen | Neue Anmeldung starten |
| `cli_login_unreachable` | Control Plane nicht erreichbar | Private Verbindung reparieren |
| `cli_login_secure_store_write_failed` | Freigabe erfolgreich, lokales Speichern fehlgeschlagen | Schlüsselbund reparieren und neu anmelden |

## Sicherheitsgrenze

Autorisieren Sie kein fremdes Gerät. Veröffentlichen Sie den Kurzcode nicht. Extrahieren Sie das Credential nie aus Secret Service. MEM lehnt Passwort-, TOTP-, Recovery-Code-, Bearer-Token- und Geräte-Credential-Optionen ab.

## Verwandte Dokumentation

- [Profile und Server](profile.md)
- [Kontostatus und Abmeldung](konto-und-abmeldung.md)
- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
