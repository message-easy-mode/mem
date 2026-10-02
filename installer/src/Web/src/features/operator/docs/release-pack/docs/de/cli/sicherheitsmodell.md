---
title: CLI-Sicherheitsmodell verstehen
description: Verstehen Sie Geräteautorität, Installationsbindung, lokalen Secret Store, geschlossenes Step-up, abgelöste Credentials und reservierte Recovery.
section: CLI und Automatisierung
order: 50
---

# CLI-Sicherheitsmodell verstehen

## Normaler Autoritätspfad

```text
benannter MEM-Operator
→ bestehende Browseranmeldung und TOTP
→ geprüfte CLI-Gerätefreigabe
→ serverseitiges undurchsichtiges Geräte-Credential
→ Speicherung in OS Secret Service
→ serverseitige Rollen- und Capability-Policy
→ serverseitiges Audit und Step-up
```

Die Autorität ist auf Host, per SSH und auf privater Arbeitsstation identisch. Localhost ist kein Rollen- oder Autorisierungsbypass.

## Lokale Konfiguration und Credentials

Die Profildatei enthält nur nicht geheime Namen, Server-URLs, Sprache und Standardauswahl. Das Geräte-Credential liegt separat in `secret-tool` und ist an normalisiertes Profil plus Server-URL gebunden.

Die CLI prüft den sicheren Speicher vor einer Autorisierung. Es gibt keinen Fallback in:

- Klartextprofil oder Konfigurationsdatei;
- Umgebungsvariable;
- Befehlsargument;
- stdin;
- Shell-Historie;
- kopiertes Browser-Cookie.

## Serverseitige Sitzungsgrenzen

Der Server bindet die Autorisierung an die aktuelle abgeschlossene MEM-Installation. Neuinstallation oder Austausch macht das alte Credential ungültig, auch bei gleicher URL.

Aktuelle Serverstandards:

- ausstehende Freigabe: 10 Minuten;
- Inaktivitätsablauf: 8 Stunden;
- absoluter Ablauf: 7 Tage.

Der Server kontrolliert diese Werte. Login und `mem account show` zeigen die tatsächlich gelieferten Zeiten.

## Step-up-Grenze

Der Server entscheidet über aktuelle Identitätsbestätigung. Die CLI erzeugt oder umgeht keinen Grant.

Die aktuelle CLI besitzt keinen secret-sicheren interaktiven Passwort-und-TOTP-Wiederholungsablauf. Bei HTTP 403 schlägt sie geschlossen fehl. Verwenden Sie den genehmigten Browserablauf.

Abgewiesene Optionen:

```text
--password
--totp
--totp-code
--recovery-code
--device-credential
--bearer-token
```

## Abgelöste Autorität

Nicht in neue Skripte oder Anleitungen übernehmen:

```text
--installer-token
MEM_INSTALLER_TOKEN
--agent-secret
MEM_AGENT_SECRET
X-MEM-Agent-Secret
```

`--installer-token` und `--agent-secret` werden explizit abgewiesen. `MEM_INSTALLER_TOKEN` wird ignoriert. Das alte Shared-Secret-Modell darf nicht zurückkehren.

`--host-agent-url` und `MEM_HOST_AGENT_URL` bleiben nur versteckte Adress-Kompatibilität. Sie geben keine besondere Autorität und sollen durch `--server` und `MEM_SERVER_URL` ersetzt werden.

## Reservierte lokale Recovery

```bash
sudo mem auth arm-recovery
```

Der Befehl ist für eine zukünftige lokale Unix-Socket- oder Named-Pipe-Recovery-Brücke reserviert. Aktuell liefert er `cli_recovery_arm_not_available`, gibt keinen Recovery-Grant aus und stellt keine Control-Plane-Netzwerkanfrage. Remote-Server- und Profiloptionen werden abgewiesen.

Behandeln Sie diese reservierte Ablehnung nicht als verfügbaren Recovery-Ablauf.

## Sicheres Supportmaterial

In der Regel nach Prüfung teilbar:

- `mem --version`;
- Profilname und redigierte Server-URL;
- Kontostatus ohne Credential;
- begrenzte JSON-Status-, Check- und Evidence-Ausgabe sowie redigierte Supportberichte;
- genaue sichere Fehlercodes und Exit-Codes.

Niemals Credentials, Passwörter, TOTP- oder Wiederherstellungscodes, Browser-Cookies, Secret-Service-Ausgabe, Signatur- oder private Schlüssel, rohe Datenbankdumps oder Backup-Payloads teilen.

## Verwandte Dokumentation

- [Mit Geräteanmeldung anmelden](geraeteanmeldung.md)
- [JSON-Ausgabe und Skripting](json-und-skripting.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
