---
title: Fehlerbehebung bei der Installation
description: Diagnostizieren Sie Bootstrap-, Zugriffs-, Zertifikats-, Installations-, Verifikations- und First-Owner-Fehler.
section: Installation
order: 90
---

# Fehlerbehebung bei der Installation

Untersuchen Sie die fehlgeschlagene Stufe und löschen Sie keine dauerhaften Daten, bevor Nachweise gesichert sind.

## Bootstrap startet nicht

```bash
sudo ./install.sh --dry-run
sudo docker info
sudo docker compose version
sudo docker ps -a --filter name=mem-control-plane
sudo docker ps -a --filter name=mem-installer
```

Häufige Ursachen:

- nicht unterstützte Ubuntu-Version;
- zu wenig RAM oder Speicher;
- fehlender Repository-Zugriff;
- Docker-Daemon nicht erreichbar;
- Control-Plane-Port belegt;
- Image-Pull fehlgeschlagen.

Bei gestopptem Container zuerst:

```bash
sudo docker logs mem-control-plane
```

Bei einem **realen** Bootstrap-Lauf prüfen Sie außerdem die nur für `root` lesbaren Bootstrap-Nachweise unter:

```text
/var/log/mem/bootstrap/
```

`latest.log` verweist auf das letzte Transkript. Ein fehlgeschlagener Lauf hinterlässt zusätzlich `mem-bootstrap-<UTC-Lauf-ID>-failure.txt` mit fehlgeschlagener Phase, sicherer Operationsbezeichnung, Änderungsstatus, beibehaltenem Volume, begrenzten/redigierten Laufzeitnachweisen und Wiederherstellungshinweisen. Sammeln Sie diesen Bericht, bevor ein fehlgeschlagener Control-Plane-Container gelöscht wird.

Ein `--dry-run` erzeugt absichtlich kein dauerhaftes Transkript, weil der Dry Run nicht mutieren darf.

## Setup-Code fehlt

```bash
sudo ./install.sh --show-setup-token
```

Wenn Volume oder Token fehlen, erfinden Sie keinen Browser-Code. Führen Sie das offizielle Bootstrap erneut aus und untersuchen Sie die Persistenzursache.

## Browser erreicht Control Plane nicht

```bash
sudo docker ps --filter name=mem-control-plane
sudo ss -ltnp | grep 8443
sudo docker logs mem-control-plane
```

Prüfen Sie Port, Firewall, VPN oder SSH-Tunnel. Eine selbstsignierte Zertifikatswarnung ist erwartet; eine Verbindungsablehnung nicht.

## Legacy-Laufzeitname der Control Plane erkannt

Ein Container `mem-installer` mit dem geprüften Volume `mem-installer-data` ist eine frühere MEM-0.2.0-Control-Plane-Identität und keine MEM-0.1.0-Quelle. Prüfen Sie zuerst den Migrationsplan:

```bash
sudo ./install.sh --dry-run
```

Der reale Lauf verwendet Volume und Zertifikate weiter, prüft die kanonische Laufzeit und führt bei Fehlern ein Rollback aus. Benennen Sie das Volume nicht manuell um. Bei beiden Containernamen oder einem alleinstehenden `mem-installer-data` bricht das Bootstrap ohne Änderung ab.

## Legacy-Installation erkannt

Bei `mem-api` oder `mem-web` stoppen und MEM Migrate verwenden.

Benennen oder löschen Sie diese Container nicht, um die Erkennung zu umgehen.

## Unerwartete MEM-Ressourcen

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

Verwenden Sie Reparatur/Untersuchung und Diagnostics. Klären Sie, ob die Ressourcen zu fehlgeschlagener Installation, Test-Stack, Restore, Migration oder Legacy gehören.

## Zertifikatsvorgang scheitert

Prüfen Sie Domain, deSEC-Zone, Tokenrechte, ausgehendes HTTPS/DNS, Staging/Produktion, Propagation, TXT-Konflikte und NPM-Bereitschaft.

Fügen Sie den deSEC-Token niemals in Supportberichte ein.

Nach Browser-Timeout zuerst das gespeicherte Ergebnis laden, da Backend-Bereinigung noch laufen kann.

## Installation wartet auf Operator

Öffnen Sie die verlinkte Domain-/Ingress-Aktion, korrigieren Sie den Zustand und setzen Sie dieselbe Installation fort.

## Installation scheitert

Verwenden Sie die dauerhafte Fehlermeldung und **Diagnose ausführen**. Wenn die Control Plane verfügbar ist, verwenden Sie **Diagnostics öffnen**, um korrelierte Incident- und technische Ereignisnachweise zu prüfen, bevor Sie auf rohe Logs zurückgreifen.

Notieren Sie Installations-ID, Schritt, Incident-ID, Trace-/Korrelations-ID sowie erwarteten und beobachteten Zustand. Erzeugen Sie einen begrenzten Supportbericht.

Bei vollständigem API-Ausfall:

```bash
sudo docker logs mem-control-plane
```

sowie den konfigurierten dauerhaften Control-Plane-Logpfad verwenden.

## Verifikation warnt oder scheitert

Prüfen Sie Installationslauf, Schritte, Docker, `mem-postgres`, `mem-npm`, NPM-Initialisierung, Hauptzertifikat, Schlüsselübereinstimmung und NPM-Zertifikatssichtbarkeit.

Erstellen Sie keinen öffentlichen Stack, solange eine erforderliche Plattformprüfung fehlschlägt.

## First-Owner-Bootstrap scheitert

Prüfen Sie, ob bereits ein Platform Owner existiert, der Setup-Code exakt ist, der Grant gültig ist, Benutzername/Passwort akzeptiert wurden, die Uhrzeiten für TOTP stimmen und Recovery-Codes gespeichert wurden.

Passwörter, TOTP-Secrets, Recovery-Codes, Cookies, Setup-Codes und private Schlüssel gehören niemals in Supportmaterial.

## Installations-Supportbericht

Wenn Setup oder Installation nach dem Start der Control Plane scheitern, verwenden Sie bevorzugt den begrenzten MEM-Installations-Supportbericht statt rohe Logs zu kopieren.

Öffnen Sie nach Möglichkeit **Fehlerbehebung bei der Einrichtung** von der Seite Einrichtungsaktivität. Diese Ansicht behält die genaue Installations-ID bei, zeigt den zur Laufzeit passenden Host-Befehl, bietet den begrenzten Berichtsdownload an und behandelt rohe Docker-Logs nur als letzten Fallback.

Der Bericht enthält unter anderem den Fingerabdruck des geprüften Plans, die sichere Zusammenfassung der Serverprüfungen, die dauerhafte Installationszeitleiste, zugehörige Diagnoseereignisse, Wiederherstellungshinweise, Laufzeitidentität und begrenzte Docker-Nachweise, wenn diese eindeutig zugeordnet werden können.

Der Bericht lässt Zugangsdaten, Passwörter, DNS-Anbieter-Tokens, private Schlüssel, rohe Autorisierungsdaten, Recovery-Codes, Connection Strings, vollständige Containerumgebungen, unbegrenzte Befehlsausgaben und rohe CLEF-Dateien ausdrücklich aus.

Im Browser verwenden Sie **Supportbericht herunterladen** auf Einrichtungsaktivität, Fehlerbehebung bei der Einrichtung oder Abschluss.

Neueste Installation in Produktion:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report --latest \
  > "mem-install-report-$(date -u +%Y%m%dT%H%M%SZ).json"
```

Bekannte Installation:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report \
  --installation-id "<installation-id>" \
  --format json \
  > mem-install-report.json
```

Containerisierte Entwicklung:

```bash
docker exec mem-control-plane-dev \
  dotnet Api.dll support install-report --latest
```

Wenn ein Fehler vor der Vergabe einer Installations-ID auftrat, aber eine technische Trace-Referenz vorhanden ist:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report \
  --trace-id "<trace-id>" \
  --format text
```

Docker-Nachweise sind standardmäßig aktiviert. Verwenden Sie `--no-docker-evidence`, wenn nur dauerhafte Setup- und Diagnosedatensätze benötigt werden. Der Befehl schreibt den Bericht nach stdout, Betriebsfehler nach stderr, fragt keine Zugangsdaten interaktiv ab und erzeugt keine fehlende Control-Plane-Datenbank.

Als letzten Rohlog-Fallback:

```bash
sudo docker logs --timestamps --tail 500 mem-control-plane
```

oder in der containerisierten Entwicklung:

```bash
docker logs --timestamps --tail 500 mem-control-plane-dev
```

Rohe Logs können Betriebsmetadaten enthalten und sollten vor externer Weitergabe geprüft werden. Der generierte Supportbericht ist das bevorzugte Supportartefakt.

