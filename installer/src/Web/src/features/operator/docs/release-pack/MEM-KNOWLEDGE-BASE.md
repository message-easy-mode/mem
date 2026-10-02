# MEM — Offline Documentation Knowledge Base

This file concatenates the portable Markdown documents bundled into MEM. Current pages are written for MEM 0.2.x; explicitly legacy pages are historical reference only. Do not combine this generic documentation with credentials, private keys, recovery codes, raw database dumps, backup payloads, or unrestricted host-specific logs.

---

# Back up and restore chat servers

Source: `docs/backups-and-restores/index.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Back up and restore chat servers

MEM treats backup and recovery as a product workflow, not as a collection of Docker commands.

## Outcome

After following this section, you can:

- capture a recovery-ready backup from a managed Matrix + Element stack;
- inspect the backup in the Backup Catalog;
- export an off-host portable ZIP or import one from another MEM server;
- open a durable Restore Workspace;
- run an optional private test before production changes;
- perform the supported Standard Recreate workflow;
- verify the recovered public service and complete handover;
- preserve evidence when a restore is cancelled or fails.

## Canonical recovery model

```text
Live Runtime Stack
  → local capture or imported portable ZIP
  → Backup Catalog entry
  → Restore Workspace
  → optional private test
  → Standard Recreate
  → public verification
  → operator handover
```

The **Backup Catalog is the only supported restore source**. An uploaded ZIP validation ID is an ingestion and provenance reference. It is not a restore-session ID and must not be used as a Restore Workspace route.

## Start here

1. [Create and inspect a backup](create-backup.md).
2. [Understand the Backup Catalog](backup-catalog.md).
3. [Export a portable backup](portable-export.md) and store it away from the MEM host.
4. When recovery is required, [start a Restore Workspace](restore-workspace.md).
5. Prefer the [private test](private-test.md) before production recreate when time permits.
6. Review [Standard Recreate](standard-recreate.md), [target claims](target-claims.md), and [verification and handover](verify-and-complete.md) before executing.

## Restore is not migration

Restore recreates a supported MEM stack from a native Backup Catalog payload while preserving the backed-up Matrix identity. Migration assesses and converts a different or legacy source environment. Read [Restore or migrate?](restore-or-migrate.md) before using a backup to move between unlike installations.

> [!IMPORTANT]
> A server backup does not replace user recovery keys or device key backup. MEM can restore server-side rooms, events, accounts, media, configuration, and identity material that was captured. It cannot recreate end-to-end encryption keys that users never preserved.

---

# Create and operate chat servers

Source: `docs/chat-servers/index.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Create and operate chat servers

A **chat server** in MEM is one managed Matrix homeserver with an Element web client. MEM calls this pair a **Runtime Stack** or simply a **stack**.

## Outcome

After following this section, you can:

- create a new Matrix + Element stack on an installed MEM platform;
- verify public and internal readiness;
- create the first Matrix administrator and later users;
- inspect services, routes, storage, TURN, federation, and operation history;
- run routine checks and create a backup before risky work;
- remove the active runtime without assuming that retained data was erased.

## What belongs to one stack

MEM creates and records a stack-scoped Matrix identity, Synapse configuration, Element configuration, Matrix and Element containers, a PostgreSQL database and role, filesystem storage, Nginx Proxy Manager routes, secrets, readiness evidence, and operation history.

Platform services such as PostgreSQL, Nginx Proxy Manager, and coturn are shared. Matrix users, rooms, messages, media, federation policy, and most runtime evidence belong to the individual stack.

> [!IMPORTANT]
> Matrix accounts are not MEM Control Plane accounts. A Platform Owner signs in to administer MEM. A Matrix user signs in to Element or another Matrix client.

## Recommended first-server sequence

1. [Create a chat server](create.md).
2. [Read the stack workspace and status](workspace.md).
3. [Synchronize and create Matrix users](users.md).
4. Open Element and sign in with the first Matrix administrator.
5. Review [Network and domains](network-and-domains.md), [Voice and video](voice-and-video.md), and [Federation](federation.md).
6. Establish a [daily operating routine](daily-operations.md).

## Guides in this section

- [Create a chat server](create.md)
- [Use the stack workspace](workspace.md)
- [Manage Matrix users](users.md)
- [Reset passwords and manage account lifecycle](passwords-and-accounts.md)
- [Inspect network and domains](network-and-domains.md)
- [Configure voice and video TURN](voice-and-video.md)
- [Manage federation](federation.md)
- [Understand storage and media](storage-and-media.md)
- [Run daily operations safely](daily-operations.md)
- [Remove a chat server](remove.md)
- [Troubleshoot a chat server](troubleshooting.md)

Backup, restore, migration, and full diagnostics have their own bounded workflows. Use the stack workspace links to enter those workflows rather than treating them as ordinary container operations.

---

# Chatserver sichern und wiederherstellen

Source: `docs/de/backups-und-wiederherstellen/index.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Chatserver sichern und wiederherstellen

MEM behandelt Sicherung und Wiederherstellung als Produktablauf und nicht als Sammlung einzelner Docker-Befehle.

## Ergebnis

Nach diesem Abschnitt können Sie:

- eine wiederherstellbare Sicherung eines verwalteten Matrix- und Element-Stacks erstellen;
- die Sicherung im Sicherungskatalog prüfen;
- ein portables ZIP für die externe Aufbewahrung exportieren oder importieren;
- einen dauerhaften Wiederherstellungsarbeitsbereich öffnen;
- vor Produktionsänderungen einen optionalen privaten Test ausführen;
- die unterstützte Standard-Neuerstellung durchführen;
- den öffentlichen Dienst prüfen und die Übergabe abschließen;
- Nachweise bei Abbruch oder Fehler erhalten.

## Kanonisches Wiederherstellungsmodell

```text
Aktiver Runtime Stack
  → lokale Sicherung oder importiertes portables ZIP
  → Eintrag im Sicherungskatalog
  → Wiederherstellungsarbeitsbereich
  → optionaler privater Test
  → Standard-Neuerstellung
  → öffentliche Prüfung
  → Übergabe an den Betreiber
```

Der **Sicherungskatalog ist die einzige unterstützte Wiederherstellungsquelle**. Eine Validierungs-ID eines hochgeladenen ZIPs ist eine Referenz für Aufnahme und Herkunft. Sie ist keine Wiederherstellungssitzungs-ID.

## Einstieg

1. [Sicherung erstellen und prüfen](sicherung-erstellen.md).
2. [Sicherungskatalog verstehen](sicherungskatalog.md).
3. [Portable Sicherung exportieren](portabler-export.md) und außerhalb des MEM-Hosts aufbewahren.
4. Bei Bedarf [Wiederherstellungsarbeitsbereich starten](wiederherstellungsarbeitsbereich.md).
5. Wenn möglich zuerst den [privaten Test](privater-test.md) ausführen.
6. Vor der Ausführung [Standard-Neuerstellung](standard-neuerstellung.md), [Zielreservierungen](zielreservierungen.md) sowie [Prüfung und Übergabe](pruefen-und-abschliessen.md) lesen.

## Wiederherstellung ist keine Migration

Eine Wiederherstellung erstellt einen unterstützten MEM-Stack aus einem nativen Katalog-Payload neu und bewahrt die gesicherte Matrix-Identität. Eine Migration bewertet und konvertiert eine andere oder ältere Quellumgebung. Lesen Sie [Wiederherstellen oder migrieren?](wiederherstellen-oder-migrieren.md), bevor Sie eine Sicherung zum Wechsel zwischen unterschiedlichen Installationen verwenden.

> [!IMPORTANT]
> Eine Serversicherung ersetzt keine Wiederherstellungsschlüssel oder Geräteschlüsselsicherung der Benutzer. MEM kann erfasste serverseitige Räume, Ereignisse, Konten, Medien, Konfiguration und Identitätsmaterial wiederherstellen. Nicht gesicherte Ende-zu-Ende-Verschlüsselungsschlüssel kann MEM nicht neu erzeugen.

---

# Chatserver erstellen und betreiben

Source: `docs/de/chat-servers/index.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Chatserver erstellen und betreiben

Ein **Chatserver** in MEM besteht aus einem verwalteten Matrix-Homeserver und einem Element-Webclient. MEM bezeichnet dieses Paar als **Runtime Stack** oder kurz **Stack**.

## Ergebnis

Nach diesem Abschnitt können Sie:

- einen neuen Matrix- und Element-Stack auf einer installierten MEM-Plattform erstellen;
- öffentliche und interne Bereitschaft prüfen;
- den ersten Matrix-Administrator und weitere Benutzer anlegen;
- Dienste, Routen, Speicher, TURN, Föderation und Vorgangsverlauf prüfen;
- Routineprüfungen durchführen und vor riskanten Arbeiten eine Sicherung erstellen;
- die aktive Laufzeit entfernen, ohne fälschlich von gelöschten Daten auszugehen.

## Was zu einem Stack gehört

MEM erstellt und erfasst eine Stack-bezogene Matrix-Identität, Synapse-Konfiguration, Element-Konfiguration, Matrix- und Element-Container, PostgreSQL-Datenbank und Rolle, Dateispeicher, Nginx-Proxy-Manager-Routen, Geheimnisse, Bereitschaftsnachweise und Vorgangsverlauf.

Plattformdienste wie PostgreSQL, Nginx Proxy Manager und coturn werden gemeinsam genutzt. Matrix-Benutzer, Räume, Nachrichten, Medien, Föderationsrichtlinie und die meisten Laufzeitnachweise gehören zum jeweiligen Stack.

> [!IMPORTANT]
> Matrix-Konten sind keine MEM-Control-Plane-Konten. Ein Platform Owner meldet sich zur Verwaltung von MEM an. Ein Matrix-Benutzer meldet sich bei Element oder einem anderen Matrix-Client an.

## Empfohlener Ablauf für den ersten Server

1. [Chatserver erstellen](erstellen.md).
2. [Stack-Arbeitsbereich und Status verstehen](arbeitsbereich.md).
3. [Matrix-Benutzer synchronisieren und erstellen](benutzer.md).
4. Element öffnen und mit dem ersten Matrix-Administrator anmelden.
5. [Netzwerk und Domains](netzwerk-und-domains.md), [Sprache und Video](sprache-und-video.md) sowie [Föderation](foederation.md) prüfen.
6. Einen [sicheren täglichen Betriebsablauf](taeglicher-betrieb.md) festlegen.

## Anleitungen in diesem Abschnitt

- [Chatserver erstellen](erstellen.md)
- [Stack-Arbeitsbereich verwenden](arbeitsbereich.md)
- [Matrix-Benutzer verwalten](benutzer.md)
- [Passwörter und Kontolebenszyklus verwalten](passwoerter-und-konten.md)
- [Netzwerk und Domains prüfen](netzwerk-und-domains.md)
- [TURN für Sprache und Video konfigurieren](sprache-und-video.md)
- [Föderation verwalten](foederation.md)
- [Speicher und Medien verstehen](speicher-und-medien.md)
- [Täglichen Betrieb sicher durchführen](taeglicher-betrieb.md)
- [Chatserver entfernen](entfernen.md)
- [Chatserver-Fehler beheben](fehlerbehebung.md)

Sicherung, Wiederherstellung, Migration und vollständige Diagnose besitzen eigene abgegrenzte Workflows. Verwenden Sie die Links im Stack-Arbeitsbereich, statt diese Aufgaben als gewöhnliche Container-Aktionen zu behandeln.

---

# MEM 0.2.0 installieren

Source: `docs/de/installation/index.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# MEM 0.2.0 installieren

Diese Anleitung beschreibt eine **neue MEM-0.2.0-Installation** auf einem unterstützten Ubuntu- und Docker-Host.

MEM 0.2.0 verwendet zwei Installationsstufen:

1. Das Host-Bootstrap bereitet Ubuntu, Docker, die private MEM Control Plane, ihren privaten Verwaltungszugang und den einmaligen Setup-Code vor.
2. Der First-Owner-Bootstrap erstellt den benannten Platform Owner mit MFA. Danach prüft die authentifizierte Control Plane den Host, installiert und verifiziert die verwaltete Plattform und übergibt in den normalen Operatorbetrieb.

> [!IMPORTANT]
> **MEM 0.2.0 unterstützt keine direkte Veröffentlichung der Control Plane auf einer öffentlich routbaren Netzwerkschnittstelle.**
>
> In ausdrücklich ausgewählten vertrauenswürdigen privaten Netzen ist direkter LAN-Zugriff unterstützt. Ansonsten ist SSH-Tunneling der unterstützte Weg für Remote-Verwaltung.

## Zustand der Control Plane und vorhandene MEM-0.2.0-Laufzeiten

Ein neues MEM-0.2.0-Bootstrap speichert den dauerhaften Zustand der Control Plane im Docker-Volume `mem-control-plane-data`. Benennen, kopieren oder ersetzen Sie dieses Volume nicht manuell; der Zustand muss Container-Neuerstellung und Upgrades überstehen.

Frühere Entwicklungs- und Vorschauinstallationen von MEM 0.2.0 verwendeten den Laufzeitnamen `mem-installer` und das Volume `mem-installer-data`. Das aktuelle Bootstrap erkennt genau diese Legacy-Laufzeit, zeigt eine geprüfte Migration zu `mem-control-plane`, verwendet das vorhandene Volume und die Zertifikatspfade weiter, wendet die aktuelle Richtlinie für private Administration an und verifiziert die Ersatzlaufzeit, bevor der alte Container-Eintrag entfernt wird. Eine aktualisierte Installation kann daher den Legacy-Volume-Namen `mem-installer-data` dauerhaft weiterverwenden.

Schlägt die Verifikation fehl, verwendet das Bootstrap die geprüfte Rollback-Richtlinie. Eine Wildcard- oder öffentlich routbare Control-Plane-Bindung darf nicht stillschweigend erneut geöffnet werden, nur um die Verfügbarkeit wiederherzustellen.

## Installationsablauf

1. Bei einem lokalen VM-/Server-Setup zuerst [Ubuntu Server für eine lokale MEM-Installation vorbereiten](ubuntu-server-on-prem.md), danach [Ubuntu-Host vorbereiten](host-vorbereiten.md)
2. [Bootstrap-Installer ausführen](bootstrap-installer.md)
3. [Private Control Plane öffnen und Setup-Code verwenden](privater-zugriff-und-setup-code.md)
4. [Ersten Platform Owner erstellen](erster-platform-owner.md)
5. [Serverprüfung ausführen](server-pruefen.md)
6. [Öffentliche Domain und Zertifikatsplan wählen](domain-und-zertifikat.md)
7. [Installationsplan prüfen und ausführen](pruefen-und-installieren.md)
8. [Plattform verifizieren und Übergabe abschließen](verifizieren-und-uebergeben.md)

## Wo Produktionsdaten liegen

Das unterstützte Serverlayout trennt drei Bereiche:

```text
/data                         privater Control-Plane-Zustand in mem-control-plane-data
/var/lib/message-easy-mode    hostsichtbare MEM-Laufzeit-/Stackdaten
/opt/mem                      installierte Software und Laufzeitpakete
```

Die Same-Path-Bindung von `/var/lib/message-easy-mode` gehört zum Produktionsvertrag zwischen Docker-Host und Control Plane. Neue Produktionsressourcen sollten nicht im Home-Verzeichnis eines Login-Benutzers angelegt werden.

## Was die Neuinstallation erstellt

Das Host-Bootstrap erstellt die private `mem-control-plane`-Laufzeit und deren dauerhaften Zustand. Die authentifizierte Ersteinrichtung bereitet unter anderem `mem-gateway`, `mem-postgres`, `mem-npm`, den gemeinsamen Coturn-Dienst, Volumes, Zertifikate sowie Installations- und Diagnosenachweise vor.

Getrennte `mem-api`- oder `mem-web`-Anwendungscontainer werden nicht erstellt. Der erste Matrix- und Element-Stack wird nach der Plattformübergabe erstellt.

## Sicherheitsgrenze

Die Control Plane besitzt Zugriff auf den Docker-Socket und ist hoch privilegiert. MEM behandelt ihre Netzwerkkonnektivität daher als Release-Sicherheitsgrenze.

```text
Internet/VPS
→ nur 127.0.0.1
→ SSH-Tunnel

Ausdrücklich vertrauenswürdiges LAN
→ eine ausgewählte RFC1918-Hostadresse
→ direktes privates HTTPS
```

Ein öffentlicher MEM-Admin-Hostname oder eine öffentliche NPM-Route für die Control Plane ist nicht erforderlich.

---

# Von MEM 0.1.0 migrieren

Source: `docs/de/migrieren/index.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Von MEM 0.1.0 migrieren

MEM Migrate überführt einen unterstützten älteren MEM-0.1.0-Chatserver nach MEM 0.2.0, ohne den alten Server als normale Sicherung zu behandeln.

Der Ablauf bewahrt die ausgewählte Matrix-Identität, verschlüsselt die Übertragung für die Ziel-Control-Plane, erstellt einen privaten Kandidaten, prüft die neue Laufzeit vor der öffentlichen Umschaltung und zeichnet durchgehend dauerhafte Nachweise auf.

## Ergebnis

Nach diesem Abschnitt können Sie:

- den MEM Migrate Source Assistant auf dem alten Host installieren und privat öffnen;
- die Quelle ohne Änderungen bewerten;
- genau einen Quell-Stack auswählen;
- die Ziel-Migrationsanfrage importieren und ein verschlüsseltes Paket erstellen;
- das Paket in MEM 0.2.0 hochladen, validieren, konvertieren und privat testen;
- den neuen normalen MEM-Server erstellen, ohne ihn zu veröffentlichen;
- die geführte Routenumstellung und Produktionsprüfung durchführen;
- den migrierten Server annehmen, die Quelle aufbewahren und die erste native MEM-Sicherung erstellen;
- Abbruch-, Rollback-, Bereinigungs- und Nachweisgrenzen verstehen.

## Die beiden Arbeitsbereiche

```text
Alter MEM-0.1.0-Host                         MEM-0.2.0-Ziel
────────────────────                         ──────────────
MEM Migrate Source Assistant                 Migrationsarbeitsbereich der Control Plane
  1. Import request (Anfrage importieren)       1. Paket erstellen und hochladen
  2. Confirm readiness (Bereitschaft)           2. Alten Server überprüfen
  3. Create capture (Erfassung)                 3. Vorbereiten und testen
  4. Create package (Paket)                     4. Neuen Server erstellen
  5. Download package (Download)                5. Neuen Server live schalten
                                                6. Migration abschließen
```

Die Oberfläche des Source Assistant verwendet derzeit englische Aktionsbezeichnungen. Der Source Assistant liest und verpackt den ausgewählten alten Stack. Die Ziel-Control-Plane besitzt Entschlüsselung, Konvertierung, privates Staging, Routenumstellung, Annahme und erste native Sicherung.

## Zentrale Sicherheitsregeln

- Migrieren Sie **jeweils genau einen Stack**. Die Quellauswahl wird in das Paket geschrieben und ist maßgeblich.
- Bewahren Sie die alte Quelle bis nach Produktionsprüfung, Annahme und dem gewählten Aufbewahrungszeitraum auf.
- Stellen Sie den Source Assistant nicht ins öffentliche Internet. Verwenden Sie Loopback oder einen SSH-Tunnel.
- Schützen Sie vor der Erfassung die Verschlüsselungswiederherstellung der Benutzer. Ein Passwort-Reset kann fehlende historische Nachrichtenschlüssel nicht neu erzeugen.
- Privates Staging besitzt keine öffentlichen Matrix- oder Element-Routen.
- Routenveröffentlichung ist keine DNS-Verwaltung. DNS-Einträge und ein verwendbares Zertifikat müssen bereits vorhanden sein.
- Eine fehlgeschlagene Live-Prüfung schaltet den alten Server nicht automatisch wieder öffentlich.
- Migrationsmaterial gelangt erst nach Annahme und erster nativer Sicherung in den Sicherungskatalog.

> [!IMPORTANT]
> Installieren Sie MEM 0.2.0 nicht als Abkürzung über den alten Host. Verwenden Sie ein separates MEM-0.2.0-Ziel und den geführten Migrationspfad.

## Einstieg

1. [Migration entscheiden und sicher vorbereiten](decide-and-prepare.md).
2. [Source Assistant installieren und privat öffnen](install-source-assistant.md).
3. [Quelle bewerten und einen Stack auswählen](assess-and-select.md).
4. [Verschlüsseltes Migrationspaket erstellen](create-package.md).
5. In der Ziel-Control-Plane mit [Paket hochladen und alten Server prüfen](upload-and-review.md) fortfahren.
6. Vor der Produktionsumschaltung [Rollback-Grenzen](rollback.md) lesen.

---

# Willkommen bei MEM

Source: `docs/de/start/welcome.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# Willkommen bei MEM

![Message Easy Mode Logo](../../../assets/brand/mem-logo-docs.png)

MEM steht für **Message Easy Mode**. MEM 0.2.0 ist eine Open-Source-Control-Plane für den Betrieb selbst gehosteter Matrix- und Element-Dienste auf einem herkömmlichen Linux- und Docker-Host.

MEM richtet sich an technisch versierte Betreiber, die ihre Kommunikationsplattform selbst kontrollieren möchten, ohne jede Datenbank, Proxy-Route, jedes Zertifikat sowie alle Backup-, Restore- und Migrationsschritte von Hand zusammensetzen zu müssen.

> [!IMPORTANT]
> MEM ist die Verwaltungsebene. Matrix bleibt das Kommunikationsprotokoll, Synapse der Homeserver und Element der primäre Web-Client.

## Wobei MEM hilft

Die MEM Control Plane bündelt die wichtigsten Betriebsabläufe in einer privaten Oberfläche:

- den Host vorbereiten und prüfen;
- Domains und Zertifikate konfigurieren;
- Matrix-Chatserver erstellen und untersuchen;
- Benutzer, Föderation und TURN-Verbindungen verwalten;
- Backups erstellen und katalogisieren;
- Wiederherstellungen privat testen und durchführen;
- eine unterstützte ältere MEM-0.1.0-Installation migrieren;
- Diagnosen, Vorfälle, Laufzeitnachweise und Supportberichte prüfen.

Der normale öffentliche Datenverkehr geht an Element, Synapse und TURN. Die MEM-Verwaltungsoberfläche sollte privat bleiben.

## Nächster Schritt

- [Was MEM ist](what-is-mem.md)
- [Passt MEM zu Ihnen?](is-mem-right-for-you.md)
- [Installieren oder migrieren?](install-or-migrate.md)
- [Anforderungen und unterstützte Umgebung](requirements.md)
- [MEM-Produktpakete](packages.md)
- [Bekannte Einschränkungen](known-limitations.md)
- [MEM-Glossar](glossary.md)

## Status dieser Dokumentation

Dieser Bereich **Erste Schritte** wurde für MEM 0.2.x geschrieben und orientiert sich an den aktuellen Verträgen für Control Plane, Einrichtung, Laufzeit, CLI, Migration, Wiederherstellung und Diagnose.

Der aktuelle nicht-legacy Dokumentationsbaum ist für MEM 0.2.x geprüft. Historisches Pre-0.2-Material wird ausdrücklich als Legacy erhalten und nicht mit aktueller Betriebsanleitung vermischt.

---

# Install MEM 0.2.0

Source: `docs/installation/index.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Install MEM 0.2.0

This guide covers a **fresh MEM 0.2.0 installation** on a supported Ubuntu and Docker host.

MEM 0.2.0 uses a two-stage installation model:

1. a small host bootstrap prepares Ubuntu, Docker, the private MEM Control Plane container, its private administration path, and the one-time setup code;
2. first-owner bootstrap creates the named Platform Owner and MFA authority, then the authenticated MEM Control Plane checks the host, validates the public-domain plan, installs the managed platform, verifies the result, and hands the operator into normal Control Plane use.

> [!IMPORTANT]
> **MEM 0.2.0 does not support directly exposing the Control Plane on a publicly routable network interface.**
>
> On explicitly selected trusted private networks, direct LAN administration is supported. Everywhere else, SSH tunnelling is the supported remote-management method.

## Choose the correct path first

Use this guide for a new first-time Setup when the private MEM Control Plane has been bootstrapped but the managed MEM platform has not yet completed installation.

Stop and use MEM Migrate when the server contains the legacy `mem-api` or `mem-web` containers. The Control Plane detects those names and directs the operator away from a destructive fresh install.

If MEM-managed containers, networks, or volumes exist without a current installation record, treat the host as a repair or investigation case. Do not delete resources merely to make the fresh-install path appear.

See [Install or migrate?](../start/install-or-migrate.md) before continuing when the server has ever hosted MEM.

## Control Plane state and existing MEM 0.2.0 runtime names

A fresh MEM 0.2.0 bootstrap stores Control Plane state in the Docker volume `mem-control-plane-data`. Do not rename, copy, or replace that volume manually; it contains the persistent Control Plane state that must survive container recreation and upgrades.

Earlier MEM 0.2.0 development and preview installations used the runtime name `mem-installer` and volume `mem-installer-data`. The current bootstrap recognises that exact legacy runtime, presents a reviewed migration to `mem-control-plane`, reuses the existing volume and certificate paths, applies the current private-administration binding policy, and verifies the replacement before retiring the old container record. An upgraded installation may therefore continue using the legacy volume name `mem-installer-data` permanently.

If verification fails, bootstrap follows its reviewed rollback policy. It must not silently reopen a wildcard or publicly routable Control Plane binding merely to restore availability.

## Installation sequence

1. [Prepare Ubuntu Server for an on-premises MEM install](ubuntu-server-on-prem.md) when using a local VM/server, then [Prepare the Ubuntu host](prepare-host.md)
2. [Run the bootstrap installer](bootstrap-installer.md)
3. [Open the private Control Plane and use the setup code](private-access-and-setup-code.md)
4. [Create the first Platform Owner](first-platform-owner.md)
5. [Run the server checks](check-server.md)
6. [Choose the public domain and certificate plan](domain-and-certificate.md)
7. [Review and run the install plan](review-and-install.md)
8. [Verify the platform and complete handoff](verify-and-handoff.md)

Use [Installation troubleshooting](troubleshooting.md) when a stage does not complete.

## What a fresh install creates

The host bootstrap creates the private `mem-control-plane` runtime and its persistent state. Authenticated first-time Setup then prepares or verifies:

- the `mem-gateway` Docker network;
- PostgreSQL as `mem-postgres`;
- Nginx Proxy Manager as `mem-npm`;
- shared Coturn as the platform TURN service;
- persistent platform volumes;
- the selected wildcard certificate and its NPM import;
- selected support tools where enabled;
- durable installation, verification, and diagnostic evidence.

It does **not** create separate MEM API and Web application containers. The ASP.NET Core API, HostAgent runtime services, and built React application run together inside the private Control Plane process.

The first Matrix + Element stack is created after platform handoff.

## Security boundary

The Control Plane mounts the Docker socket and is highly privileged. MEM therefore treats its network exposure as a release security boundary.

Supported administration is:

```text
Internet/VPS
→ 127.0.0.1 only
→ SSH tunnel

Explicit trusted LAN
→ one selected RFC1918 host address
→ direct private HTTPS
```

MEM does not require a public `admin` hostname and must not create a public NPM route for the Control Plane.

---

# Migrate from MEM 0.1.0

Source: `docs/migrate/index.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Migrate from MEM 0.1.0

MEM Migrate moves one supported legacy MEM 0.1.0 chat server into MEM 0.2.0 without treating the old server as a normal backup.

The workflow preserves the selected Matrix identity, encrypts the transfer for the target Control Plane, creates a private candidate, proves the new runtime before public cutover, and records durable evidence throughout.

## Outcome

After following this section, you can:

- install and open the MEM Migrate Source Assistant on the legacy host;
- assess the source without changing it;
- select exactly one source stack;
- import the target Migration Request and create an encrypted package;
- upload, validate, convert, and privately test the package in MEM 0.2.0;
- create the new normal MEM server without publishing it;
- perform a guided route cutover and production verification;
- accept the migrated server, retain the legacy source, and create the first native MEM backup;
- understand cancellation, rollback, cleanup, and evidence boundaries.

## The two workspaces

```text
Legacy MEM 0.1.0 host                         MEM 0.2.0 target
─────────────────────                         ────────────────
MEM Migrate Source Assistant                  Control Plane Migration Workspace
  1. Import request                             1. Create and upload package
  2. Confirm readiness                          2. Review the old server
  3. Create capture                             3. Prepare and test
  4. Create package                             4. Create the new server
  5. Download package                           5. Make the new server live
                                                 6. Finish the migration
```

The Source Assistant reads and packages the selected legacy stack. The target Control Plane owns decryption, conversion, private staging, route cutover, acceptance, and the first native backup.

## Core safety rules

- Migrate **one stack at a time**. The source selection is written into the package and is authoritative.
- Keep the legacy source available until production verification, acceptance, and the chosen retention period are complete.
- Do not expose the Source Assistant to the public internet. Use loopback access or an SSH tunnel.
- Protect user encryption recovery before capture. A password reset cannot recreate missing historical message keys.
- Private staging has no public Matrix or Element routes.
- Route publication is not DNS management. Required DNS records and a usable certificate must already exist.
- A failed live verification does not automatically make the old server public again.
- Migration material does not enter the Backup Catalog until the server is accepted and MEM creates its first native backup.

> [!IMPORTANT]
> Do not install MEM 0.2.0 over the legacy host as a shortcut. Use a separate MEM 0.2.0 target and the guided migration path.

## Start here

1. [Decide whether migration is the correct path and prepare safely](decide-and-prepare.md).
2. [Install and open the Source Assistant privately](install-source-assistant.md).
3. [Assess the source and select one stack](assess-and-select.md).
4. [Create the encrypted migration package](create-package.md).
5. Continue in the target Control Plane with [Upload and review the old server](upload-and-review.md).
6. Read [Rollback boundaries](rollback.md) before the production cutover.

---

# Welcome to MEM

Source: `docs/start/welcome.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# Welcome to MEM

![Message Easy Mode logo](../../assets/brand/mem-logo-docs.png)

MEM means **Message Easy Mode**. MEM 0.2.0 is an open-source control plane for operating self-hosted Matrix and Element services on a conventional Linux and Docker host.

MEM is intended for technically capable operators who want ownership of their communications service without assembling every database, proxy route, certificate, backup procedure, and migration step by hand.

> [!IMPORTANT]
> MEM is the management layer. Matrix remains the communications protocol, Synapse remains the homeserver, and Element remains the primary web client.

## What MEM helps you do

The MEM Control Plane brings the main operator workflows into one private interface:

- prepare and verify the host;
- configure domains and certificates;
- create and inspect Matrix chat servers;
- manage users, federation, and TURN connectivity;
- create and catalogue backups;
- test and perform restores;
- migrate a supported legacy MEM 0.1.0 installation;
- inspect diagnostics, incidents, runtime evidence, and support reports.

The normal public traffic path goes to Element, Synapse, and TURN. The MEM administration surface should remain private.

## Choose your next page

- [What MEM is](what-is-mem.md)
- [Is MEM right for you?](is-mem-right-for-you.md)
- [Install or migrate?](install-or-migrate.md)
- [Requirements and supported environment](requirements.md)
- [MEM product packages](packages.md)
- [Known limitations](known-limitations.md)
- [MEM glossary](glossary.md)

## Documentation status

This **Start here** section is written for MEM 0.2.x and is grounded in the current control-plane, setup, runtime, CLI, migration, recovery, and diagnostics contracts.

The current non-legacy documentation tree is reviewed for MEM 0.2.x. Historical pre-0.2 material is preserved explicitly as legacy rather than mixed into current operating guidance.

---

# Ubuntu Server für eine lokale MEM-Installation vorbereiten

Source: `docs/de/installation/ubuntu-server-on-prem.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Ubuntu Server für eine lokale MEM-Installation vorbereiten

Diese Seite fasst die praktischen Vorbereitungen zusammen, die vor einer echten MEM-Installation auf Ubuntu Server wichtig sind. Sie ergänzt [Ubuntu-Host vorbereiten](host-vorbereiten.md) um Speicher- und LAN-Details, die bei einer frischen VM leicht übersehen werden.

## Empfohlener Ausgangspunkt

Für den normalen MEM-0.2.0-Releasepfad verwenden Sie:

- Ubuntu Server **24.04 LTS** auf `amd64` / `x86_64`;
- mindestens **3.500 MB RAM** für den Bootstrap, etwa **7.800 MB** empfohlen;
- mindestens **20.000 MB freien Speicher auf `/`**, etwa **50.000 MB** empfohlen;
- eine stabile private Serveradresse bei Trusted-LAN-Verwaltung;
- funktionierendes öffentliches DNS und bei On-Premises-Installationen einen bewussten internen DNS-Plan.

## Ubuntu Guided LVM: sicherstellen, dass `/` die Platte wirklich nutzt

Die geführte LVM-Installation von Ubuntu Server kann fast die gesamte virtuelle Platte der Volume Group zuweisen, aber nur einen Teil davon dem Root Logical Volume. Eine 50-GB-Test-VM kann dann ungefähr so aussehen:

```text
virtuelle Platte             50 GB
ubuntu-vg                    ~48 GB
ubuntu-lv unter /            ~24 GB
frei innerhalb ubuntu-vg     ~24 GB
```

MEM prüft den freien Speicher, der auf `/` sichtbar ist. Nicht zugewiesener Platz innerhalb der Volume Group zählt dafür nicht.

Prüfen Sie auf der Ubuntu-Seite **Storage configuration** vor **Done** das Logical Volume, das unter `/` eingebunden wird. Für einen einfachen dedizierten MEM-Server sollte `ubuntu-lv` im Wesentlichen die gesamte verfügbare Kapazität von `ubuntu-vg` verwenden.

Nach der Installation prüfen Sie:

```bash
df -hT /
sudo vgs
sudo lvs
```

Wenn das Root Logical Volume auf einem dedizierten Server bereits zu klein angelegt wurde und die Volume Group noch freie Extents besitzt, lautet die übliche LVM-Erweiterung:

```bash
sudo lvextend -l +100%FREE -r /dev/ubuntu-vg/ubuntu-lv
```

Prüfen Sie die Gerätenamen vorher. Verwenden Sie den Befehl nicht ungeprüft auf einem Host mit anderer LVM-Struktur.

## Produktions-Dateisystemmodell

Eine unterstützte Produktionsinstallation trennt privaten Control-Plane-Zustand, hostsichtbare Laufzeitdaten und installierte Software:

```text
/data
    privater Control-Plane-Zustand im persistenten
    Docker-Volume mem-control-plane-data

/var/lib/message-easy-mode
    servereigene, hostsichtbare MEM-Daten
    ├── mem-data
    ├── instances
    ├── platform/coturn
    └── seq

/opt/mem
    installierte MEM-Software und Laufzeitpakete
```

Die offizielle Produktionslaufzeit bindet `/var/lib/message-easy-mode` unter demselben absoluten Pfad in die Control Plane ein. So adressieren Control Plane und Host-Docker-Daemon dieselben physischen Stack-Dateien.

Eine neue Produktionsinstallation darf für neue Laufzeitdaten nicht vom Home-Verzeichnis des Login-Benutzers abhängen. Leiten Sie Produktions-Stackdaten nicht auf `/home/<user>/mem-data` um.

## Privater Control-Plane-Zugriff

Für einen lokalen Server verwenden Sie einen unterstützten privaten Verwaltungsmodus:

- **Trusted LAN** — Bindung an genau eine ausgewählte RFC1918-Adresse;
- **SSH / local-only** — Bindung an Loopback und Zugriff per SSH-Portweiterleitung.

Veröffentlichen Sie den Control-Plane-Port nicht über öffentliches DNS, Internet-NAT oder Nginx Proxy Manager.

## Öffentliche Dienstports

Der verwaltete öffentliche Dienst benötigt normalerweise:

```text
443/tcp                HTTPS Matrix und Element
3478/tcp               TURN
3478/udp               TURN
49160-49200/udp        TURN-Relay-Bereich
```

TCP 80 ist optional, wenn bewusst ein HTTP-/Weiterleitungspfad verwendet wird. Der aktuelle geführte deSEC-DNS-01-Zertifikatspfad benötigt öffentliches Port 80 nicht für die ACME-Ausstellung.

Die Nginx-Proxy-Manager-Verwaltung auf TCP 81 und die MEM Control Plane auf TCP 8443 sind Verwaltungsoberflächen und sollten privat bzw. eingeschränkt bleiben.

## DNS und Split DNS

Öffentliches DNS sollte Matrix-, Element- und TURN-Namen auf die Adresse zeigen lassen, über die Internet-Clients den Server erreichen.

Im internen LAN ist Split DNS oft sinnvoll, damit derselbe Dienstname direkt auf die private Serveradresse zeigt, zum Beispiel:

```text
öffentliches DNS
turn.example.org  -> öffentliche WAN-Adresse

interner Resolver
turn.example.org  -> 10.10.0.198
```

Dadurch hängen lokale Dienstprüfungen nicht unnötig von NAT-Reflection/Hairpin-Verhalten ab.

Eine DNS-Überschreibung ändert nur die Namensauflösung. Sie öffnet **keine** Firewall-Ports und richtet **keine** NAT-/Portweiterleitung ein.

## Vor dem MEM-Bootstrap

Prüfen Sie die Basisdaten direkt auf dem neuen Server:

```bash
. /etc/os-release
echo "$PRETTY_NAME"
dpkg --print-architecture
df -hT /
ip -4 -br addr
getent ahostsv4 turn.example.org
```

Wenn Docker auf einem wirklich frischen Host noch nicht installiert ist, ist das erwartbar; das MEM-Bootstrap kann die unterstützten Docker-Pakete installieren.

---

# Prepare Ubuntu Server for an on-premises MEM install

Source: `docs/installation/ubuntu-server-on-prem.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Prepare Ubuntu Server for an on-premises MEM install

This page records the practical host preparation that matters on a real Ubuntu Server before MEM is installed. It complements [Prepare the Ubuntu host](prepare-host.md) with the storage and LAN details that are easy to miss during a fresh VM installation.

## Recommended starting point

For the normal MEM 0.2.0 release path use:

- Ubuntu Server **24.04 LTS** on `amd64` / `x86_64`;
- at least **3,500 MB RAM** to pass bootstrap, with about **7,800 MB** recommended;
- at least **20,000 MB free on `/`** to pass bootstrap, with about **50,000 MB free** recommended;
- a stable private server address when using Trusted LAN administration;
- working public DNS and, for an on-premises deployment, a deliberate internal DNS plan.

## Ubuntu guided LVM: make sure `/` actually uses the disk

Ubuntu Server's guided LVM layout can place most of a virtual disk in the volume group while assigning only part of it to the root logical volume. A 50 GB test VM can therefore look roughly like this:

```text
virtual disk                 50 GB
ubuntu-vg                    ~48 GB
ubuntu-lv mounted at /       ~24 GB
free space inside ubuntu-vg  ~24 GB
```

MEM checks the free space visible on `/`. Space left unused inside the volume group does not count toward that check.

On the Ubuntu **Storage configuration** screen, inspect the logical volume mounted at `/` before selecting **Done**. For a simple dedicated MEM server, edit `ubuntu-lv` so it uses essentially the full available `ubuntu-vg` capacity.

After installation, verify:

```bash
df -hT /
sudo vgs
sudo lvs
```

For a dedicated server where the root logical volume was already created too small and the volume group still has free extents, the standard LVM expansion is:

```bash
sudo lvextend -l +100%FREE -r /dev/ubuntu-vg/ubuntu-lv
```

Review the device names before running that command. Do not copy it blindly to a host with a different LVM layout.

## Production filesystem model

A supported production installation deliberately separates private Control Plane state, host-visible runtime data, and installed software:

```text
/data
    private Control Plane state inside the persistent
    mem-control-plane-data Docker volume

/var/lib/message-easy-mode
    server-owned host-visible MEM data
    ├── mem-data
    ├── instances
    ├── platform/coturn
    └── seq

/opt/mem
    installed MEM software and runtime payloads
```

The official production runtime identity-mounts `/var/lib/message-easy-mode` into the Control Plane at the same absolute path so the Control Plane and the host Docker daemon address the same physical stack files.

A fresh production install must not depend on the login account's home directory for new runtime data. Do not redirect production stack storage to `/home/<user>/mem-data`.

## Private Control Plane access

For an on-premises server, choose one of the supported private administration modes:

- **Trusted LAN** — bind the Control Plane to one explicitly selected RFC1918 address;
- **SSH / local-only** — bind it to loopback and use SSH port forwarding.

Do not publish the Control Plane port through public DNS, Internet NAT, or Nginx Proxy Manager.

## Public service ports

The managed public service normally needs:

```text
443/tcp                HTTPS Matrix and Element
3478/tcp               TURN
3478/udp               TURN
49160-49200/udp        TURN relay range
```

TCP 80 is optional when you deliberately want an HTTP/redirect path. The current guided deSEC DNS-01 certificate flow does not require public port 80 for ACME issuance.

Nginx Proxy Manager administration on TCP 81 and the MEM Control Plane on TCP 8443 are administration surfaces and should remain private/restricted.

## DNS and split DNS

Public DNS should point Matrix, Element, and TURN names at the address through which Internet clients can reach the server.

On an internal LAN it is often useful to use split DNS so the same service hostname resolves directly to the server's private address, for example:

```text
public DNS
turn.example.org  -> public WAN address

internal resolver
turn.example.org  -> 10.10.0.198
```

This avoids making local service checks depend on NAT reflection/hairpin behaviour.

A DNS override only changes name resolution. It does **not** open firewall ports or configure NAT/port forwarding.

## Before running MEM

Confirm the basics from the new server itself:

```bash
. /etc/os-release
echo "$PRETTY_NAME"
dpkg --print-architecture
df -hT /
ip -4 -br addr
getent ahostsv4 turn.example.org
```

When Docker is not already installed, that is expected on a genuinely fresh host; the MEM bootstrap can install the supported Docker packages.

---

# Operations guide

Source: `docs/operations/operations-guide.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Operations guide

Operate MEM through the Control Plane's server-owned state and durable operations rather than by treating Docker containers as the product contract.

## Normal daily sequence

1. Open the private Control Plane.
2. Check Home for public access and platform-service health.
3. Review Diagnostics attention when it is non-zero.
4. Open affected stack/service workspaces and refresh current evidence.
5. Use **Doctor** before making assumptions from container presence alone.
6. Create a backup before high-risk stack work.
7. Use reviewed MEM operations for restart, TURN, federation, backup/restore, migration, and removal.

## Platform versus stack health

A healthy platform means shared services such as PostgreSQL, NPM and Coturn are ready. Each Matrix + Element stack has its own readiness and public-route state.

A successful first-time Setup does not by itself prove the first managed stack can be provisioned. On a new server, create and verify a first stack as part of acceptance.

## Incidents and evidence

When an operation fails, preserve the operation ID and incident/support report before retrying or cleaning resources. A durable failed operation can contain the evidence needed to distinguish a product defect from DNS, firewall, image, storage, or network state.

## Docker and Portainer

Use Docker/Portainer for bounded low-level inspection, not as the normal mutation surface. Do not manually recreate, relabel, reconnect, or delete MEM-owned resources unless a documented recovery procedure explicitly calls for it.

## Backups

Keep portable backups off-host. Test recovery rather than assuming an on-box backup is sufficient.

## Restart/reboot acceptance

After Control Plane recreation or server reboot, verify runtime identity, managed-network/service reachability, platform health, and at least one representative stack before declaring recovery complete.

---

# Tools

Source: `docs/tools/index.md`
Locale: en
Section: Tools
Status: supported
Applies to: 0.2.x

# Tools

MEM's normal operating model is the private Control Plane, durable operations, Diagnostics, and the MEM CLI. Optional tools can provide lower-level evidence but should not become an alternate product control plane.

## Portainer

[Use Portainer for advanced container diagnostics](optional-portainer.md) when MEM provides a supported private handoff or when an experienced operator needs bounded Docker inspection.

## Seq

Seq is an optional structured-log workspace managed through MEM's Diagnostics/Seq workflow where supported.

## pgAdmin

[Optional pgAdmin](optional-pgadmin.md) is external advanced database tooling. MEM 0.2.x does not require or manage pgAdmin as part of the normal platform install.

## Safety boundary

Use these tools to understand a problem before mutation. Prefer MEM's supported workflow for lifecycle changes, backup/restore, service repair, routes, TURN, and stack operations.

---

# Architecture

Source: `docs/architecture/index.md`
Locale: en
Section: Architecture
Status: supported
Applies to: 0.2.x

# Architecture

MEM 0.2.x is a private **Control Plane** that manages shared platform services and one or more Matrix + Element runtime stacks on an operator-owned Docker host.

## Main runtime layers

```text
private administration
    mem-control-plane

shared platform
    mem-postgres
    mem-npm
    mem-coturn
    optional support tools such as Portainer / Seq

managed chat stacks
    mem-matrix-<slug>
    mem-element-<slug>
    stack PostgreSQL database/role
    NPM routes
    stack files and operation evidence
```

The Control Plane owns Docker orchestration authority through the host Docker socket. That is why its network exposure is deliberately private.

## Private and public traffic are different boundaries

The Control Plane should be reachable only through an explicitly selected Trusted LAN address, VPN, or SSH/local-only path.

Public traffic goes instead to managed services:

```text
Internet / client
    -> 80/443 -> Nginx Proxy Manager -> Matrix / Element
    -> TURN ports -> shared coturn
```

MEM does not require a public admin hostname for the Control Plane.

## `mem-gateway`

Managed platform and stack services communicate on the Docker network `mem-gateway`. NPM carries the `npm` alias there. A containerized Control Plane that must administer NPM also needs membership in that network.

Browser links must never expose internal Docker-only names merely because the server can resolve them.

## Production data boundaries

The production filesystem has three distinct roles:

```text
/data
    private persistent Control Plane volume state

/var/lib/message-easy-mode
    host-visible MEM operational data
    ├── mem-data
    ├── instances
    ├── platform/coturn
    └── seq

/opt/mem
    installed MEM software and runtime payloads
```

`/var/lib/message-easy-mode` is bind-mounted into the Control Plane at the same absolute path. This identity-mount is intentional: when MEM passes a stack path to the host Docker daemon, both namespaces must refer to the same physical file.

## One stack

A managed stack contains a Synapse homeserver and Element web client plus stack-specific configuration, database identity, public routes, secrets, readiness evidence, and operation history. PostgreSQL, NPM, and Coturn are shared platform services.

Matrix accounts are separate from MEM Control Plane operator accounts.

## Runtime identity

The running Control Plane exposes `/health/runtime` with durable and process identity:

- `ControlPlaneInstanceId` identifies the persistent Control Plane authority;
- `ApiProcessInstanceId` changes when the API/container process is recreated;
- `runtimeMode` distinguishes development from canonical production;
- `validationState` reports whether the current runtime contract is valid.

A supported container recreation should preserve durable Control Plane state while rotating the API process identity.

## Development versus production

Development may use repository-scoped paths and the `mem-env` harness. Production uses server-owned paths and the official bootstrap. The semantic contract is the same: Docker-host paths and Control-Plane-visible paths must agree whenever both sides need the same resource.

See [Network behavior](../operations/network-behavior.md), [Configuration](../operations/configuration.md), and [Prepare Ubuntu Server for an on-premises MEM install](../installation/ubuntu-server-on-prem.md).

---

# Create and inspect a backup

Source: `docs/backups-and-restores/create-backup.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Create and inspect a backup

Create a backup before upgrades, federation changes, TURN changes, storage work, account recovery work, or any operation whose rollback would otherwise depend on memory.

## Before you begin

Confirm that:

- the stack is visible in **Chat servers**;
- the MEM Control Plane can reach Docker and `mem-postgres`;
- the host has enough free space for the database dump and media copy;
- no unrelated host-level maintenance is changing the same files.

## Create the backup

1. Open **Chat servers** and select the stack.
2. Open **Backups & recovery**, or use **Create backup** in the stack workspace header.
3. Wait for the success notice. Record the backup ID and creation time.
4. Open the recovery source in the Backup Catalog.

The capture reads production PostgreSQL and copies recovery material into MEM-managed backup storage. It does not stop the stack or change public routes.

## What MEM captures

A native MEM backup can contain:

- a PostgreSQL dump of the Synapse database;
- `homeserver.yaml`;
- the Matrix signing key;
- the Matrix `media_store` directory;
- Element `config.json`;
- a manifest with stack identity, file statistics, and warnings;
- the Matrix and Element public-route snapshot;
- the effective TURN state recorded from the captured Synapse configuration.

The route and TURN sections are capture-time evidence. MEM does not reconstruct them later from whatever the live stack happens to look like.

## Verify success

In the Backup Catalog, confirm:

- **Origin** is `local-captured`;
- **Payload** is available;
- the source stack and backup ID are correct;
- the captured time and size are plausible;
- integrity is valid, or every warning is understood;
- Matrix server name, Matrix host, and Element host match the intended stack.

A missing signing key is critical because it is part of the Matrix federation identity. Missing media means messages may survive while uploaded files and thumbnails do not. Do not treat either warning as routine.

## Evidence to retain

Keep the backup ID, catalog entry ID, operation ID, warning text, total bytes, and capture time. When the UI reports a captured payload but the expected catalog entry is absent, do not move or rename the backup directory manually. Preserve the operation evidence and use the supported catalog backfill or diagnostics path.

> [!NOTE]
> Matrix clients retain their own end-to-end encryption material. A successful server backup does not prove that every user can decrypt historical encrypted rooms after a device loss or password reset.

---

# Create a chat server

Source: `docs/chat-servers/create.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Create a chat server

## Outcome

MEM creates a new Matrix homeserver and Element web client under a selected platform domain, publishes their Nginx Proxy Manager routes, records the runtime manifest, and runs readiness checks.

## Before you begin

Confirm that:

- MEM installation and Platform Owner enrolment are complete;
- at least one active platform domain is available;
- the selected domain has the intended wildcard DNS and certificate setup;
- the proposed stack slug is unique and suitable for long-term use;
- you have enough storage for the database, media, backups, and future growth.

Use **Chat servers → Create stack**.

## Choose the stack identity

**Display name** is the friendly label entered during creation. The current runtime list and workspace primarily identify the stack by its slug.

**Stack slug** becomes the stable runtime token. The UI converts it to lowercase and keeps letters, numbers, and dashes. Review the normalized value before submitting.

For a slug such as `family`, MEM derives names similar to:

- Matrix: `matrix-family.example.org`
- Element: `chat-family.example.org`
- containers: `mem-matrix-family` and `mem-element-family`

The actual base domain comes from the selected active MEM domain.

> [!CAUTION]
> Treat the slug and Matrix server name as durable identity. Do not create a temporary name for a production community and assume it can later be renamed without migration consequences.

## Choose the domain and images

Select the domain that should serve Matrix and Element. MEM defaults to the main platform domain when one is available.

The form also exposes the Synapse and Element image names and versions. Keep the approved defaults unless you are deliberately testing a specific image. Choosing an arbitrary tag can change compatibility and may cause candidate validation or readiness failure.

## What MEM creates

The server operation:

- resolves the selected platform domain and certificate reference;
- provisions a per-stack PostgreSQL database and role;
- creates or reuses the stack registration secret;
- prepares Matrix and Element data directories;
- writes Synapse and Element configuration;
- starts the Matrix and Element containers on `mem-gateway`;
- publishes HTTPS routes through Nginx Proxy Manager;
- writes platform TURN settings when coturn is ready;
- verifies internal HTTP, public HTTPS, NPM, and route readiness;
- persists the runtime manifest and durable operation evidence.

When coturn is not ready, creation can finish with a warning and without TURN. Voice and video may then require a later reviewed connection.

## Submit and track

Select **Create stack**. MEM accepts the request as a durable server-side operation and shows the current creation stage while Docker, files, ingress, PostgreSQL, Matrix, and Element are prepared.

The browser no longer owns the mutation lifetime. You may refresh or leave the page after the operation has been accepted; returning to the create page in the same browser profile resumes tracking of the accepted operation.

If the durable operation reaches a failed terminal state, MEM shows the failed stage and operation reference and does not automatically start another attempt. Open Diagnostics before submitting a new stack request.

## Verify success

After navigation to the stack workspace:

1. confirm Matrix and Element public addresses are present;
2. run **Doctor** if the latest readiness is not clearly healthy;
3. open **Network & domains** and confirm the expected hosts and route identifiers;
4. open **Users**, synchronize the inventory, and create the first Matrix administrator;
5. open Element and sign in.

Do not treat container existence alone as proof that the public chat service is ready.

---

# Sicherung erstellen und prüfen

Source: `docs/de/backups-und-wiederherstellen/sicherung-erstellen.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Sicherung erstellen und prüfen

Erstellen Sie vor Upgrades, Föderationsänderungen, TURN-Änderungen, Speicherarbeiten, Kontowiederherstellung oder anderen riskanten Vorgängen eine Sicherung.

## Voraussetzungen

Prüfen Sie:

- Der Stack ist unter **Chatserver** sichtbar.
- Die MEM Control Plane erreicht Docker und `mem-postgres`.
- Der Host besitzt genügend freien Speicher für Datenbankdump und Medienkopie.
- Keine parallele Host-Wartung verändert dieselben Dateien.

## Sicherung erstellen

1. **Chatserver** öffnen und den Stack auswählen.
2. **Sicherungen & Wiederherstellung** öffnen oder im Kopfbereich **Sicherung erstellen** verwenden.
3. Auf die Erfolgsmeldung warten und Sicherungs-ID sowie Zeitpunkt notieren.
4. Die Wiederherstellungsquelle im Sicherungskatalog öffnen.

Die Erfassung liest Produktions-PostgreSQL und kopiert Wiederherstellungsmaterial in MEM-verwalteten Speicher. Sie stoppt den Stack nicht und ändert keine öffentlichen Routen.

## Erfasste Bestandteile

Eine native MEM-Sicherung kann enthalten:

- PostgreSQL-Dump der Synapse-Datenbank;
- `homeserver.yaml`;
- Matrix-Signaturschlüssel;
- Matrix-Verzeichnis `media_store`;
- Element-Datei `config.json`;
- Manifest mit Stack-Identität, Dateistatistik und Warnungen;
- Snapshot der öffentlichen Matrix- und Element-Routen;
- wirksamen TURN-Zustand aus der erfassten Synapse-Konfiguration.

Routen und TURN sind Nachweise zum Erfassungszeitpunkt. MEM rekonstruiert sie nicht später aus dem dann aktuellen Laufzeitstatus.

## Erfolg prüfen

Im Sicherungskatalog bestätigen:

- **Herkunft** ist `local-captured`;
- **Payload** ist verfügbar;
- Quell-Stack und Sicherungs-ID stimmen;
- Zeitpunkt und Größe sind plausibel;
- Integrität ist gültig oder jede Warnung wurde verstanden;
- Matrix-Servername, Matrix-Host und Element-Host gehören zum richtigen Stack.

Ein fehlender Signaturschlüssel ist kritisch für die Matrix-Föderationsidentität. Fehlende Medien können dazu führen, dass Nachrichten vorhanden sind, hochgeladene Dateien und Vorschaubilder jedoch fehlen.

## Nachweise aufbewahren

Sicherungs-ID, Katalog-ID, Vorgangs-ID, Warnungen, Gesamtgröße und Erfassungszeit notieren. Wenn der Payload erfolgreich erfasst wurde, aber kein erwarteter Katalogeintrag erscheint, verschieben oder benennen Sie das Sicherungsverzeichnis nicht manuell um. Bewahren Sie die Vorgangsnachweise auf und verwenden Sie den unterstützten Katalog-Backfill- oder Diagnoseweg.

> [!NOTE]
> Matrix-Clients verwalten eigene Ende-zu-Ende-Verschlüsselungsschlüssel. Eine erfolgreiche Serversicherung beweist nicht, dass jeder Benutzer nach Geräteverlust oder Passwortzurücksetzung alte verschlüsselte Räume lesen kann.

---

# Chatserver erstellen

Source: `docs/de/chat-servers/erstellen.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Chatserver erstellen

## Ergebnis

MEM erstellt einen neuen Matrix-Homeserver und Element-Webclient unter einer ausgewählten Plattform-Domain, veröffentlicht die Nginx-Proxy-Manager-Routen, erfasst das Laufzeitmanifest und führt Bereitschaftsprüfungen aus.

## Voraussetzungen

Stellen Sie sicher, dass:

- MEM-Installation und Platform-Owner-Einrichtung abgeschlossen sind;
- mindestens eine aktive Plattform-Domain verfügbar ist;
- die gewählte Domain die beabsichtigte Wildcard-DNS- und Zertifikatskonfiguration besitzt;
- der geplante Stack-Slug eindeutig und langfristig geeignet ist;
- ausreichend Speicher für Datenbank, Medien, Sicherungen und Wachstum vorhanden ist.

Öffnen Sie **Chatserver → Stack erstellen**.

## Stack-Identität wählen

Der **Anzeigename** ist die freundliche Bezeichnung während der Erstellung. Die aktuelle Laufzeitliste und der Arbeitsbereich identifizieren den Stack hauptsächlich über den Slug.

Der **Stack-Slug** wird zum stabilen Laufzeittoken. Die Oberfläche wandelt ihn in Kleinbuchstaben um und behält Buchstaben, Zahlen und Bindestriche. Prüfen Sie den normalisierten Wert vor dem Absenden.

Für den Slug `familie` leitet MEM beispielsweise folgende Namen ab:

- Matrix: `matrix-familie.example.org`
- Element: `chat-familie.example.org`
- Container: `mem-matrix-familie` und `mem-element-familie`

Die tatsächliche Basis-Domain stammt aus der gewählten aktiven MEM-Domain.

> [!CAUTION]
> Behandeln Sie Slug und Matrix-Servernamen als dauerhafte Identität. Verwenden Sie für eine produktive Gemeinschaft keinen vorläufigen Namen in der Annahme, er lasse sich später ohne Migrationsfolgen umbenennen.

## Domain und Abbilder wählen

Wählen Sie die Domain für Matrix und Element. MEM verwendet standardmäßig die Hauptplattform-Domain, sofern vorhanden.

Das Formular zeigt außerdem Namen und Versionen der Synapse- und Element-Abbilder. Behalten Sie die freigegebenen Standardwerte bei, sofern Sie nicht gezielt eine bestimmte Version testen. Ein beliebiger Tag kann die Kompatibilität verändern und Kandidatenprüfung oder Bereitschaft scheitern lassen.

## Was MEM erstellt

Der Servervorgang:

- löst Plattform-Domain und Zertifikatsreferenz auf;
- stellt eine Stack-bezogene PostgreSQL-Datenbank und Rolle bereit;
- erstellt oder verwendet das Registrierungsgeheimnis des Stacks;
- bereitet Matrix- und Element-Datenverzeichnisse vor;
- schreibt Synapse- und Element-Konfiguration;
- startet Matrix und Element im Netzwerk `mem-gateway`;
- veröffentlicht HTTPS-Routen über Nginx Proxy Manager;
- schreibt Plattform-TURN-Einstellungen, wenn coturn bereit ist;
- prüft internes HTTP, öffentliches HTTPS, NPM und Routen;
- speichert Laufzeitmanifest und dauerhafte Vorgangsnachweise.

Ist coturn nicht bereit, kann die Erstellung mit einer Warnung und ohne TURN abgeschlossen werden. Sprache und Video benötigen dann möglicherweise später eine geprüfte Verbindung.

## Absenden und verfolgen

Wählen Sie **Stack erstellen**. MEM nimmt die Anfrage als dauerhaften serverseitigen Vorgang an und zeigt die aktuelle Erstellungsphase, während Docker, Dateien, Ingress, PostgreSQL, Matrix und Element vorbereitet werden.

Die Lebensdauer der Mutation hängt nicht mehr vom Browser ab. Nach Annahme des Vorgangs können Sie die Seite aktualisieren oder verlassen; beim erneuten Öffnen der Erstellungsseite im selben Browserprofil wird die Verfolgung des angenommenen Vorgangs fortgesetzt.

Erreicht der dauerhafte Vorgang einen fehlgeschlagenen Endzustand, zeigt MEM die fehlgeschlagene Phase und die Vorgangsreferenz an und startet nicht automatisch einen weiteren Versuch. Öffnen Sie vor einer neuen Stack-Anfrage die Diagnose.

## Erfolg prüfen

Nach dem Wechsel in den Stack-Arbeitsbereich:

1. prüfen Sie, ob öffentliche Matrix- und Element-Adressen vorhanden sind;
2. führen Sie **Diagnose** aus, wenn die Bereitschaft nicht eindeutig gesund ist;
3. prüfen Sie unter **Netzwerk & Domains** die erwarteten Hosts und Routenkennungen;
4. öffnen Sie **Benutzer**, synchronisieren Sie das Inventar und erstellen Sie den ersten Matrix-Administrator;
5. öffnen Sie Element und melden Sie sich an.

Vorhandene Container allein beweisen nicht, dass der öffentliche Chat-Dienst bereit ist.

---

# Ubuntu-Host vorbereiten

Source: `docs/de/installation/host-vorbereiten.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Ubuntu-Host vorbereiten

Bereiten Sie den Host vor dem MEM-Bootstrap vor. Das Bootstrap führt eigene Prüfungen durch und kann fehlende Pakete installieren. Speicher, Ports, DNS und der private Zugriff sollten trotzdem vorher geplant sein.

## Unterstützter Bootstrap-Host

Das mitgelieferte MEM-0.2.0-Bootstrap akzeptiert derzeit:

- Ubuntu 22.04, 24.04 oder 26.04;
- `amd64` / `x86_64`;
- `arm64` / `aarch64` nur, wenn alle benötigten Release-Images für diese Architektur verfügbar sind.

Ubuntu 24.04 LTS ist das normale Release-Ziel.

Das Skript muss über `sudo` als root ausgeführt werden.

## Ressourcenanforderungen des Bootstraps

Das Bootstrap verwendet strengere Grenzwerte als die spätere Browser-Prüfung:

- **2 CPU-Kerne** werden empfohlen; weniger erzeugt eine Warnung;
- **3.500 MB RAM** sind das harte Minimum;
- ungefähr **7.800 MB RAM** werden empfohlen;
- **20.000 MB freier Speicher** auf `/` sind das harte Minimum;
- ungefähr **50.000 MB freier Speicher** werden empfohlen.

Die Browser-Prüfung meldet zusätzlich niedrigere Schwellen für frühe Tests. Ein dortiger Erfolg ersetzt weder die Bootstrap-Grenzen noch eine echte Kapazitätsplanung.

Matrix-Historie, Medien, PostgreSQL, Images, Diagnosen, Backups, Restores und Migrationsarbeitsbereiche können deutlich mehr Speicher benötigen.

## Ubuntu-Server-Speicher prüfen

Bei Ubuntu Server mit geführtem LVM muss geprüft werden, ob das unter `/` eingebundene Logical Volume die beabsichtigte Plattenkapazität tatsächlich nutzt. Bei einer 50-GB-Platte kann sonst ungefähr die Hälfte der Volume Group unzugewiesen bleiben, während `/` nur etwa 24 GB erhält. MEM bewertet korrekt den auf `/` sichtbaren freien Speicher und nicht ungenutzte LVM-Extents.

Bearbeiten Sie vor Abschluss der Ubuntu-Installation auf der Seite **Storage configuration** `ubuntu-lv`, damit ein einfacher dedizierter MEM-Server im Wesentlichen die gesamte Kapazität von `ubuntu-vg` nutzt. Nach der Installation prüfen Sie `df -hT /`, `sudo vgs` und `sudo lvs`.

Die vollständigen Hinweise zu LVM, Dateisystem, Firewall und Split DNS finden Sie unter [Ubuntu Server für eine lokale MEM-Installation vorbereiten](ubuntu-server-on-prem.md).

## Produktions-Datenpfade

Das offizielle Servermodell trennt:

```text
/data                         privater Control-Plane-Zustand im Docker-Volume
/var/lib/message-easy-mode    hostsichtbare MEM-Betriebsdaten
/opt/mem                      installierte Software/Laufzeitpakete
```

Neue Produktions-Stackdaten dürfen nicht vom Home-Verzeichnis des Installationsbenutzers abhängen.

## Host-Pakete

Das Bootstrap prüft und installiert bei Bedarf:

```text
ca-certificates
curl
gnupg
lsb-release
jq
dnsutils
iproute2
net-tools
libsecret-tools
```

`libsecret-tools` stellt `secret-tool` bereit. Die Linux-MEM-CLI verwendet es zusammen mit einem Secret-Service-kompatiblen Schlüsselbund.

## Docker

Das Bootstrap kann Docker Engine, containerd, Buildx und das Docker-Compose-Plugin aus dem offiziellen Docker-Apt-Repository installieren.

Bei vorhandenem Docker prüfen Sie:

```bash
sudo docker info
sudo docker compose version
```

Verwenden Sie `--skip-docker-install` nur, wenn Docker und Compose bereits vollständig vorhanden sind und das Bootstrap bei Fehlen abbrechen soll.

Der Docker-Convenience-Script-Weg ist für Entwicklung und Tests vorgesehen. Das offizielle Apt-Repository ist der normale Weg.

## Ports und privater Zugriff

Planen Sie:

- den privaten Installer-Port, standardmäßig TCP **8443**;
- öffentliches HTTPS auf TCP **443** für Matrix und Element über Nginx Proxy Manager;
- optional öffentliches TCP **80** nur dann, wenn bewusst eine HTTP-/Weiterleitungs-Topologie gewünscht ist; der geführte deSEC-DNS-01-Pfad benötigt Port 80 nicht für die Zertifikatsausstellung;
- TCP **81** für die NPM-Verwaltung, ebenfalls eingeschränkt;
- TURN auf TCP/UDP **3478** sowie UDP **49160-49200** für Produktions-Relay von Sprache und Video.

Die Browser-Prüfung beobachtet außerdem 8080 und 5432. PostgreSQL und weitere Verwaltungs-/Datenports bleiben privat.

Legen Sie vorher fest, wie der Operator den privaten Port erreicht:

- vertrauenswürdiges LAN;
- Tailscale, WireGuard oder anderes VPN;
- genehmigtes Management-Netz;
- lokaler SSH-Tunnel.

Eine öffentliche IP und eine geheime URL sind keine ausreichende Sicherheitsgrenze.

## On-Premises-DNS und NAT

Für öffentliche Dienste muss DNS auf die Adresse zeigen, über die Clients den Server erreichen können. Im internen LAN kann Split DNS dieselben TURN-/Matrix-/Element-Namen bewusst direkt auf die private Serveradresse auflösen. Dadurch hängen lokale Funktionsprüfungen nicht von NAT-Reflection ab.

DNS öffnet keine Ports und erstellt keine NAT-Regeln. Befindet sich der Server hinter Firewall/Router, benötigt öffentliche TURN-Erreichbarkeit weiterhin die passende Firewall-/NAT-Regel für TCP/UDP 3478 und UDP 49160-49200.

## Domain und DNS

Bereiten Sie vor:

- eine kontrollierte Basisdomain;
- ein deSEC-Konto und Token mit Berechtigung für die Zone;
- eine ACME-Kontaktadresse;
- Kontrolle über spätere A-/AAAA-Einträge für Matrix und Element.

Der geführte Weg unterstützt derzeit deSEC DNS-01 und ein Wildcard-Zertifikat. Andere DNS-Anbieter und vorhandene Reverse Proxies sind in MEM 0.2.0 keine vollständig geführten Optionen.

## Bereits verwendeter Host

Dokumentieren Sie vor Änderungen:

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

Wenn `mem-api`, `mem-web` oder ein älteres Matrix-Easy-Mode-Compose-System vorhanden ist, stoppen Sie und verwenden Sie die Migration.

---

# Migration entscheiden und sicher vorbereiten

Source: `docs/de/migrieren/decide-and-prepare.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Migration entscheiden und sicher vorbereiten

Verwenden Sie die Migration, wenn die Quelle eine unterstützte MEM-0.1.0-Installation und das Ziel eine separate MEM-0.2.0-Control-Plane ist. Die Migration ist kein universeller Synapse-Importer und nicht der Wiederherstellungspfad für eine native MEM-Sicherung.

## Ergebnis

Quelle und Ziel sind bestätigt, die Benutzer haben ihre Verschlüsselungswiederherstellung geschützt und der Betreiber verfügt über die erforderlichen Zugriffs-, Speicher-, DNS-, Zertifikats- und Wartungsinformationen.

## Bevor Sie beginnen

Bestätigen Sie:

- Die Quelle entspricht dem von MEM Migrate erkannten älteren MEM-0.1.0-Profil.
- Der Docker-Daemon der Quelle ist erreichbar und die alten Container und Daten sind vorhanden.
- Eine separate MEM-0.2.0-Control-Plane ist installiert und betriebsbereit.
- Sie können sich als berechtigter Zielbetreiber anmelden und Step-up-Authentifizierung abschließen.
- Das Ziel hat genug freien Speicher für verschlüsseltes Paket, entschlüsseltes Arbeitsmaterial, Konvertierung, Staging und endgültige Laufzeit.
- Sie kontrollieren die bestehenden öffentlichen Matrix- und Element-Hostnamen, DNS-Einträge und Nginx-Proxy-Manager-Routen.
- Auf dem Ziel ist bereits ein geeignetes aktives Zertifikat für die öffentlichen Hostnamen vorhanden.
- Ein Wartungsfenster für Umschaltung und Produktionsprüfungen ist festgelegt.

Wenn der Source Assistant die Quelle als nicht unterstützt oder blockiert einstuft, stoppen Sie. Erzwingen Sie den Paketablauf nicht und kopieren Sie Quelldateien nicht manuell in die Ziel-Laufzeit.

## Wiederherstellung verschlüsselter Nachrichten schützen

Bitten Sie betroffene Benutzer vor der Erfassung, sich nicht aus funktionierenden Element-Sitzungen abzumelden. Jeder Benutzer sollte mindestens einen verlässlichen Wiederherstellungsweg prüfen:

- ein anderes vertrauenswürdiges und verifiziertes Gerät;
- funktionierendes Secure Backup samt Wiederherstellungsgeheimnis;
- sicher gespeicherte exportierte Raumschlüssel.

Eine Servermigration kann serverseitige Räume, Ereignisse, Konten, Medien, Konfiguration und Matrix-Identität bewahren. Nicht gesicherte Ende-zu-Ende-Verschlüsselungsschlüssel kann sie nicht neu erzeugen.

## Einen Stack planen

Der Source Assistant kann mehrere Stacks erkennen, doch jedes Paket enthält genau einen ausgewählten Stack. Notieren Sie:

- beabsichtigten Quell-Stack-Slug;
- unverändert zu erhaltenden Matrix-Hostnamen;
- bestehenden Element-Hostnamen;
- aktuellen Eigentümer der öffentlichen Routen;
- Quell- und Zielhost;
- verantwortlichen Entscheider für die Umschaltung.

Starten Sie keine parallelen Migrationen für dieselbe öffentliche Identität.

## Quelle bewahren

Erstellen Sie bei Bedarf eine Host-Sicherheitskopie nach Ihrem bestehenden Verfahren, stellen Sie diese MEM 0.2.0 jedoch nicht als native Sicherungskatalog-Quelle dar. Halten Sie den alten Host intakt und erreichbar. Der Zielablauf löscht ihn nicht.

## Bereitschaft prüfen

Sie können fortfahren, wenn Quelle und Ziel getrennt sind, der Stack eindeutig ist, Benutzer informiert wurden, DNS- und Zertifikatshoheit klar sind, die Nutzung des alten Servers zur Liveschaltung gestoppt werden kann und Sie [Rollback-Grenzen](rollback.md) gelesen haben.

Weiter: [Source Assistant installieren und privat öffnen](install-source-assistant.md).

---

# Was MEM ist

Source: `docs/de/start/what-is-mem.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# Was MEM ist

MEM ist eine unabhängige Open-Source-**Control-Plane für Betrieb, Wiederherstellung und Migration** selbst gehosteter Matrix-Umgebungen. MEM definiert kein neues Messaging-Protokoll und ersetzt Matrix nicht.

## Das zugrunde liegende Kommunikationssystem

Ein normaler, von MEM verwalteter Chatserver nutzt bekannte Upstream-Komponenten:

- **Matrix** stellt das offene Kommunikationsprotokoll bereit.
- **Synapse** ist der Matrix-Homeserver.
- **Element Web** ist der Messaging-Client im Browser.
- **PostgreSQL** speichert die Serverdaten von Synapse.
- **Nginx Proxy Manager** stellt HTTPS-Ingress und öffentliche Routen bereit.
- **coturn** stellt TURN-Relay-Dienste für Sprach- und Videoverbindungen bereit.

MEM koordiniert diese Komponenten und dokumentiert die Betriebsabläufe darum herum.

## Die Control Plane

Die MEM Control Plane ist die maßgebliche Verwaltungsanwendung. In MEM 0.2.0 arbeiten React-Oberfläche, ASP.NET-Core-API, Authentifizierung, dauerhafter Workflow-Zustand und privilegierte HostAgent-Dienste als eine Control-Plane-Anwendung. HostAgent ist eine privilegierte Bibliothek im selben Prozess und kein separater Remote-Daemon.

Die Control Plane kann MEM-eigene Docker-Ressourcen prüfen und ändern, genehmigte Host-Konfigurationen schreiben, Datenbanken bereitstellen, Routen erstellen oder prüfen, Workloads steuern und Betriebsnachweise aufbewahren.

Da sie Zugriff auf den Docker-Socket und MEM-Datenpfade hat, besitzt sie weitreichende Rechte. Halten Sie sie privat und schützen Sie Operator-Konten mit MFA und passenden Rollen.

## Die drei Ebenen

1. **Control Plane** — Operatoridentität, Workflows, Sollzustand, Diagnose und privilegierte Orchestrierung.
2. **Verwaltete Matrix-Datenebene** — Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn und stackbezogene Daten.
3. **Wiederherstellungs- und Migrationsebene** — Backup-Katalog, Restore Workspace, portable Artefakte, MEM Migrate, private Staging-Umgebungen und Produktionsübernahme.

Normale Matrix-Nachrichten laufen nicht durch die Verwaltungsoberfläche.

## Was MEM ergänzt

MEM ergänzt kontrollierte Abläufe rund um Infrastruktur, die sonst durch Compose-Dateien, Shell-Befehle, Datenbankkommandos, YAML, Proxy-Konfiguration und das Gedächtnis einzelner Betreiber gepflegt würde.

Ziel ist nicht, die Infrastruktur unsichtbar zu machen. Ziel ist, Eigentum, Absicht, Änderungen, Überprüfung, Wiederherstellung und Fehlernachweise verständlicher zu machen.

---

# Deploy MEM on a DigitalOcean Ubuntu droplet

Source: `docs/guides/digitalocean-deployment-guide.md`
Locale: en
Section: Guides
Status: advanced
Applies to: 0.2.x

# Deploy MEM on a DigitalOcean Ubuntu droplet

This is the current cloud deployment guide for MEM 0.2.x on DigitalOcean. DigitalOcean is not a special MEM runtime mode: use the same official Ubuntu bootstrap and first-time Setup as other supported servers, but keep the Control Plane private and configure the cloud firewall deliberately.

## 1. Create an Ubuntu 24.04 amd64 Droplet

Use **Ubuntu 24.04 LTS on amd64/x86_64**.

For the current MEM bootstrap:

- fewer than **2 CPU cores** produces a warning;
- less than **3,500 MB RAM** is rejected;
- about **7,800 MB RAM** is the recommended level;
- less than **20 GB free on `/`** is rejected;
- about **50 GB free on `/`** is recommended.

A DigitalOcean 2 GB Droplet does not satisfy the current 3,500 MB RAM floor. A 4 GB Droplet can satisfy the hard admission check, but it remains below MEM's roughly 7.8 GB recommendation. For a less constrained production starting point, choose approximately 8 GB RAM or more and size storage for Matrix history, media, backups, restores, and migrations.

Review [Requirements and supported environment](../start/requirements.md) before creating the server.

## 2. Understand the DigitalOcean network boundary

A normal DigitalOcean Droplet is different from an on-premises VM behind a home or office router. You normally configure a **Cloud Firewall**, not a separate WAN-to-LAN NAT port-forward rule.

```text
Internet
    |
DigitalOcean Cloud Firewall
    |
Droplet public interface
    |
MEM public services
```

The Droplet's public address is **not** a Trusted LAN address. Use **SSH / local-only** administration for the Control Plane:

```text
mem-control-plane -> 127.0.0.1:8443
operator -> SSH local port forward -> droplet
```

Do not create a public NPM route or public DNS name for the Control Plane.

## 3. Configure the Cloud Firewall

A proven minimal inbound shape for the current deSEC DNS-01 path is:

```text
22/tcp                  SSH — restrict to the operator network/IP where practical
443/tcp                 Matrix and Element HTTPS
3478/tcp                TURN
3478/udp                TURN
49160-49200/udp          TURN relay media
```

The following administration/data ports should remain non-public:

```text
81/tcp                  Nginx Proxy Manager administration
8443/tcp                MEM Control Plane
5432/tcp                PostgreSQL
9443/tcp                Portainer, when installed
```

TCP **80 is not required for MEM's current deSEC DNS-01 certificate issuance path**. Open it only when you deliberately want a public HTTP/redirect topology that uses it. HTTPS service traffic still requires TCP 443.

Keep outbound access sufficient for DNS, ACME, Ubuntu/Docker package sources, container registries, and the external services you intentionally use.

## 4. Prepare deSEC, registrar delegation, and DNSSEC

Before asking MEM to issue a production certificate, make sure the domain's public DNS authority is correct.

For the current guided deSEC path:

1. add or prepare the zone in deSEC;
2. delegate the domain at the registrar to the authoritative nameservers shown by deSEC;
3. when DNSSEC is enabled, publish the **DS values supplied by deSEC at the registrar/parent zone**;
4. do not create an apex DS record inside the deSEC child zone as a substitute for registrar delegation;
5. allow delegation and DNSSEC changes to propagate before treating certificate failures as a MEM defect.

A deSEC TXT challenge can be present while ACME validation still fails if the registrar/parent DNSSEC chain is wrong.

Point the public Matrix, Element, and TURN service names at the Droplet's public address as your deployment plan requires. The current guided certificate flow uses deSEC DNS-01 and Nginx Proxy Manager.

See [Choose the public domain and certificate plan](../installation/domain-and-certificate.md).

## 5. Run the official bootstrap

Acquire the approved release artifacts and run the normal MEM bootstrap on the Droplet. During bootstrap choose **SSH / local-only** administration.

After bootstrap, use the installer-provided SSH tunnel command from your workstation and open the private Control Plane through the local forwarded address. Complete:

1. first Platform Owner setup;
2. server checks;
3. public-domain and certificate review;
4. platform installation;
5. final platform verification and handoff.

Do not publish `8443` to the Internet to avoid using the SSH tunnel.

## 6. Allow certificate issuance to settle

DNS-01 issuance is not necessarily instant. MEM waits for the authoritative DNS servers to agree on the challenge before continuing to ACME validation.

It is possible for one authoritative nameserver to return the new `_acme-challenge` TXT record before another. If MEM reports an authoritative DNS visibility timeout:

- keep the existing Domain/installation state;
- check the authoritative nameservers rather than deleting and recreating the Domain;
- allow DNS to converge;
- use the supported Retry path on the same durable operation.

A successful production issuance can take several minutes while DNS readiness, ACME validation, finalization, protected storage, and NPM import complete.

## 7. Create and verify the first chat server

Do not stop at **Platform setup complete**. Create the first Matrix + Element stack and require:

- the stack to reach **Healthy**;
- public Matrix and Element routes to work over HTTPS;
- users to be able to sign in and exchange a real message;
- Diagnostics to contain no unexplained urgent incident;
- PostgreSQL, NPM, and Coturn to remain healthy.

Run **Doctor** from the stack workspace when available and retain the result if this is an acceptance test.

## 8. Prove TURN relay, not only call success

A successful Element call does **not** by itself prove that TURN was used. WebRTC may establish a direct peer-to-peer path.

Use two clients on genuinely different networks where practical. During a fresh call, an operator can observe the server with `tcpdump` if it is installed and packet capture is acceptable in the environment:

```bash
sudo tcpdump -ni any \
  '(udp port 3478 or tcp port 3478 or udp portrange 49160-49200)'
```

Interpret the result carefully:

- traffic on `3478/tcp` or `3478/udp` shows TURN/STUN negotiation activity;
- **sustained UDP traffic in `49160-49200` during the call is strong evidence that Coturn is relaying media**;
- browser WebRTC diagnostics showing a selected ICE candidate of type `relay` are useful additional evidence.

Packet captures can expose client IP addresses and timing information. Stop the capture after the bounded test and do not publish it as an unrestricted support artifact.

See [Configure voice and video TURN](../chat-servers/voice-and-video.md).

## 9. Reboot and recovery acceptance

Before treating a new production server as fully accepted, schedule one controlled Droplet reboot after the platform and first stack are healthy.

After reboot confirm:

- the Control Plane is again available only through the private SSH/local path;
- PostgreSQL, NPM, Coturn, and the managed stack recover without manual reconstruction;
- Matrix and Element return to Healthy;
- an existing user can send a new message;
- Diagnostics does not show an unexplained recovery failure.

This verifies durable host/runtime recovery rather than only first-boot success.

## 10. Create the first off-host backup

Create a native MEM backup after the first stack is accepted. Export a portable ZIP and store at least one copy away from the Droplet.

For important deployments, a **Private Restore Test** provides stronger evidence that the backup is usable without replacing production.

See [Back up and restore](../backups-and-restores/index.md).

## Acceptance checklist

A DigitalOcean deployment is ready for normal operation when you have confirmed:

- Ubuntu 24.04 amd64 and the MEM bootstrap resource floor;
- SSH/local-only Control Plane access on `127.0.0.1:8443`;
- only the intended public Cloud Firewall ports are open;
- deSEC delegation and DNSSEC/DS state are correct;
- a production wildcard certificate is active;
- PostgreSQL, NPM, and Coturn are healthy;
- the first Matrix + Element stack is Healthy and publicly reachable;
- a real off-network call has evidence of TURN relay use;
- a controlled reboot recovers the platform and stack;
- the first native backup has been exported off-host;
- Diagnostics contains no unexplained urgent incident.

---

# Prepare the Ubuntu host

Source: `docs/installation/prepare-host.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Prepare the Ubuntu host

Prepare the host before running the MEM bootstrap. The bootstrap performs its own checks and may install missing packages, but it should not be the first time you consider storage, ports, DNS, or private access.

## Supported bootstrap host

The supplied MEM 0.2.0 bootstrap currently accepts:

- Ubuntu 22.04, 24.04, or 26.04;
- `amd64` / `x86_64`;
- `arm64` / `aarch64` only when every required release image is available for that architecture.

Ubuntu 24.04 LTS is the normal release target.

The script must run as root through `sudo`.

## Bootstrap resource requirements

The bootstrap applies a stricter admission floor than the later browser preflight:

- **2 CPU cores** are recommended; fewer produces a warning;
- **3,500 MB RAM** is the hard bootstrap minimum;
- approximately **7,800 MB RAM** is recommended;
- **20,000 MB free disk** on `/` is the hard bootstrap minimum;
- approximately **50,000 MB free disk** is recommended.

The browser preflight also reports lower early-testing warning thresholds. Passing those later checks does not override the bootstrap floor or constitute production sizing.

Matrix history, media, PostgreSQL, container images, diagnostics, backups, restores, and migration workspaces can grow well beyond these minimums.

## Ubuntu Server storage check

On Ubuntu Server with guided LVM, confirm that the logical volume mounted at `/` actually uses the intended disk capacity. A 50 GB disk can otherwise leave roughly half of the volume group unassigned while `/` receives only about 24 GB. MEM correctly evaluates the space visible on `/`, not unused LVM extents.

Before completing Ubuntu installation, edit `ubuntu-lv` on the Storage configuration screen so a simple dedicated MEM server uses essentially the full `ubuntu-vg`. After installation, verify with `df -hT /`, `sudo vgs`, and `sudo lvs`.

See [Prepare Ubuntu Server for an on-premises MEM install](ubuntu-server-on-prem.md) for the full LVM, filesystem, firewall, and split-DNS guidance.

## Production data locations

The official server model separates:

```text
/data                         private Control Plane Docker-volume state
/var/lib/message-easy-mode    host-visible MEM operational data
/opt/mem                      installed software/runtime payloads
```

New production stack data must not depend on the installer account's home directory.

## Host packages

The bootstrap checks and can install:

```text
ca-certificates
curl
gnupg
lsb-release
jq
dnsutils
iproute2
net-tools
libsecret-tools
```

`libsecret-tools` provides `secret-tool`, which the Linux MEM CLI uses with a Secret Service-compatible keyring.

## Docker

The bootstrap can install Docker Engine, containerd, Buildx, and the Docker Compose plugin from Docker's official Ubuntu apt repository.

When Docker is already installed, confirm:

```bash
sudo docker info
sudo docker compose version
```

Use `--skip-docker-install` only when Docker and Compose are already present and you want the bootstrap to fail rather than modify them.

The convenience-script Docker path exists for development and testing. The official apt repository path is the normal installation method.

## Ports and private access

Plan for:

- the private installer port, default TCP **8443**;
- public HTTPS on TCP **443** for Matrix and Element through Nginx Proxy Manager;
- optional public TCP **80** only when you deliberately want an HTTP/redirect topology; the guided deSEC DNS-01 certificate path does not require port 80 for issuance;
- NPM administration on TCP **81**, which should also be restricted;
- TURN TCP/UDP **3478** and UDP relay ports **49160-49200** for production voice/video relay.

The browser preflight also watches 8080 and 5432 for unexpected listeners. Keep PostgreSQL and other administration/data ports private.

Before installation, decide how the operator will reach the private port:

- trusted LAN;
- Tailscale, WireGuard, or another VPN;
- an approved management network;
- SSH local forwarding.

Do not rely on a public IP and a secret URL as the security boundary.

## On-premises DNS and NAT

For public services, DNS must point at the address through which clients can reach the server. On an internal LAN, split DNS can deliberately resolve the same TURN/Matrix/Element names directly to the private server address. This avoids making local functional checks depend on NAT reflection.

DNS does not open ports or create NAT rules. If the server is behind a firewall/router, public TURN reachability still requires the appropriate firewall/NAT policy for TCP/UDP 3478 and UDP 49160-49200.

## Domain and DNS

Prepare:

- a base domain you control;
- a deSEC account and token with permission for that DNS zone;
- an ACME contact email;
- control of the public A/AAAA records that future Matrix and Element hostnames will use.

The guided certificate flow currently supports deSEC DNS-01 and a wildcard certificate. Other DNS providers and existing reverse proxies are not first-class guided paths in MEM 0.2.0.

## Before changing an existing host

Record the existing Docker state:

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

If you see `mem-api`, `mem-web`, or an older Matrix Easy Mode Compose deployment, stop and use the migration path. If you see other `mem-*` resources, investigate ownership before continuing.

---

# Decide whether to migrate and prepare safely

Source: `docs/migrate/decide-and-prepare.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Decide whether to migrate and prepare safely

Use migration when the source is a supported MEM 0.1.0 installation and the destination is a separate MEM 0.2.0 Control Plane. Migration is not a universal Synapse importer and is not the recovery path for a native MEM backup.

## Outcome

You have a confirmed source and target, users have protected their encryption recovery, and the operator has the access, storage, DNS, certificate, and maintenance information required to proceed.

## Before you begin

Confirm all of the following:

- the source is the legacy MEM 0.1.0 product profile detected by MEM Migrate;
- the source Docker daemon is reachable and the legacy containers and data still exist;
- a separate MEM 0.2.0 Control Plane is installed and operational;
- you can sign in as an authorized target operator and complete step-up authentication;
- the target has enough free storage for the encrypted package, decrypted working material, conversion, staging, and the final runtime;
- you control the existing Matrix and Element public hostnames, DNS records, and Nginx Proxy Manager routes;
- the target already has a suitable active certificate for the public hostnames;
- you have a maintenance window for the public cutover and production checks.

If the Source Assistant classifies the source as unsupported or blocked, stop. Do not force the package workflow or copy source files manually into the target runtime.

## Protect encrypted message recovery

Before capture, tell affected users not to sign out of working Element sessions. Each user should confirm at least one dependable recovery path, such as:

- another trusted and verified device;
- working Secure Backup and its recovery secret;
- an exported room-key file stored securely.

A server migration can preserve server-side rooms, events, accounts, media, configuration, and Matrix identity. It cannot recreate end-to-end encryption keys that users never backed up.

## Plan one stack

The Source Assistant may discover more than one stack, but each migration package contains exactly one selected stack. Record:

- the intended source stack slug;
- the Matrix public hostname that must remain unchanged;
- the existing Element hostname;
- the current public route owner;
- the source host and target host;
- the operator responsible for the cutover decision.

Do not start parallel migrations for the same public identity.

## Preserve the source

Take any reasonable host-level safety copy your existing operating procedure requires, but do not present it to MEM 0.2.0 as a native Backup Catalog entry. Keep the legacy host intact and reachable. The guided target workflow does not delete it.

## Verify readiness

You are ready to continue when:

- the source and target are separate and reachable;
- the intended source stack is unambiguous;
- users have been warned about encryption recovery;
- public DNS and certificate ownership are understood;
- you know who can stop normal use of the old server at go-live;
- you have read [Rollback boundaries](rollback.md).

Next: [Install and open the Source Assistant privately](install-source-assistant.md).

---

# Configuration

Source: `docs/operations/configuration.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Configuration

MEM 0.2.x is configured through the official bootstrap, first-time Setup, server-owned runtime configuration, and supported Control Plane settings. The current product is not operated by hand-editing a legacy deployment `.env` file.

## Bootstrap-owned configuration

The bootstrap establishes the private Control Plane boundary, image identity, persistent volume, host-data bind, certificate, and runtime environment.

For production host-visible data, the canonical root is:

```text
/var/lib/message-easy-mode
```

and the current production children include:

```text
mem-data
instances
platform/coturn
seq
```

The private Control Plane state remains under `/data` inside the persistent `mem-control-plane-data` volume.

## Setup-owned configuration

First-time Setup owns the managed platform plan: domain, certificate workflow, PostgreSQL, NPM, shared Coturn, support tools, and final verification.

After Setup completes, normal changes belong to the corresponding MEM workspace rather than to manual Docker or configuration-file edits.

## Runtime configuration is authoritative

Use `/health/runtime`, System Information, Diagnostics, and the relevant service/stack pages to establish the currently running mode and identity. Do not infer runtime truth from a port number or container name alone.

## Do not edit generated stack files as a normal workflow

Synapse and Element configuration under the managed instance root are MEM-owned operational resources. Manual edits can create drift that MEM may refuse to overwrite.

For supported changes use the appropriate stack, TURN, federation, backup/restore, or migration workflow.

## Secrets

Do not put passwords, TOTP secrets, recovery codes, TURN shared secrets, private keys, access tokens, or unrestricted connection strings into documentation notes, support tickets, or command history.

## Development overrides

The developer harness deliberately supplies repository-scoped paths and runtime-specific networking. Those are development contracts and must not be copied into an official server installation.

---

# Releases

Source: `docs/releases/index.md`
Locale: en
Section: Releases
Status: supported
Applies to: 0.2.x

# Releases

MEM 0.2.x uses a release-candidate qualification process before final publication. Release-specific artifacts, image digests, upgrade instructions, and known limitations are authoritative for the candidate or release being installed.

## Current product line

The documentation in the normal `en/` and `de/` trees targets MEM **0.2.x**. Historical 0.1.0 material is preserved separately and is marked legacy.

## Before installing a candidate or release

Confirm:

- exact version and source commit;
- immutable Control Plane image digest;
- release artifact checksums;
- supported Ubuntu/architecture;
- release-specific upgrade or fresh-install instructions;
- open qualification observations that affect your deployment.

Do not apply an old Compose/`stack.sh` procedure to MEM 0.2.x merely because it appears in historical notes.

## Legacy release notes

- [Version 0.1.0 legacy notes](0.1.0.md) — historical first public release.

---

# What MEM is

Source: `docs/start/what-is-mem.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# What MEM is

MEM is an independent open-source **operations, recovery, and migration control plane** for self-hosted Matrix environments. It does not define a new messaging protocol and it does not replace Matrix.

## The underlying communications system

A normal MEM-managed chat server uses familiar upstream components:

- **Matrix** provides the open communications protocol.
- **Synapse** provides the Matrix homeserver.
- **Element Web** provides the browser messaging client.
- **PostgreSQL** stores Synapse server data.
- **Nginx Proxy Manager** provides HTTPS ingress and public routing.
- **coturn** provides TURN relay service for voice and video.

MEM coordinates those components and records the operator workflows around them.

## The Control Plane

The MEM Control Plane is the authoritative administration application. In MEM 0.2.0, the React interface, ASP.NET Core API, authentication, durable workflow state, and privileged HostAgent services operate as one control-plane application. HostAgent is an in-process privileged runtime library, not a separate remote daemon.

The control plane can inspect and change MEM-owned Docker resources, write approved host-side configuration, provision databases, create or verify routes, control workloads, and retain operation evidence.

Because it has access to the Docker socket and MEM data roots, it is highly privileged. Keep it private and protect operator accounts with MFA and appropriate roles.

## The three planes

1. **Control plane** — operator identity, workflows, expected state, diagnostics, and privileged orchestration.
2. **Managed Matrix data plane** — Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn, and per-stack data.
3. **Recovery and migration plane** — Backup Catalog, Restore Workspace, portable artifacts, MEM Migrate, private staging, and production adoption.

Ordinary Matrix users do not send messages through the administration UI.

## What MEM adds

MEM adds controlled workflows around infrastructure that would otherwise be maintained through a mixture of Compose files, shell commands, database commands, YAML, proxy configuration, and operator memory.

Its purpose is not to make the infrastructure invisible. Its purpose is to make ownership, intent, changes, verification, recovery, and failure evidence easier to understand.

---

# Optional pgAdmin

Source: `docs/tools/optional-pgadmin.md`
Locale: en
Section: Tools
Status: advanced
Applies to: 0.2.x

# Optional pgAdmin

pgAdmin is **not required** for MEM 0.2.x and is not currently a first-class MEM-managed platform service.

If an experienced operator chooses to run pgAdmin separately, treat it as external database-administration tooling rather than as a MEM lifecycle surface.

## Appropriate use

Use it for deliberate read-oriented investigation such as:

- confirming PostgreSQL connectivity;
- inspecting database/schema state during a known incident;
- validating a support hypothesis when MEM evidence points specifically at PostgreSQL.

## Do not use it as a shortcut

Do not edit MEM-owned database state to make a failed operation appear successful. Do not bypass backup/restore, migration, user, or stack lifecycle workflows by modifying records directly.

A direct database edit can make the Control Plane's durable state disagree with Docker resources, files, routes, operation history, or security authority.

## Access and secrets

Keep pgAdmin private and independently authenticated. Do not store or paste database administrator credentials into MEM documentation/support material.

For normal platform evidence start with MEM Diagnostics and the relevant workspace. For container-level investigation use [Portainer](optional-portainer.md) where appropriate.

---

# Operate domains, certificates, and renewal

Source: `docs/operations/domains-and-certificates.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Operate domains, certificates, and renewal

Use **Domains** as the operator workspace for public Domain registration, Domain-owned certificates, active-certificate selection, main-platform Domain selection, and renewal readiness.

The important ownership rule is:

```text
Domain
  → owns certificates
  → has at most one active production certificate
  → has its own renewal policy and renewal credential
```

The main-platform Domain is a separate role. A certificate being active for its Domain does not by itself make that Domain the main platform Domain.

## Register a Domain first

Open **Domains → Add Domain** to create the Domain registry entry.

The MEM 0.2.x guided provider is **deSEC**. Add Domain records the Domain, provider, and DNS zone needed for later work. It is deliberately **registration only**:

- it does not contact deSEC;
- it does not create `_acme-challenge` records;
- it does not request a certificate;
- it does not store a certificate-issuance token;
- it does not enable automatic renewal.

After registration, open the Domain's own workspace to issue or manage certificates.

> [!NOTE]
> First-time Setup has its own reviewed platform-installation certificate plan. The post-install **Add Domain** workflow is intentionally smaller and must not be treated as the old Setup wizard repeated inside Domains.

## Use the global Certificates page as inventory

**Domains → Certificates** is the fleet-wide certificate inventory. It helps you find certificates across all registered Domains and then open the owning Domain or certificate details.

Certificate ownership and normal lifecycle mutations belong to the Domain resource. Do not treat the fleet inventory as a cross-Domain assignment tool.

The collapsed **Advanced ingress diagnostics** area is different: it contains bounded low-level NPM/ingress diagnostic operations for troubleshooting. Those diagnostics do not replace the Domain-owned certificate model.

## Issue a certificate from its owning Domain

Open the Domain, then **Certificates → Issue certificate**.

MEM locks the request to the owning Domain. The guided path derives the wildcard certificate name from the Domain and uses deSEC DNS-01. Supply the one-time credential requested by the issuance form; do not place DNS tokens in support records, screenshots, or ordinary logs.

Certificate issuance is a **server-owned durable operation**. The browser shows a human-readable phase timeline derived from server progress, for example:

```text
Prepare certificate request
Publish DNS-01 challenge
Wait for authoritative DNS readiness
Validate DNS challenge with Let's Encrypt
Finalize and download certificate
Store and validate certificate
Secure production renewal credential   (production when applicable)
```

The exact current phase is authoritative. You can navigate away and return; MEM rediscovers the durable operation rather than making the browser own the work.

Once the server accepts an issuance request, the full request form collapses into a safe request summary plus the durable progress/result view. MEM does **not** redisplay the deSEC token. After a terminal result, **Issue another certificate** or **Try another issuance** returns to a fresh request form with the token blank.

While issuance is running, the global **Certificates** inventory, the owning Domain's **Certificates** page, and Domain detail can show compact active-issuance activity with **View progress**. These surfaces lead back to the same server-owned operation; they do not create another issuance request.

### Technical evidence versus current state

The phase timeline is the normal operator view. Expand **Technical evidence** when troubleshooting.

Evidence rows are historical observations recorded during the operation. A row labelled, for example, **Recorded: Running** means that state was observed when the evidence was emitted. It does **not** mean a terminal operation that now says **Succeeded** is still running.

If issuance fails and MEM creates an incident, use the Diagnostics deep link from the operation result. Preserve the operation/incident identity when asking for support.

A successful historical issuance remains useful evidence if its resulting certificate is later deleted. In that case the issuance workspace records that the certificate no longer exists instead of presenting a dead **Open issued certificate** link.

## Understand staging and production

Use Let's Encrypt **staging** to test DNS-01 behavior without consuming normal production issuance limits.

A staging certificate:

- is not trusted by normal browsers;
- remains owned by its Domain;
- cannot become the Domain's active production certificate;
- cannot make the Domain eligible to become Main;
- does not create or replace the production renewal credential or renewal policy;
- must not be treated as a normal Matrix/Element ingress certificate.

Use **production** for real public service. MEM must complete the production validation and activation contract before the Domain's active-certificate pointer changes. A successful production issuance normally stores the verified deSEC credential in protected Domain storage, records the ACME contact, and enables the default automatic-renewal policy. If that final renewal-credential step returns a warning, the certificate itself can still be valid, but the Renewal workspace must be repaired before unattended renewal is considered ready.

## Active certificate and main Domain are separate

A Domain can own certificate history while one eligible production certificate is active.

When changing the active certificate, MEM uses the Domain-scoped operation and validates that the certificate belongs to that Domain and is eligible for production use.

A non-main Domain can be promoted to **Main** only when the current readiness contract allows it. Staging certificates are never a shortcut around that requirement.

## Use Renewal as a fleet view, then open one Domain

Open **Domains → Renewal** for a fleet-level status view. It summarizes renewal readiness across Domains, including whether automatic renewal is enabled, whether a protected DNS renewal credential is present, certificate expiry, and the next automatic attempt when one is scheduled.

The fleet page is intentionally read-only for sensitive renewal configuration. Select **Open renewal** for a Domain to use its detailed renewal workspace.

The Domain renewal workspace contains the protected operations, including:

- enrol or rotate the deSEC renewal credential for a historical Domain, recovery, or planned credential rotation;
- enable or disable automatic renewal when allowed;
- **Renew now** or retry a failed renewal when allowed;
- current/last operation state;
- durable renewal history and Diagnostics links.

The deSEC renewal token is verified and stored protected server-side. MEM does not return the stored token to the browser. Successful production issuance normally performs this enrolment automatically; manual enrolment is the recovery/rotation path when the workspace reports that a credential is required.

## How automatic renewal behaves

Automatic renewal is server-owned. When an eligible production certificate enters its renewal window, MEM can issue and validate a replacement, activate it in NPM when required, verify ingress, and only then switch the Domain to the renewed certificate.

The previous certificate is retained as history. A failure before safe activation must not silently move the active pointer to an unverified replacement.

If renewal is disabled or the Domain has no renewal credential, the fleet and detail pages should say so explicitly rather than implying that unattended renewal is configured.

## Delete certificates and Domains explicitly

Certificate deletion belongs to the owning Domain. Open the certificate detail and use **Delete certificate** when the certificate is no longer required. MEM deliberately blocks deletion when the certificate is the main-platform certificate, is still required by a live Chat Server, or is still consumed by NPM ingress. Staging certificates and otherwise-unused production certificates can be removed safely.

Deleting the active production certificate from an unused non-main Domain deliberately clears that Domain's active-certificate pointer; it does not delete the Domain. A cleanup failure must leave the registry/storage state intact rather than reporting a false success.

Domain deletion is not a hidden certificate cascade. Delete Domain-owned certificates explicitly first, then use **Delete domain** from Domain detail. The main Domain and Domains still referenced by active Chat Servers remain protected.

Deleting an active production certificate does not automatically erase a verified renewal credential or policy. Until another production certificate exists, Renewal can therefore report that the policy and credential are configured while a production certificate is still required.

## A safe operator sequence

For a new post-install Domain:

1. open **Domains → Add Domain** and register the Domain;
2. open that Domain;
3. issue a staging certificate first when DNS delegation or provider access is uncertain, and confirm it remains non-active;
4. issue the production certificate;
5. confirm the eligible production certificate is active for the Domain and Renewal reports the protected credential/policy state expected from successful production issuance;
6. use manual renewal credential enrolment or policy controls only when the Renewal workspace reports recovery/rotation is required or you intentionally change the policy;
7. set the Domain as **Main** only when the readiness contract permits it;
8. use Diagnostics/evidence if any durable operation fails.

## Do not infer more than the state proves

Keep these distinctions clear:

- **deSEC registered as provider** does not mean a reusable renewal credential is enrolled;
- **certificate exists** does not mean it is the active certificate;
- **active for Domain** does not automatically mean **main platform certificate**;
- **staging succeeded** does not make a staging certificate production-eligible;
- an old evidence row does not override the current terminal operation state.

---

# Understand the Backup Catalog

Source: `docs/backups-and-restores/backup-catalog.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Understand the Backup Catalog

Open **Backups** to view every recovery-ready source known to MEM. Local captures and imported ZIPs share one catalog because both must enter the same restore workflow.

## Origin and identity

Each entry has a stable catalog entry ID and one origin:

- `local-captured` — captured directly from a managed stack;
- `imported-zip` — validated and materialised from a portable MEM export.

The detail page can also show source stack slug, source backup ID, validation upload ID, manifest version, MEM version, Matrix server name, Matrix host, Element host, capture time, and import time.

## Payload state

The payload state describes the managed recovery material:

- `available` — MEM can resolve the payload for restore;
- `materialising` — a validated archive is being copied into catalog-owned storage;
- `failed` — materialisation or payload preparation failed;
- `removed` — the catalog payload was intentionally deleted.

A catalog record may remain readable after its payload is removed so that provenance and audit history are not silently erased.

## Integrity and advisories

Integrity status can be `valid`, `warning`, `invalid`, or `unknown`.

Imported archives can also carry **advisories**. Advisories are non-blocking operator guidance, such as secure handling of signing material, old-server identity risk, route/TURN context, or regenerated-export provenance. They are deliberately separate from checksum or structural integrity failures.

Do not ignore a warning merely because the Restore button is enabled. Read the integrity summary and every advisory before production recovery.

## Search and filtering

The catalog supports search, origin, stack, and sort filters. Use these fields rather than relying only on a display name. Two captures may have similar names but different backup IDs, times, payload states, or Matrix identities.

## Restore-session boundary

Open an available entry and choose **Restore**. MEM creates or resumes a durable Restore Workspace and redirects to `/restores/<restoreSessionId>`.

The catalog entry remains the source. The restore-session ID is the workspace identity. An upload validation ID is neither.

## Lifecycle boundary

An active restore blocks permanent deletion of its source. The detail page shows whether a payload is present, whether an active Restore Workspace exists, and why deletion is blocked.

Permanent catalog deletion is irreversible. It can remove the managed payload, retained original upload, generated portable exports, and catalog linkage from older restore attempts. Read [Delete and retain recovery material](deletion-retention.md) before using it.

---

# Use the stack workspace

Source: `docs/chat-servers/workspace.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Use the stack workspace

## Outcome

Use the stack workspace as the normal operating surface for one Matrix + Element stack.

Open **Chat servers**, then select the stack name or **Inspect**.

## Header actions

The workspace header provides stack-scoped actions:

- **Open Element** opens the recorded public Element URL in a new tab.
- **Matrix API** opens the recorded homeserver address.
- **Refresh** reloads the current projections.
- **Run doctor** checks Nginx Proxy Manager, configured routes, internal HTTP, and public HTTPS.
- **Create backup** starts the stack backup workflow.

A recorded URL is not itself a live health check. Use Refresh and Doctor when current state matters.

## Overview

The Overview page combines:

- the last known runtime status and verification time;
- Matrix and Element public hosts;
- the latest Doctor result retained in the current browser session;
- the most recent locally recorded backup operation;
- recent control-plane operations with requested, current, and completed states.

The **last verified** value is historical evidence. It does not mean the endpoint was checked at the moment you opened the page.

## Workspace sections

**Services** shows Matrix and Element runtime-manifest facts, including containers, internal addresses, public routes, certificate identifiers, database metadata, and paths. Most values are read-only evidence.

**Storage & media** shows recovery-relevant files and media usage.

**Users** synchronizes Matrix accounts and provides guarded account workflows.

**Backups** and **Recovery** enter the dedicated recovery plane.

**Federation** manages Public, Restricted, and Local-only federation through reviewed operations.

**Network & domains** shows recorded public and internal delivery facts.

**Voice & video** inspects and manages the stack TURN association.

**Diagnostics** presents Doctor evidence for the current browser session and links into the broader diagnostics surface.

## Read-only evidence versus mutation

Many workspace panels intentionally do not expose generic edit, start, stop, or restart controls. MEM uses dedicated reviewed workflows for changes that can affect identity, availability, federation, calls, or recovery.

Do not edit Synapse YAML, Element config, NPM routes, or MEM-owned database records merely because a workspace card shows their paths or identifiers. An out-of-band change can create drift and disable safe automation.

---

# Sicherungskatalog verstehen

Source: `docs/de/backups-und-wiederherstellen/sicherungskatalog.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Sicherungskatalog verstehen

Unter **Sicherungen** sehen Sie alle MEM bekannten, wiederherstellbaren Quellen. Lokale Erfassungen und importierte ZIPs verwenden denselben Katalog und denselben Wiederherstellungsablauf.

## Herkunft und Identität

Jeder Eintrag besitzt eine stabile Katalog-ID und eine Herkunft:

- `local-captured` — direkt von einem verwalteten Stack erfasst;
- `imported-zip` — aus einem validierten portablen MEM-Export materialisiert.

Die Detailseite kann Quell-Stack, Sicherungs-ID, Upload-Validierungs-ID, Manifestversion, MEM-Version, Matrix-Servername, Matrix-Host, Element-Host sowie Erfassungs- und Importzeit anzeigen.

## Payload-Zustand

- `available` — der Payload kann für die Wiederherstellung aufgelöst werden;
- `materialising` — ein validiertes Archiv wird in Katalogspeicher kopiert;
- `failed` — Materialisierung oder Vorbereitung ist fehlgeschlagen;
- `removed` — der Payload wurde bewusst gelöscht.

Ein Katalogdatensatz kann nach Payload-Löschung lesbar bleiben, damit Herkunft und Auditverlauf erhalten bleiben.

## Integrität und Hinweise

Integrität kann `valid`, `warning`, `invalid` oder `unknown` sein.

Importierte Archive können zusätzlich **Hinweise** enthalten. Diese sind nicht blockierende Betreiberinformationen, etwa zum sicheren Umgang mit Signaturmaterial, zur Identitätsgefahr durch einen alten Server oder zu Routen- und TURN-Snapshots. Sie sind bewusst von Struktur- oder Prüfsummenfehlern getrennt.

Lesen Sie Integritätszusammenfassung und alle Hinweise, auch wenn die Wiederherstellungsaktion verfügbar ist.

## Suche und Filter

Der Katalog kann nach Text, Herkunft, Stack und Sortierung gefiltert werden. Verlassen Sie sich nicht nur auf den Anzeigenamen. Ähnliche Einträge können unterschiedliche Sicherungs-IDs, Zeiten, Zustände oder Matrix-Identitäten besitzen.

## Grenze zur Wiederherstellungssitzung

Bei einem verfügbaren Eintrag **Wiederherstellen** wählen. MEM erstellt oder öffnet einen dauerhaften Wiederherstellungsarbeitsbereich unter `/restores/<restoreSessionId>`.

Der Katalogeintrag bleibt die Quelle. Die Wiederherstellungssitzungs-ID ist die Arbeitsbereichsidentität. Eine Upload-Validierungs-ID ist keines von beidem.

## Lebenszyklus

Eine aktive Wiederherstellung blockiert die permanente Löschung ihrer Quelle. Die Detailseite zeigt Payload-Verfügbarkeit, aktiven Arbeitsbereich und Löschblocker.

Die permanente Kataloglöschung ist irreversibel und kann Payload, Original-Upload, erzeugte portable Exporte sowie Katalogverknüpfungen älterer Versuche entfernen. Lesen Sie vorher [Wiederherstellungsmaterial sicher löschen und aufbewahren](loeschen-und-aufbewahren.md).

---

# Stack-Arbeitsbereich verwenden

Source: `docs/de/chat-servers/arbeitsbereich.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Stack-Arbeitsbereich verwenden

## Ergebnis

Verwenden Sie den Stack-Arbeitsbereich als normale Betriebsoberfläche für einen Matrix- und Element-Stack.

Öffnen Sie **Chatserver** und wählen Sie den Stack-Namen oder **Prüfen**.

## Aktionen in der Kopfzeile

Die Kopfzeile bietet Stack-bezogene Aktionen:

- **Element öffnen** öffnet die erfasste öffentliche Element-URL.
- **Matrix API** öffnet die erfasste Homeserver-Adresse.
- **Aktualisieren** lädt die aktuellen Projektionen neu.
- **Diagnose ausführen** prüft Nginx Proxy Manager, konfigurierte Routen, internes HTTP und öffentliches HTTPS.
- **Sicherung erstellen** startet den Stack-Sicherungsworkflow.

Eine erfasste URL ist keine aktuelle Live-Prüfung. Verwenden Sie Aktualisieren und Diagnose, wenn der gegenwärtige Zustand wichtig ist.

## Überblick

Der Überblick kombiniert:

- letzten bekannten Laufzeitstatus und Prüfzeitpunkt;
- öffentliche Matrix- und Element-Hosts;
- das letzte in der aktuellen Browsersitzung behaltene Diagnoseergebnis;
- den zuletzt lokal erfassten Sicherungsvorgang;
- aktuelle Control-Plane-Vorgänge mit angefordertem, aktuellem und abgeschlossenem Zustand.

**Zuletzt geprüft** ist historischer Nachweis. Der Wert bedeutet nicht, dass der Endpunkt beim Öffnen der Seite erneut geprüft wurde.

## Bereiche des Arbeitsbereichs

**Dienste** zeigt Fakten aus dem Laufzeitmanifest zu Matrix und Element, darunter Container, interne Adressen, öffentliche Routen, Zertifikatskennungen, Datenbankmetadaten und Pfade. Die meisten Werte sind schreibgeschützte Nachweise.

**Speicher & Medien** zeigt wiederherstellungsrelevante Dateien und Mediennutzung.

**Benutzer** synchronisiert Matrix-Konten und bietet geschützte Kontoworkflows.

**Sicherungen** und **Wiederherstellung** öffnen die eigene Recovery Plane.

**Föderation** verwaltet öffentliche, eingeschränkte und lokale Föderation über geprüfte Vorgänge.

**Netzwerk & Domains** zeigt erfasste öffentliche und interne Zustellungsdaten.

**Sprache & Video** prüft und verwaltet die TURN-Zuordnung des Stacks.

**Diagnose** zeigt Nachweise der aktuellen Browsersitzung und verweist auf die breitere Diagnoseoberfläche.

## Schreibgeschützter Nachweis oder Änderung

Viele Bereiche bieten absichtlich keine allgemeinen Bearbeitungs-, Start-, Stopp- oder Neustartaktionen. MEM verwendet eigene geprüfte Workflows für Änderungen an Identität, Verfügbarkeit, Föderation, Anrufen oder Wiederherstellung.

Bearbeiten Sie Synapse-YAML, Element-Konfiguration, NPM-Routen oder MEM-eigene Datenbankeinträge nicht nur deshalb direkt, weil ihre Pfade oder Kennungen angezeigt werden. Änderungen außerhalb von MEM können Drift erzeugen und sichere Automatisierung blockieren.

---

# Bootstrap-Installer ausführen

Source: `docs/de/installation/bootstrap-installer.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Bootstrap-Installer ausführen

Das Host-Bootstrap bereitet den Rechner vor und startet die private MEM Control Plane. Die Installation der verwalteten Plattform erfolgt anschließend im Browser.

## Zuerst trocken prüfen

```bash
sudo ./install.sh --dry-run
```

Der Dry Run prüft Betriebssystem, Ressourcen, Pakete, Docker, Compose, vorhandene Control-Plane-Laufzeit und den privaten Verwaltungsplan ohne Hoständerungen.

## Stabile Control Plane starten

```bash
sudo ./install.sh
```

Das Bootstrap kann unter anderem den dauerhaften Zustand vorbereiten, ein selbstsigniertes Control-Plane-Zertifikat und den Setup-Code erstellen, den privaten Verwaltungsweg auswählen, `mem-control-plane` starten und anschließend Gesundheit sowie die exakte Docker-Hostbindung verifizieren.

## Privaten Verwaltungsweg wählen

Standard ist SSH/local-only:

```text
127.0.0.1:8443 -> 8443/tcp
```

Interaktiv kann MEM geeignete RFC1918-Adressen als ausdrücklich gewähltes Trusted LAN anbieten. Es bindet nur an die ausgewählte Adresse.

Nicht-interaktive Beispiele:

```bash
sudo ./install.sh --control-plane-access ssh

sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

Wildcard-Adressen, öffentliche IPv4-Adressen, Link-Local-Adressen und beliebige nicht zugewiesene private Adressen werden nicht akzeptiert.

## Vorhandene Control Plane

MEM liest die tatsächliche Docker-`HostIp` einer vorhandenen Control Plane.

- Sichere Loopback- und zugewiesene Trusted-LAN-Bindungen werden ohne ausdrückliche Änderung beibehalten.
- Unsichere Wildcard-, öffentliche, veraltete private oder mehrdeutige Bindungen erfordern eine geprüfte Container-Neuerstellung.
- `/data`, Zertifikat, Setup-Autorität und Host-Daten bleiben erhalten.
- Gesundheit und exakte Docker-Hostbindung werden geprüft, bevor der alte Container-Eintrag entfernt wird.

Scheitert die Härtung, darf eine vorher sichere private Laufzeit wieder gestartet werden. Eine vorher unsicher veröffentlichte Laufzeit bleibt für Recovery erhalten, wird aber nicht automatisch wieder öffentlich gestartet.

## Nützliche Optionen

```bash
sudo ./install.sh --dry-run
sudo ./install.sh --yes
sudo ./install.sh --control-plane-access ssh
sudo ./install.sh --control-plane-access trusted-lan --control-plane-bind-address 192.168.10.20
sudo ./install.sh --control-plane-port 9443
sudo ./install.sh --show-setup-token
```

Ein realer Bootstrap-Lauf speichert begrenzte root-only Nachweise unter `/var/log/mem/bootstrap/`. Setup-Code und typische Zugangsdaten werden redigiert; private Schlüssel werden nicht als Nachweis ausgegeben.

## Nicht Teil des unterstützten 0.2.0-Wegs

Das Bootstrap veröffentlicht die Control Plane nicht direkt im Internet, erstellt keine öffentliche NPM-Route für MEM-Administration und benötigt keinen öffentlichen MEM-Admin-DNS-Namen.

---

# Source Assistant installieren und privat öffnen

Source: `docs/de/migrieren/install-source-assistant.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

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

---

# Passt MEM zu Ihnen?

Source: `docs/de/start/is-mem-right-for-you.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# Passt MEM zu Ihnen?

MEM richtet sich an technisch versierte Personen, die einen Linux-Server betreiben können, aber nicht die vollständige Matrix-Plattform selbst zusammensetzen und dauerhaft im Kopf behalten möchten.

## MEM passt wahrscheinlich, wenn

Sie mit den meisten dieser Aufgaben vertraut sind:

- einen Ubuntu- oder vergleichbaren Linux-Server pflegen;
- mit Docker-Containern, Netzwerken, eingebundenen Datenpfaden und Logs arbeiten;
- DNS für eine Domain verwalten;
- erforderliche Netzwerkpfade absichern;
- Speicherplatz überwachen und Sicherheitsupdates des Hosts einspielen;
- Backup-Kopien außerhalb des Hauptservers aufbewahren.

Typische Einsatzbereiche sind Familienkommunikation, Gaming-Communities, Vereine, kleine Organisationen oder ein Betreiber mit mehreren unabhängigen Matrix-Stacks.

## MEM ist kein wartungsfreier Dienst

MEM reduziert wiederkehrende Integrationsarbeit. Der Betreiber bleibt jedoch zuständig für Linux-Host, DNS, Verbindung, Speicherkapazität, externe Backup-Kopien, Betriebssystemsicherheit, Operator-Konten, Upstream-Updates und die Aufklärung der Benutzer über Recovery Keys.

Eine Oberfläche kann keinen ungepflegten Host und kein Backup ersetzen, das nur auf dem ausgefallenen Datenträger liegt.

## MEM und ESS Community

ESS Community ist Elements offizielle Open-Source-Matrix-Distribution und verwendet Kubernetes und Helm. Sie kann die bessere Wahl sein, wenn Sie das offizielle Element-Bereitstellungsmodell oder einen direkten Weg in die weitere ESS-Familie wünschen.

MEM bedient eine andere Präferenz: eine Docker-first Control Plane auf Host-Ebene mit ausdrücklichen Abläufen für Backup, Restore, Migration, Diagnose und mehrere Stacks.

Entscheidend ist nicht nur, welches System am schnellsten installiert ist. Entscheidend ist auch, welches Betriebsmodell Sie verstehen und wiederherstellen möchten, wenn etwas ausfällt.

## Wann ein anderer Ansatz sinnvoll sein kann

MEM 0.2.0 ist möglicherweise nicht die richtige Wahl, wenn Sie Hochverfügbarkeit über mehrere Knoten, automatische Cluster-Planung, kommerziellen Herstellersupport, eine Kubernetes-native Bereitstellung, einen vollständig verwalteten Dienst oder eine andere Messaging-Plattform als Matrix benötigen.

---

# Run the bootstrap installer

Source: `docs/installation/bootstrap-installer.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Run the bootstrap installer

The host bootstrap prepares the machine and starts the private MEM Control Plane. The browser workflow performs the managed-platform installation afterward.

## Run a dry check first

```bash
sudo ./install.sh --dry-run
```

The dry run checks the operating system, architecture, CPU, memory, disk, packages, Docker, Compose, current Control Plane state, and private administration plan without modifying the host.

## Start the stable Control Plane

```bash
sudo ./install.sh
```

The normal bootstrap can:

1. validate Ubuntu and architecture;
2. check resources and basic network access;
3. install missing prerequisites and Docker when required;
4. create or preserve Control Plane persistent state;
5. create the long-lived self-signed Control Plane certificate;
6. generate and persist a high-entropy `mem_...` setup code;
7. select the private Control Plane administration boundary;
8. start `mem-control-plane` with the Docker socket and persistent `/data` state;
9. verify health and the exact Docker host binding;
10. print the safe browser/SSH handoff and certificate SHA-256 fingerprint.

## Private administration choice

The default is SSH/local-only:

```text
127.0.0.1:8443 -> 8443/tcp
```

When interactive bootstrap detects eligible RFC1918 addresses, it can also offer an explicit Trusted-LAN choice. MEM binds only to the selected address, never to every interface.

Examples for non-interactive/release use:

```bash
sudo ./install.sh --control-plane-access ssh

sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

`0.0.0.0`, `[::]`, public IPv4 addresses, link-local addresses, and arbitrary unassigned private addresses are not supported Control Plane bindings.

## Existing Control Plane behaviour

Bootstrap inspects the actual Docker `HostIp` for an existing Control Plane.

- a safe loopback binding is preserved;
- a safe assigned Trusted-LAN binding is preserved unless the operator explicitly changes it;
- wildcard, public, stale-private, ambiguous, or otherwise unsupported bindings require reviewed container recreation on a supported private address;
- persistent `/data`, certificate material, setup authority, and host-data state are retained;
- replacement health and the exact Docker host binding are verified before the previous container record is retired.

If a hardening migration fails, a previously safe private runtime may be restarted. An unsafe previous wildcard/public runtime is retained for recovery but is left stopped rather than being automatically re-exposed.

## Useful options

```bash
sudo ./install.sh --dry-run
sudo ./install.sh --yes
sudo ./install.sh --control-plane-access ssh
sudo ./install.sh --control-plane-access trusted-lan --control-plane-bind-address 192.168.10.20
sudo ./install.sh --control-plane-port 9443
sudo ./install.sh --show-setup-token
```

Development/release options also include `--channel`, `--control-plane-image`, CLI payload options, `--skip-docker-install`, `--allow-low-disk`, and the Docker convenience-script override.

Do not use development overrides for a normal production installation.

## Bootstrap transcript and failure evidence

Real bootstrap runs keep bounded root-only evidence under `/var/log/mem/bootstrap/`. Setup codes and common credential/token patterns are redacted. The complete container environment and private key are not captured.

A `--dry-run` deliberately creates no persistent bootstrap transcript.

## What MEM does not do

The supported 0.2.0 bootstrap does not:

- publish the Control Plane on a publicly routable interface;
- create a public NPM proxy host for MEM administration;
- require public MEM admin DNS;
- restore the old 0.1.0 `.env` / `stack.sh` / separate `mem-api` + `mem-web` deployment model.

---

# Install and open the Source Assistant privately

Source: `docs/migrate/install-source-assistant.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Install and open the Source Assistant privately

The MEM Migrate bootstrap installs a verified release on the legacy source host without requiring Git, .NET, Node.js, npm, or a source checkout.

## Outcome

MEM Migrate is installed beneath `/opt/mem/migrate`, its durable working state remains beneath `/var/lib/mem-migrate`, and the Source Assistant is reachable only through a private local connection.

## Supported bootstrap host

The current bootstrap supports Ubuntu Server 24.04 on amd64/x86_64. It requires an existing reachable Docker daemon. It validates Docker but does not install Docker, change its repositories, enable services, restart containers, or mutate the legacy stack.

It preserves a working `age` command or installs the supported Ubuntu package when required.

## Run the hosted bootstrap

Use the MEM Migrate release host supplied with the MEM 0.2.0 release artifacts. Do not invent or substitute an untrusted download location.

First run the non-modifying plan:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host> \
      --dry-run
```

Review the reported operating system, architecture, Docker reachability, `age` state, requested release, and installation plan. Then apply the same release:

```bash
curl -fsSL https://<release-host>/install-mem-migrate.sh \
  | sudo bash -s -- \
      --release-base-url https://<release-host>
```

The installer verifies the release manifest, outer and inner checksums, archive root, entry types, and payload before atomically changing the current release link. Existing state under `/var/lib/mem-migrate` is preserved.

## Start the Source Assistant

Run it in a terminal on the source host:

```bash
sudo /opt/mem/migrate/current/mem-migrate-web
```

The safe defaults listen on `127.0.0.1:7391`. The terminal prints the startup access code. Keep that terminal available while using the assistant.

The current release does not promise stable `mem-migrate-start`, `mem-migrate-stop`, or `mem-migrate-status` host commands. Do not document or rely on those names until the installed release provides them. Stop the foreground process with `Ctrl+C` when you have finished the source-side work.

## Open it through an SSH tunnel

From your operator workstation:

```bash
ssh -N \
  -L 7391:127.0.0.1:7391 \
  <operator>@<source-host>
```

Then open:

```text
http://localhost:7391
```

Enter the access code printed by the Source Assistant process.

> [!WARNING]
> Public internet exposure is unsupported. Do not bind the Source Assistant to a wildcard address or create a public reverse-proxy route for it.

## Verify success

- The browser shows the Source Assistant access page.
- The access code opens the workspace.
- The first normal action is source assessment.
- Refreshing the browser does not erase durable assessment, capture, package, or journal state.

If the browser cannot connect, verify that the source process is still running, the SSH session remains open, and local port `7391` is not already in use.

Next: [Assess the source and select one stack](assess-and-select.md).

---

# Network behavior

Source: `docs/operations/network-behavior.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Network behavior

MEM 0.2.x deliberately separates **private administration** from **public chat traffic**.

## Control Plane

Supported administration shapes are:

```text
SSH / local-only
    127.0.0.1:<control-plane-port>

Trusted LAN
    one selected RFC1918 host address:<control-plane-port>
```

The Control Plane is not a public NPM route and must not be published through Internet-facing NAT or public DNS.

## Public Matrix and Element

Nginx Proxy Manager handles public HTTP/HTTPS routes for managed stacks. Public Matrix and Element HTTPS uses TCP **443**. TCP **80** is optional when the operator deliberately enables an HTTP/redirect path; the current guided deSEC DNS-01 certificate path does not require public port 80 for certificate issuance.

Inside Docker, NPM participates in `mem-gateway` and uses the alias `npm`. The Control Plane must share that managed network when it performs NPM administration.

## TURN

The production coturn service publishes:

```text
3478/tcp
3478/udp
49160-49200/udp
```

If the host is behind NAT, firewall/router policy must make the required public TURN paths reachable. DNS does not create those rules.

## Split DNS

For on-premises installations, internal and external resolvers may intentionally answer the same service name differently:

```text
public:  turn.example.org -> WAN address
inside:  turn.example.org -> private MEM address
```

This is useful for direct LAN service access and for avoiding accidental dependence on NAT hairpinning during local functional checks.

## Browser boundary

A Docker hostname such as `npm`, `mem-npm`, or a stack container name is an internal server authority. The browser should receive only a supported private Control Plane URL or public Matrix/Element URL.

## Troubleshooting

Use server-authored Diagnostics and support reports first. If stack route publication reports `Name or service not known (npm:81)`, inspect the current Control Plane runtime/recreation contract rather than creating a second stack identity to hide the failure.

---

# MatrixEasyMode v0.1.0

Source: `docs/releases/0.1.0.md`
Locale: en
Section: Releases
Status: legacy
Applies to: 0.1.0

# MatrixEasyMode v0.1.0

> [!IMPORTANT]
> **First public release**
>
> MatrixEasyMode v0.1.0 is the first public release of the project.
>
> This release establishes the initial deployment model, runtime structure, installation flow, and documentation foundation for MatrixEasyMode.

**Topics:** v0.1.0 · Initial Release · Self Hosted · Docker Compose

## Release overview

v0.1.0 is the first public release of MatrixEasyMode.

This release establishes the initial platform foundation for deploying and operating Matrix + Element on infrastructure you control using Docker Compose, PostgreSQL, and Nginx Proxy Manager.

The goal of this release is not to present a finished platform with every feature already implemented.

The goal is to establish:

- the deployment model
- the runtime structure
- the operational workflow
- the ingress model
- the documentation posture
- the foundation future releases will build on

## What this release includes

The v0.1.0 runtime model includes:

- PostgreSQL
- Nginx Proxy Manager
- MatrixEasyMode API
- MatrixEasyMode web frontend

This release also introduces:

- staged startup flow
- installer-driven `.env` generation
- Docker Compose deployment helpers
- NPM-backed ingress bootstrap
- public hostname configuration
- wildcard certificate integration
- local and registry image modes
- initial operations and troubleshooting documentation

## Deployment model

v0.1.0 establishes the current MatrixEasyMode deployment flow:

1. start infrastructure
2. configure ingress and certificates
3. start the application layer
4. verify runtime state

Typical install flow:

```bash
./install.sh
./stack.sh up infra
./stack.sh up app
```

This deployment model intentionally keeps infrastructure visible rather than hiding it behind a fully managed abstraction layer.

## Runtime shape

The standard runtime in this release consists of:

### Infrastructure layer

- `postgres`
- `npm`

### Application layer

- `api`
- `web`

The staged startup order matters because the MatrixEasyMode API integrates with Nginx Proxy Manager and expects ingress prerequisites to already exist before application startup.

## Documentation introduced in this release

v0.1.0 also establishes the first public documentation set for MatrixEasyMode.

That includes:

- Get Started
- Installation Guide
- Advanced Install
- Configuration
- Architecture
- Network Behavior
- Operations
- Troubleshooting
- Release Notes

The documentation is intended to help users understand how MatrixEasyMode works, how the deployment behaves, and how the different runtime components fit together.

## Registry mode and local mode

This release supports two image modes:

### Registry mode

Uses published application images from a container registry.

This is the normal deployment path for most installations.

### Local mode

Uses locally built application images.

This is mainly intended for contributors or development-oriented workflows.

Both modes share the same overall runtime architecture.

## What users should expect from v0.1.0

This release should be viewed as an early platform foundation rather than a fully mature product.

Users should expect:

- a hands-on deployment flow
- visible infrastructure setup
- explicit ingress configuration
- real hostname and DNS requirements
- staged startup behavior
- Docker Compose-based operations
- log and status inspection during bring-up

The intended mindset for this release is:

review, deploy, verify.

## Current platform assumptions

The current deployment model assumes:

- Linux host environment
- Docker Engine and Docker Compose
- operator-controlled DNS
- HTTPS certificate management
- Nginx Proxy Manager as the ingress layer
- a self-hosted environment you control

These are part of the current product phase and deployment philosophy.

## Upgrade impact

For most users, v0.1.0 will be a fresh installation rather than an upgrade target.

Because this is the first public release:

- there is no earlier public release line
- this version establishes the baseline deployment posture
- future upgrades should be compared against this release

## Recommended first-run workflow

Recommended flow for first-time users:

1. read [Get Started](https://matrixeasymode.com/get-started)
2. review the [Installation Guide](../installation/index.md)
3. configure DNS and certificates
4. deploy infrastructure first
5. deploy the application layer second
6. verify logs, ingress, and public URLs
7. keep the operations and troubleshooting docs nearby during bring-up

## Foundation release

v0.1.0 is primarily a foundation release.

Its purpose is to establish the correct long-term shape of the project:

- self-hosted
- infrastructure-aware
- deployment-focused
- explicit runtime behavior
- documentation-first
- built on existing open-source foundations
- designed to evolve over time

## Related documentation

- [Get Started](https://matrixeasymode.com/get-started)
- [Installation Guide](../installation/index.md)
- [Advanced Install](../installation/advanced-install.md)
- [Configuration](../operations/configuration.md)
- [Operations Guide](../operations/operations-guide.md)
- [Troubleshooting](../operations/troubleshooting.md)
- [Upgrading](../operations/upgrading.md)
- [Architecture](../architecture/index.md)
- [Network Behavior](../operations/network-behavior.md)

---

# Is MEM right for you?

Source: `docs/start/is-mem-right-for-you.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# Is MEM right for you?

MEM is designed for technically capable people who are willing to operate a Linux server but do not want to assemble and remember the complete Matrix platform by hand.

## MEM is likely a good fit when

You are comfortable with most of the following:

- maintaining an Ubuntu or similar Linux server;
- working with Docker containers, networks, mounted data, and logs;
- controlling DNS for a domain;
- securing required network paths;
- monitoring storage and applying host security updates;
- keeping backup copies outside the main host.

Typical uses include private family communication, a gaming community, a local club, a small organisation, or one operator hosting several independent Matrix stacks.

## MEM is not a zero-administration service

MEM removes repeated integration work, but the operator still owns the Linux host, DNS, connectivity, disk capacity, off-host backups, operating-system security, operator-account security, upstream updates, and user education around encryption recovery keys.

A dashboard cannot compensate for an unmaintained host or a backup retained only on the failed disk.

## MEM and ESS Community

ESS Community is Element's official open-source Matrix distribution and uses Kubernetes and Helm. It may be the better choice when you want the official Element deployment model or a direct path into the wider ESS family.

MEM serves a different preference: a Docker-first, host-level control plane with explicit backup, restore, migration, diagnostics, and multi-stack workflows.

The deciding question is not only which system is quickest to install. It is which operating model you are prepared to understand and recover when something goes wrong.

## Consider another approach when

MEM 0.2.0 may not be the right choice when you require multi-node high availability, automatic cluster scheduling, enterprise vendor support, a Kubernetes-native deployment, a fully managed service, or a messaging platform other than Matrix.

---

# Use Portainer for advanced container diagnostics

Source: `docs/tools/optional-portainer.md`
Locale: en
Section: Tools
Status: supported
Applies to: 0.2.x

# Use Portainer for advanced container diagnostics

MEM explains a failure; Portainer provides advanced low-level container inspection. Portainer does not replace MEM incidents, support reports, or workflow guidance, and MEM does not become a general Docker administration console.

## Open Portainer from MEM

A Platform Owner can open `/diagnostics/portainer` when the server has an authoritative private Portainer URL and environment ID. MEM may provide:

- **Open Portainer**;
- **Open local environment**;
- **Open containers**.

The browser does not construct these links from `localhost`, published ports, container names, or browser origin.

Portainer owns its own authentication session. MEM does not proxy or store a Portainer password, session token, or API key in browser storage.

## First-time setup and the five-minute window

A fresh Portainer 2.39.5 installation requires a one-time setup token before the first administrator can be created. Portainer prints that token to the server logs. On the Docker host, retrieve the latest token line with:

```bash
docker logs portainer 2>&1 | grep 'setup_token=' | tail -n 1
```

Copy the value after `setup_token=` into Portainer's **Setup token** field. The token is one-time use. The username defaults to `admin` but can be changed, and Portainer requires a password of at least 12 characters.

The first administrator must still be created within five minutes. If the window expires, restart only the Portainer container, reopen the UI immediately, then run the token command again so you use the newest setup token:

```bash
docker restart portainer
```

MEM never reads or stores the Portainer setup token or administrator password. Once the first administrator is created, Portainer's setup wizard detects the local Docker environment. In host-native development, MEM may open the running local Portainer home before an exact environment ID is configured; exact container handoff remains unavailable until that server-owned environment information exists.

## Open the current incident container

When an incident has current MEM-owned Docker evidence, **Open this container in Portainer** calls a MEM redirect endpoint. The server resolves the logical resource and current container identity at request time.

This avoids stale browser links when a container is recreated. Unsupported, removed, or unresolved resources fall back to the configured containers list instead of a known-broken detail page.

The Seq workspace uses the same model for **Open Seq container in Portainer**.

## Ownership boundary

Only MEM-owned logical resources are eligible for contextual handoff. The browser cannot submit an arbitrary Docker container ID. An unmanaged or identity-mismatched resource remains read-only and is not presented as MEM-owned.

MEM exposes no container start, stop, restart, remove, exec, prune, network mutation, or volume deletion action through Diagnostics.

## Approved runtime policy

New MEM-managed installations use the exact approved Portainer CE image:

```text
portainer/portainer-ce:2.39.5
```

The installation workflow resolves the local immutable `sha256:` image identity and creates the container from that identity. Ordinary status, start, and stop operations do not pull an image.

The runtime uses:

- the persistent `portainer_data` volume at `/data`;
- the Docker socket at `/var/run/docker.sock`;
- private HTTPS UI port `9443`;
- no Edge-agent port `8000` unless a future explicit workflow adds it;
- no automatic public NPM route.

An existing unowned Portainer installation, including a working 2.39.1 instance, is observed but not automatically adopted, replaced, relabelled, restarted, or upgraded.

## Exact-link compatibility

Exact resource links are a convenience tested against the approved Portainer 2.39 route contract. The containers list is the stable fallback. After changing the approved Portainer version, re-run direct-link compatibility proof before enabling exact handoff for the new release.

Official references:

- [Portainer documentation](https://docs.portainer.io/)
- [Install Portainer CE with Docker on Linux](https://docs.portainer.io/start/install-ce/server/docker/linux)
- [Portainer initial setup](https://docs.portainer.io/start/install-ce/server/setup)

## Rollback

To roll back contextual handoff, hide MEM's Portainer links; Portainer remains independently accessible. To roll back the runtime provider, use the documented previous approved immutable image only after checking data compatibility. Preserve `portainer_data` and never downgrade blindly.

Fresh-server release validation should confirm immutable image identity, data persistence, port `9443`, absence of port `8000`, ownership labels, no public NPM route, and exact-link fallback behavior.

## Related documentation

- [Use the Diagnostics command centre](../operations/diagnostics-command-centre.md)
- [Use Seq with MEM](../operations/seq-with-mem.md)

---

# Erweiterte Bootstrap-Optionen

Source: `docs/de/installation/erweiterte-bootstrap-optionen.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

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

---

# Advanced bootstrap options

Source: `docs/installation/advanced-install.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Advanced bootstrap options

Use the standard stable bootstrap unless you are developing MEM, validating a release package, or deliberately changing the private Control Plane entry point.

## Change the private administration mode

SSH/local-only is the default and safest general mode:

```bash
sudo ./install.sh --control-plane-access ssh
```

Trusted LAN is explicit:

```bash
sudo ./install.sh \
  --control-plane-access trusted-lan \
  --control-plane-bind-address 192.168.10.20
```

The Trusted-LAN address must be a concrete RFC1918 IPv4 address assigned to an eligible host interface. MEM does not accept wildcard or publicly routable Control Plane bindings.

## Change the private Control Plane port

```bash
sudo ./install.sh --control-plane-port 9443
```

The selected host address and port map to HTTPS port 8443 inside `mem-control-plane`. Update the SSH tunnel or Trusted-LAN browser URL accordingly.

Changing the port does not permit public exposure.

## Change an existing private binding

Re-running bootstrap preserves an existing safe loopback or Trusted-LAN address unless an explicit administration-mode change is requested.

A requested change is performed as reviewed container recreation with retained persistent state and post-start host-binding verification. MEM does not mutate a live container's published port in place.

## Stable and dev channels

```bash
sudo ./install.sh --channel stable
sudo ./install.sh --channel dev
```

`stable` is for normal releases. `dev` is for controlled validation.

## Override the Control Plane image

```bash
sudo ./install.sh --control-plane-image mem-control-plane:local
```

Use this for development/release proof only and verify image provenance.

## Other development/release options

Supported advanced flags include CLI payload overrides, `--skip-docker-install`, `--allow-low-disk`, `--use-docker-convenience-script`, `--dry-run`, and `--yes`.

`--yes` accepts reviewed mutation prompts; it does not bypass private-address validation.

## Unsupported advanced paths

MEM 0.2.0 does not support:

- `0.0.0.0:<port>` or `[::]:<port>` Control Plane publication;
- direct Control Plane binding to a public/non-private IP;
- a public MEM administration hostname or NPM proxy route;
- legacy `.env` / `stack.sh` application deployment;
- separate manual `mem-api` and `mem-web` starts;
- bypassing the durable installation plan or exposure validation.

Source modifications can create a custom deployment, but that custom environment is outside the supported MEM 0.2.0 private-administration contract.

---

# Export a portable backup

Source: `docs/backups-and-restores/portable-export.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Export a portable backup

A local backup on the MEM host protects against an application mistake. It does not protect against host loss, disk failure, theft, or destructive administrator action. Export important catalog entries and copy them to independent storage.

## Create the export

1. Open **Backups**.
2. Select an entry whose payload is available.
3. In **Portable export**, create and download the ZIP.
4. Record the catalog entry ID, export filename, size, and download time.
5. Copy the ZIP to protected off-host storage.

MEM generates the portable archive from the catalog-managed payload. For an imported backup, the new export does not depend on retaining the originally uploaded ZIP.

## Portable archive contents

The export manifest describes:

- source stack and Matrix server identity;
- PostgreSQL dump presence;
- Matrix configuration, signing key, and media;
- Element configuration;
- Matrix and Element route hosts;
- TURN state and restore intent;
- restore-policy requirements;
- included files and warnings;
- the checksums file used during later import validation.

The browser response does not expose host filesystem paths.

## Security handling

Treat the ZIP as highly sensitive. It may contain:

- private room and account data from PostgreSQL;
- media uploaded by users;
- Matrix configuration;
- the Matrix signing key;
- service topology and domain information.

Encrypt storage where practical, restrict access, and avoid placing the archive in ordinary shared folders or public issue trackers.

## Verify the off-host copy

Confirm that the copied file size matches the downloaded file and that it can be read from the destination. The strongest operational proof is to import it on a non-production MEM environment and run a [private test](private-test.md).

A portable ZIP is a MEM recovery artifact, not a universal Synapse migration format. Compatibility still depends on the manifest, payload, supported database/runtime policy, and the target MEM release.

## Retention interaction

Deleting the catalog entry can also delete server-side portable exports associated with that entry. The copy you downloaded is outside MEM's control. Maintain an independent retention policy and periodically prove that off-host archives are readable.

---

# Manage Matrix users

Source: `docs/chat-servers/users.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Manage Matrix users

## Outcome

Synchronize the stack's local Synapse account inventory, create the first Matrix administrator, and add later Matrix users.

Open the stack workspace and select **Users**.

## Matrix users are separate from operators

MEM Control Plane users administer the platform. Matrix users belong to one homeserver and sign in through Element or another Matrix client.

A Platform Owner account does not automatically create a Matrix account, and a Matrix administrator is not automatically a MEM operator.

## Synchronize before changing accounts

MEM reads the local account inventory from the stack's Synapse database. This inventory is the authority used to decide whether an active administrator exists and whether user creation is safe.

User creation remains disabled while inventory is unavailable, unsynchronized, synchronizing, or failed. Select **Synchronize users** and wait for a successful result before treating an empty table as an empty homeserver.

The table can include:

- accounts created through MEM;
- accounts discovered directly from Synapse;
- active, deactivated, pending, failed, or missing projections;
- administrator and first-administrator markers.

## Create the first Matrix administrator

When Synapse reports no active local administrator, MEM presents **Create the first Matrix admin**. The first account is forced to administrator status and is created through the stack's local shared-secret registration path.

Choose a durable administrator username and a strong password. Record the password in an appropriate password manager, then sign in to Element and establish the user's Matrix encryption recovery material.

## Create later users

After inventory confirms at least one active administrator, use **Create Matrix user**.

The form accepts:

- username;
- password of at least eight characters;
- optional display name;
- optional email;
- optional Matrix administrator role.

Usernames are normalized to lowercase and retain letters, numbers, `.`, `_`, `-`, and `=`. Review the normalized username before submission. The resulting Matrix ID uses the stack's Matrix server name, for example `@alice:matrix-family.example.org`.

## Verify success

After creation:

1. confirm the account appears as Active or Synced;
2. confirm the Matrix user ID and role;
3. sign in through the stack's Element URL;
4. for encrypted use, configure recovery and verify another device before relying on password-reset recovery.

A successful account creation followed by an inventory refresh warning can leave the Matrix account created while the MEM projection needs resynchronization. Synchronize again before creating a duplicate.

---

# Portable Sicherung exportieren

Source: `docs/de/backups-und-wiederherstellen/portabler-export.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Portable Sicherung exportieren

Eine lokale Sicherung auf dem MEM-Host schützt vor Bedienfehlern, aber nicht vor Hostverlust, Datenträgerausfall, Diebstahl oder destruktiven Administratoraktionen. Exportieren Sie wichtige Katalogeinträge auf unabhängigen Speicher.

## Export erstellen

1. **Sicherungen** öffnen.
2. Einen Eintrag mit verfügbarem Payload auswählen.
3. Unter **Portabler Export** das ZIP erzeugen und herunterladen.
4. Katalog-ID, Dateiname, Größe und Zeitpunkt notieren.
5. ZIP auf geschützten externen Speicher kopieren.

MEM erzeugt das Archiv aus dem Katalog-Payload. Bei einer importierten Sicherung ist der neue Export nicht vom ursprünglichen Upload-ZIP abhängig.

## Inhalt

Das Exportmanifest beschreibt:

- Quell-Stack und Matrix-Serveridentität;
- PostgreSQL-Dump;
- Matrix-Konfiguration, Signaturschlüssel und Medien;
- Element-Konfiguration;
- Matrix- und Element-Routen;
- TURN-Zustand und Wiederherstellungsabsicht;
- Wiederherstellungsanforderungen;
- enthaltene Dateien, Warnungen und Prüfsummendatei.

Die Browserantwort enthält keine Host-Dateisystempfade.

## Sicherer Umgang

Das ZIP kann private Raum- und Kontodaten, Benutzermedien, Matrix-Konfiguration, Signaturschlüssel sowie Domain- und Topologieinformationen enthalten. Schützen Sie den Speicher, beschränken Sie Zugriff und laden Sie das Archiv nicht in normale Support-Tickets oder öffentliche Freigaben.

## Externe Kopie prüfen

Dateigröße nach dem Kopieren vergleichen und prüfen, ob die Datei vom Ziel gelesen werden kann. Der stärkste Nachweis ist ein Import auf einer nicht produktiven MEM-Umgebung mit anschließendem [privaten Test](privater-test.md).

Ein portables ZIP ist ein MEM-Wiederherstellungsartefakt und kein universelles Synapse-Migrationsformat. Kompatibilität hängt weiterhin von Manifest, Payload, Datenbank- und Laufzeitrichtlinie sowie Zielversion ab.

## Aufbewahrung

Die Löschung des Katalogeintrags kann auch serverseitige portable Exporte dieses Eintrags entfernen. Die heruntergeladene externe Kopie liegt außerhalb von MEM. Pflegen Sie eine eigene Aufbewahrungsrichtlinie und prüfen Sie Archive regelmäßig.

---

# Matrix-Benutzer verwalten

Source: `docs/de/chat-servers/benutzer.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Matrix-Benutzer verwalten

## Ergebnis

Synchronisieren Sie das lokale Synapse-Kontoinventar, erstellen Sie den ersten Matrix-Administrator und fügen Sie weitere Matrix-Benutzer hinzu.

Öffnen Sie im Stack-Arbeitsbereich **Benutzer**.

## Matrix-Benutzer und Operatoren sind getrennt

MEM-Control-Plane-Benutzer verwalten die Plattform. Matrix-Benutzer gehören zu einem Homeserver und melden sich über Element oder einen anderen Matrix-Client an.

Ein Platform-Owner-Konto erstellt nicht automatisch ein Matrix-Konto. Ein Matrix-Administrator ist nicht automatisch MEM-Operator.

## Vor Änderungen synchronisieren

MEM liest das lokale Kontoinventar aus der Synapse-Datenbank des Stacks. Dieses Inventar entscheidet verbindlich, ob ein aktiver Administrator vorhanden und Benutzererstellung sicher ist.

Benutzererstellung bleibt deaktiviert, solange das Inventar nicht verfügbar, nicht synchronisiert, in Bearbeitung oder fehlgeschlagen ist. Wählen Sie **Benutzer synchronisieren** und warten Sie auf Erfolg, bevor Sie eine leere Tabelle als leeren Homeserver interpretieren.

Die Tabelle kann enthalten:

- über MEM erstellte Konten;
- direkt aus Synapse gefundene Konten;
- aktive, deaktivierte, ausstehende, fehlgeschlagene oder fehlende Projektionen;
- Administrator- und Erster-Administrator-Markierungen.

## Ersten Matrix-Administrator erstellen

Meldet Synapse keinen aktiven lokalen Administrator, zeigt MEM **Ersten Matrix-Administrator erstellen**. Das erste Konto wird zwingend Administrator und über den lokalen Shared-Secret-Registrierungsweg des Stacks erstellt.

Wählen Sie einen dauerhaften Benutzernamen und ein starkes Passwort. Speichern Sie das Passwort sicher, melden Sie sich anschließend in Element an und richten Sie das Matrix-Wiederherstellungsmaterial für Verschlüsselung ein.

## Weitere Benutzer erstellen

Nachdem das Inventar mindestens einen aktiven Administrator bestätigt, verwenden Sie **Matrix-Benutzer erstellen**.

Das Formular akzeptiert:

- Benutzername;
- Passwort mit mindestens acht Zeichen;
- optionaler Anzeigename;
- optionale E-Mail-Adresse;
- optionale Matrix-Administratorrolle.

Benutzernamen werden in Kleinbuchstaben umgewandelt und behalten Buchstaben, Zahlen, `.`, `_`, `-` und `=`. Prüfen Sie den normalisierten Namen. Die Matrix-ID verwendet den Servernamen des Stacks, zum Beispiel `@alice:matrix-familie.example.org`.

## Erfolg prüfen

Nach der Erstellung:

1. prüfen Sie den Status Aktiv oder Synchronisiert;
2. prüfen Sie Matrix-Benutzer-ID und Rolle;
3. melden Sie sich über die Element-URL des Stacks an;
4. richten Sie für verschlüsselte Nutzung Wiederherstellung und ein verifiziertes weiteres Gerät ein.

Ein erfolgreich erstelltes Konto kann trotz anschließender Inventarwarnung bereits in Matrix existieren. Synchronisieren Sie erneut, bevor Sie ein Duplikat anlegen.

---

# Private Control Plane öffnen und Setup-Code verwenden

Source: `docs/de/installation/privater-zugriff-und-setup-code.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Private Control Plane öffnen und Setup-Code verwenden

Nach dem Start von `mem-control-plane` zeigt das Bootstrap den unterstützten privaten Verwaltungsweg, die Browser-Adresse, bei Bedarf den SSH-Befehl sowie den SHA-256-Fingerprint des Control-Plane-Zertifikats an.

## SSH-Tunnel / local-only — Standard

Für einen Internet-/VPS-Host bindet MEM die Control Plane ausschließlich an Host-Loopback.

```bash
ssh -N -o ExitOnForwardFailure=yes \
  -L 127.0.0.1:8443:127.0.0.1:8443 \
  <operator>@<server>
```

Danach öffnen Sie:

```text
https://127.0.0.1:8443
```

Auch die lokale Seite des SSH-Forwards ist an Loopback gebunden. In diesem Modus gibt es keine unterstützte direkte öffentliche Control-Plane-URL.

## Trusted LAN — ausdrückliche Auswahl

Auf einem On-Premises-Host, einer Proxmox-VM, im Home-Lab oder Management-VLAN kann MEM an genau eine ausgewählte RFC1918-Adresse binden, zum Beispiel:

```text
https://192.168.10.20:8443
```

Verwenden Sie dafür möglichst eine statische Adresse oder DHCP-Reservierung. Veröffentlichen Sie diesen Port nicht über Internet-NAT, öffentliche DNS-Namen oder Nginx Proxy Manager.

SSH bleibt als Break-Glass-/privater Tunnelweg verfügbar.

## Erwartete Browserwarnung

MEM verwendet für die private Control Plane ein lokal erzeugtes selbstsigniertes Zertifikat. Eine Browserwarnung ist normal.

Vergleichen Sie vor dem Akzeptieren den vom Bootstrap angezeigten SHA-256-Fingerprint.

Dieses Zertifikat ist unabhängig vom öffentlichen Wildcard-Zertifikat für Matrix und Element.

## MEM-Anmeldung bleibt erforderlich

Der private Netzwerkzugang ersetzt nicht die MEM-Authentifizierung. Benannte Anmeldung, TOTP-MFA, Rollen, Sitzungsregeln und Step-up für Hochrisikoaktionen gelten weiterhin.

## Setup-Code verwenden

Geben Sie den starken `mem_...`-Code beim First-Owner-Bootstrap ein und behandeln Sie ihn wie ein Passwort.

Er kann nur einen kurzlebigen Bootstrap-Grant erzeugen, solange noch kein abgeschlossener Platform Owner existiert. Erstellen Sie danach sofort den Platform Owner, richten Sie TOTP ein und speichern Sie Recovery-Codes sicher.

## Aktuelle Exposition prüfen

Home → Host status, Diagnostics → Control Plane runtime und System Information zeigen die tatsächlich beobachtete Docker-Hostbindung.

Wildcard- oder öffentliche Drift wird nicht still akzeptiert, sondern als Diagnostics-Fehlerincident gemeldet.

---

# Quelle bewerten und einen Stack auswählen

Source: `docs/de/migrieren/assess-and-select.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Quelle bewerten und einen Stack auswählen

Der Source Assistant muss die alte Installation verstehen, bevor Daten erfasst werden dürfen.

## Ergebnis

Die Quellbewertung bestätigt ein unterstütztes MEM-0.1.0-Profil, Blocker sind geklärt und genau ein Quell-Stack ist für diese Migration ausgewählt.

## Quellbewertung ausführen

Starten Sie im Source Assistant die Bewertung. Sie prüft die alte Anwendungsdatenbank, die Docker-Laufzeit, Matrix-Stack-Dateien und weitere erforderliche Quellnachweise.

Die Bewertung ist schreibgeschützt. Sie stoppt keine Container, veröffentlicht keine Routen, verändert die alte Datenbank nicht und schreibt die Quellkonfiguration nicht um.

Prüfen Sie Klassifizierung und Empfehlung. Ein bestätigtes unterstütztes MEM-0.1.0-Ergebnis kann fortfahren. Ein wahrscheinliches, reparierbares, blockiertes oder nicht unterstütztes Ergebnis erfordert die Prüfung der Details und die Behebung des genannten Zustands vor der Erfassung.

Behandeln Sie eine Warnung nicht als Erlaubnis, einen Blocker zu umgehen. Bewahren Sie Bewertungs-ID und Quellfingerabdruck auf.

## Beabsichtigten Stack auswählen

Werden mehrere Stacks gefunden, wählen Sie beim beabsichtigten Stack **Select stack**. Prüfen Sie:

- Stack-Slug;
- Matrix-Hostname;
- Element-Hostname;
- Container- und Datenidentität;
- Quellfingerabdruck;
- erwarteten Benutzer-, Raum- und Medienumfang, soweit angezeigt.

Die Auswahl ist maßgeblich. Erfassung und Paketerstellung sind an diesen Stack gebunden; die Ziel-Control-Plane darf bei der Aufnahme keinen anderen auswählen.

## Sicherheit und Auswirkung

Die Auswahl verändert den Stack nicht. Sie definiert die Grenze der späteren Erfassung. Das Paket enthält nur die erforderlichen Matrix-Daten, Konfiguration, Signaturidentität, Medien, Element-Konfiguration und minimale Herkunft des ausgewählten Stacks.

Wählen Sie keinen Test-Stack, nur um fortzufahren. Ein Paket mit falscher Matrix-Identität muss verworfen und nach korrekter Auswahl neu erstellt werden.

## Erfolg prüfen

Sie sehen einen unterstützten Bewertungszustand, genau einen ausgewählten Stack, keinen ungelösten Erfassungsblocker und einen nach Browser-Refresh stabilen Zustand.

Ändert sich die Quelle nach der Bewertung wesentlich, führen Sie vor der Erfassung eine neue Bewertung aus.

Weiter: [Zielanfrage importieren, erfassen und Paket erstellen](create-package.md).

---

# MEM-Produktpakete

Source: `docs/de/start/packages.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# MEM-Produktpakete

MEM 0.2.0 wird als kleine Gruppe zusammenarbeitender Produkte ausgeliefert und nicht als ein universelles Programm.

## MEM Control Plane

Die Control Plane ist das maßgebliche serverseitige Paket auf dem Zielhost. Sie enthält Web-Oberfläche, ASP.NET-Core-API, Operatoridentität, Einrichtungs- und Betriebsworkflows, privilegierten HostAgent-Zugriff, SQLite-Zustand, Diagnose und den lokalen Dokumentationsleser.

## MEM CLI

Der installierte Operatorbefehl lautet:

```bash
mem
```

MEM CLI ist ein Remote-Client für die Control Plane. Docker- oder Wiederherstellungslogik wird nicht lokal neu implementiert. Die CLI unterstützt Profile, eine im Browser genehmigte Geräteanmeldung, englische oder deutsche Ausgabe und stabile englische JSON-Felder.

Normale CLI-Credentials werden über den Secret Service des Betriebssystems gespeichert. Lokale Ausführung oder SSH umgehen keine serverseitigen Rollen, Audit- oder Step-up-Regeln.

## MEM Migrate CLI

Der Migrationsbefehl lautet:

```bash
mem-migrate
```

MEM Migrate ist ein separates, versionsspezifisches Produkt mit Wissen über die unterstützte MEM-0.1.0-Quellstruktur. Es bewertet und erfasst die alte Quelle, prüft Artefakte, erstellt Pakete und führt Worker-seitige Konvertierungsaufgaben aus.

Auf dem Ziel startet die Control Plane die installierte Datei als Worker und validiert strukturierte Ereignisse, Berichte, Pfade und Ausgabe-Hashes. Legacy-Migrationscode wird nicht in die normale Control-Plane-Laufzeit eingebunden.

## MEM Migrate Source Assistant

Der Source Assistant ist eine temporäre lokale ASP.NET-Core- und React-Anwendung auf dem alten Server. Er führt durch Anmeldung mit Zugriffscode, Quellenbewertung, Stack-Auswahl, Import der Zielanfrage, Erfassung, Erstellung des verschlüsselten Pakets, Download und lokalen Lebenszyklus.

Der sichere Standard ist Loopback-Zugriff. Der Source Assistant ist nicht die permanente Verwaltungsoberfläche des Zielservers.

## Welches Paket benötige ich?

| Ziel | Paket |
|---|---|
| MEM 0.2.0 installieren und betreiben | MEM Control Plane |
| Die Control Plane skripten oder untersuchen | MEM CLI |
| Von unterstütztem MEM 0.1.0 migrieren | MEM Migrate |
| Einen geführten Browser auf dem alten Server verwenden | MEM Migrate Source Assistant |

---

# Open the private Control Plane and use the setup code

Source: `docs/installation/private-access-and-setup-code.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Open the private Control Plane and use the setup code

After bootstrap starts `mem-control-plane`, it prints the supported private administration path, browser URL, SSH command when relevant, setup code guidance, and the SHA-256 fingerprint of the Control Plane certificate.

## SSH tunnel / local-only — default

For an Internet/VPS host, MEM binds the Control Plane only to host loopback.

Run the command printed by bootstrap from your workstation. Its shape is:

```bash
ssh -N -o ExitOnForwardFailure=yes \
  -L 127.0.0.1:8443:127.0.0.1:8443 \
  <operator>@<server>
```

Then open:

```text
https://127.0.0.1:8443
```

The workstation side of the forward is loopback-bound too, so the SSH tunnel does not accidentally publish MEM to the workstation LAN.

There is no supported direct public Control Plane URL in this mode.

## Trusted LAN — explicit opt-in

On an on-premises host, Proxmox VM, home lab, or management VLAN, bootstrap can bind MEM to one explicitly selected RFC1918 address assigned to the host.

Example:

```text
https://192.168.10.20:8443
```

Use Trusted LAN only when devices on that private network are trusted for administration. Prefer a stable address or DHCP reservation.

Do not forward the Control Plane port through Internet-facing NAT, public DNS, or Nginx Proxy Manager.

SSH remains available as a break-glass/private tunnel path.

## Expected browser certificate warning

MEM uses a locally generated self-signed certificate for the private Control Plane. A browser trust warning is expected.

Bootstrap prints the certificate SHA-256 fingerprint. Compare that fingerprint before accepting the browser exception.

This certificate protects the private Control Plane connection. It is separate from the public wildcard certificate used by Matrix and Element.

## Application authentication still applies

Private network access does not replace MEM authentication. Named login, TOTP MFA, roles, session controls, and recent step-up for high-risk actions remain required according to policy.

The network boundary prevents unnecessary reachability; the application boundary still decides who may operate MEM.

## Use the setup code

Enter the high-entropy `mem_...` code on the first-owner bootstrap screen when requested. Treat it like a password and do not publish it in logs, screenshots, tickets, or chat.

To display the persisted code again from the host:

```bash
sudo ./install.sh --show-setup-token
```

The code can create only a short-lived bootstrap grant while no completed Platform Owner exists. Complete first-owner creation, TOTP enrolment, and recovery-code storage immediately.

After a completed Platform Owner exists, normal named-account login is required.

## Verify the current exposure

The running Control Plane reports its observed Docker host binding in Home → Host status, Diagnostics → Control Plane runtime, and System Information.

A healthy SSH-mode runtime should report approximately:

```text
Control Plane access
SSH tunnel · Loopback only · 127.0.0.1:8443
```

A healthy Trusted-LAN runtime reports the selected private address.

Wildcard/public drift is a Diagnostics error incident rather than a state MEM silently accepts.

---

# Assess the source and select one stack

Source: `docs/migrate/assess-and-select.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Assess the source and select one stack

The Source Assistant must understand the old installation before it is allowed to capture data.

## Outcome

The source assessment confirms a supported MEM 0.1.0 profile, any blockers are understood, and exactly one source stack is selected for this migration.

## Run the source assessment

In the Source Assistant, start the assessment. It inspects the legacy application database, Docker runtime, Matrix stack files, and other required source evidence.

Assessment is read-only. It does not stop containers, publish routes, change the old database, or rewrite the source configuration.

Review the classification and recommendation. A confirmed supported MEM 0.1.0 result can proceed. A probable, repairable, blocked, or unsupported result requires the operator to read the detailed findings and resolve the stated condition before capture.

Do not treat a warning as permission to bypass a blocker. Preserve the assessment ID and source fingerprint in your migration evidence.

## Select the intended stack

If multiple source stacks are discovered, use **Select stack** on the one you intend to migrate. Verify:

- stack slug;
- Matrix hostname;
- Element hostname;
- container and data identity;
- source fingerprint;
- expected users, rooms, and media scope where the assessment exposes it.

The selection is authoritative. Capture and package creation are bound to this stack, and the target Control Plane must not select a different one during intake.

## Safety and impact

Selecting a stack does not mutate it. It defines the boundary of the future capture. The package contains only the selected stack’s required Matrix data, configuration, signing identity, media, Element configuration, and minimal provenance.

Do not select a test stack merely to continue the workflow. A package for the wrong Matrix identity must be discarded and recreated from a new, correct selection.

## Verify success

You should see:

- a supported assessment state;
- one clearly selected source stack;
- no unresolved capture blocker;
- a stable assessment and selection after browser refresh.

If the source changes materially after assessment, run a fresh assessment before capture.

Next: [Import the target request, capture, and create the package](create-package.md).

---

# Use the Diagnostics command centre

Source: `docs/operations/diagnostics-command-centre.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Use the Diagnostics command centre

MEM Diagnostics is the interpreted operator layer for control-plane failures and operational evidence. Open **Diagnostics** from the operator navigation or use the alert bell in the header when an incident needs attention.

## Read the health strip correctly

The health strip reports independent states for:

- overall Diagnostics readiness;
- incidents that currently need attention;
- the persistent local CLEF recorder;
- the browser-safe event store;
- optional Seq delivery and runtime state.

A neutral Seq state does not make MEM unhealthy. A green overall state must not hide a disabled or unavailable recorder or event store.

## Understand the three evidence layers

### Incidents

Incidents are grouped warning, error, or critical events that require operator attention. They contain a safe summary, feature, timestamps, correlation references, and links back to the owning workflow when MEM can establish one.

### Safe events

The safe event store contains browser-readable, redacted operational events. Information events can prove normal activity without creating an incident. The store deliberately excludes unrestricted exception, secret, and host-path data.

### CLEF technical recorder

The local CLEF recorder is the broader structured black-box log. It remains useful when the browser-safe event store has no matching event or when the API is unavailable. Do not distribute a complete recorder file without reviewing it for sensitive operational metadata.

## Find and filter technical events

The summary separates **Attention** from **Technical activity**. A warning-level technical event can be useful evidence without creating an incident that requires operator action.

Select the Incidents, Warning, Error, Critical, Information, or Events counter to open the corresponding view. Event severity, feature, and search filters are stored in the URL, so the result can be refreshed or bookmarked.

The Technical events view shows the newest safe events first and does not apply a severity filter by default. Select **Load 50 more events** to retrieve the next bounded cursor page. MEM uses deliberate cursor loading rather than automatic infinite scroll so keyboard focus, browser memory, and evidence boundaries remain predictable.

When no incident requires attention but safe events exist, the incident empty state links directly to Technical events.

## Verify the Diagnostics pipeline

A Platform Owner can run **Verify diagnostics pipeline** from `/diagnostics` or Logging Health.

The verification:

1. checks that the local recorder is writable;
2. writes one harmless Information event;
3. reads that exact safe event back;
4. verifies the correlation round trip;
5. reports Seq separately as passed, failed, disabled, or not configured.

It does not create a warning or error incident and does not mutate Docker.

## Use the alert bell

The header bell uses server-provided incident state. It shows at most five recent warning, error, or critical incidents and links to exact incident records. It is not a notification inbox: there is no browser-local read state, dismissal history, assignment, or acknowledgement workflow.

If the attention endpoint is unavailable, the bell must not present the system as all clear.

## Build a support handoff

Open an incident and use **Copy support JSON** or **Download support report**. Docker evidence is bounded and optional. Review the report before sharing it because hostnames, stack names, timestamps, event codes, and topology may still be sensitive.

Never attach credentials, access tokens, recovery codes, signing keys, private keys, database dumps, unrestricted configuration files, or complete raw logs to an ordinary support request.

## When the API is unavailable

If an already-loaded Diagnostics page cannot reach the API, use the external fallback paths:

```bash
sudo docker logs --tail 500 mem-control-plane
```

You can also inspect the canonical `mem-control-plane` container in Portainer and review the configured persistent CLEF location from the host. The installed single-page application cannot guarantee availability during a complete Kestrel outage, so external inspection is intentional.

## Roles

- **Auditor:** safe overview, attention summaries, and incident summaries.
- **Operator:** technical events, support reports, Logging Health, and incident-owned Docker evidence.
- **Platform Owner:** Operator capabilities plus pipeline verification, Seq management, and Portainer handoff.

Hidden controls are not authorization. Every operation is re-authorized on the server.

## Rollback and recovery

If a new Diagnostics surface causes trouble, hide or roll back that Web surface while retaining the existing incident, event, recorder, and support-report APIs. Disabling optional Seq or Portainer integration must not disable MEM-native Diagnostics.

## Related documentation

- [Use Seq with MEM](seq-with-mem.md)
- [Use Portainer for advanced container diagnostics](../tools/optional-portainer.md)
- [Use restore evidence, logs, and support reports](../backups-and-restores/evidence-logs-support.md)

---

# Roadmap

Source: `docs/releases/roadmap.md`
Locale: en
Section: Releases
Status: supported
Applies to: 0.2.x

# Roadmap

Roadmap material describes intended direction. It is not a compatibility or delivery promise.

For operational decisions, prefer in this order:

1. the exact installed release identity;
2. release-specific notes and known limitations;
3. the current supported documentation pack;
4. roadmap/planning material.

A feature that is planned, under qualification, or demonstrated in a temporary QA image is not equivalent to a formally published release artifact.

---

# MEM product packages

Source: `docs/start/packages.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# MEM product packages

MEM 0.2.0 is delivered as a small set of cooperating products rather than one universal executable.

## MEM Control Plane

The Control Plane is the authoritative server-side package on the target host. It provides the Web UI, ASP.NET Core API, operator identity, setup and operational workflows, privileged HostAgent runtime access, SQLite state, diagnostics, and the local documentation reader.

## MEM CLI

The installed operator command is:

```bash
mem
```

MEM CLI is a remote client for the Control Plane. It does not reimplement Docker or recovery logic locally. It supports profiles, browser-approved named-device login, English or German human output, and stable English JSON fields.

Normal CLI credentials are stored through the operating-system secret service. Local or SSH execution does not bypass server-side roles, audit, or step-up rules.

## MEM Migrate CLI

The migration command is:

```bash
mem-migrate
```

MEM Migrate is a separate, version-specific product containing knowledge of the supported MEM 0.1.0 source layout. It assesses and captures a legacy source, verifies artifacts, creates packages, and performs worker-side conversion operations.

On the target, the Control Plane invokes the installed executable as a worker and validates its structured events, reports, paths, and output hashes. Legacy migration code is not linked into the normal control-plane runtime.

## MEM Migrate Source Assistant

The Source Assistant is a temporary local ASP.NET Core and React application on the old server. It guides access-code login, source assessment, stack selection, target-request import, capture, encrypted-package creation, download, and local lifecycle.

Its safe default is loopback-only access. It is not the permanent management interface for the target server.

## Which package do I need?

| Goal | Package |
|---|---|
| Install and operate MEM 0.2.0 | MEM Control Plane |
| Script or inspect the Control Plane | MEM CLI |
| Migrate from supported MEM 0.1.0 | MEM Migrate |
| Use a guided browser on the old server | MEM Migrate Source Assistant |

---

# Ersten Platform Owner erstellen

Source: `docs/de/installation/erster-platform-owner.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Ersten Platform Owner erstellen

Der erste Platform Owner ersetzt die vorübergehende Setup-Code-Berechtigung durch ein benanntes Operator-Konto **bevor die verwaltete Plattform eingerichtet wird**.

Führen Sie diesen Ablauf nur über die private Control-Plane-Verbindung aus.

## First-Owner-Setup öffnen

Wenn noch kein abgeschlossener, aktiver Platform Owner existiert, führt MEM zur Route `/bootstrap`.

Geben Sie dort den vom Host-Bootstrap erzeugten `mem_...`-Code ein. Der Server tauscht ihn gegen einen kurzlebigen, begrenzten Bootstrap-Grant. Dieser Grant ist keine normale Operator-Sitzung.

## Benannten Owner erstellen

Vergeben Sie einen eindeutigen Benutzernamen, optional eine gültige E-Mail-Adresse und ein starkes Passwort. Das Konto bleibt im Bootstrap-Zustand, bis TOTP erfolgreich geprüft wurde.

## TOTP einrichten und Recovery-Codes speichern

Scannen Sie den QR-Code oder tragen Sie das Secret in eine vertrauenswürdige Authenticator-App ein und bestätigen Sie den aktuellen sechsstelligen Code.

Speichern Sie die danach angezeigten Recovery-Codes offline oder in einem vertrauenswürdigen Passwortmanager. Sie gehören nicht in Tickets, Screenshots, Dokumentation oder Chat.

## Nach dem Bootstrap

- Der Platform Owner ist angemeldet und die authentifizierte Ersteinrichtung kann beginnen.
- `/bootstrap` eröffnet keinen weiteren First-Owner-Ablauf.
- Der Setup-Code verliert seine First-Owner-Funktion.
- Normale Rollen-, MFA- und Step-up-Regeln gelten.
- Weitere benannte Operatoren können später eingerichtet werden.

---

# Create the first Platform Owner

Source: `docs/installation/first-platform-owner.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Create the first Platform Owner

The first Platform Owner replaces the temporary setup-code authority with a named operator account **before the managed-platform Setup wizard begins**.

Only complete this process over the private Control Plane connection.

## Open first-owner setup

After opening the newly bootstrapped Control Plane, MEM directs you to `/bootstrap` while no completed, enabled Platform Owner exists.

Enter the `mem_...` setup code generated by the host bootstrap. The server exchanges it for a short-lived, scoped bootstrap grant. That grant is not a normal operator session.

## Create the named owner

Provide:

- a unique username;
- an optional valid email address;
- a strong password and confirmation.

The account remains in bootstrap state until TOTP is verified and the flow completes.

## Enrol TOTP

Scan the QR code or enter the authenticator secret into a trusted TOTP application, then submit the current six-digit code.

TOTP is part of the Platform Owner security boundary.

## Store recovery codes

After successful completion, MEM displays recovery codes. Store them offline or in a trusted password manager before acknowledging completion.

They are capability-bearing recovery material and should not be placed in ordinary documentation, tickets, screenshots, or chat.

## After first-owner bootstrap

When a completed Platform Owner exists:

- the owner is signed in and authenticated Setup can begin;
- `/bootstrap` no longer opens another first-owner flow;
- the setup code no longer grants first-owner authority;
- normal operator authorization and MFA policies apply;
- additional named operators can be enrolled later.

Before creating the first chat stack, confirm that the owner can sign out, sign back in, use TOTP, and retrieve the stored recovery procedure.

---

# Use Seq with MEM

Source: `docs/operations/seq-with-mem.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Use Seq with MEM

Seq is an optional advanced search layer for structured MEM events. MEM-native incidents, the safe event store, the persistent CLEF recorder, and support reports remain the primary product path when Seq is absent, stopped, or disabled.

## Keep delivery and runtime separate

MEM displays two distinct controls:

```text
MEM event delivery to Seq
Seq container runtime
```

Stopping Seq does not disable MEM-native Diagnostics. Disabling delivery stops new MEM events from being sent after the required API restart; it does not remove the Seq container or existing Seq data.

## Set up Seq from MEM

When Seq is absent, a Platform Owner can select **Set up Seq**. The guided workflow:

1. explains the approved image, private runtime, persistent data and no-public-ingress boundary;
2. records explicit Seq EULA acceptance;
3. accepts and confirms a one-time initial administrator password, or asks once for the current administrator password when existing authority must be preserved;
4. creates a frozen review containing no password or secret and includes the operator's event-delivery choice, which defaults to enabled after the next API restart;
5. requires recent identity verification before any one-time administrator password is submitted to the execution endpoint;
6. prepares only the approved exact image when it is not already local;
7. inspects and prepares the server-owned data directory before changing first-run authority;
8. hashes a new administrator password through isolated standard input only when first-run authority is required, while preserving an existing configured administrator hash on retry;
9. creates or starts the MEM-managed container and reports runtime readiness only after Docker and Seq `/health` verification pass;
10. provisions or reuses a dedicated ingest-only MEM credential and sends a harmless verification event;
11. queries Seq for that exact verification event before treating the MEM connection as verified;
12. when event delivery was selected, stages the desired delivery state for the next MEM API start; otherwise it leaves ongoing delivery disabled.

The browser does not submit image references, host paths, container IDs, Docker networks or ports. These deployment targets remain server-owned. Plaintext administrator passwords are used only for the protected execution step and are not stored in browser storage, operation evidence or server files. The dedicated ingestion credential remains server-side and is not returned to the browser.

When MEM finds an existing configured administrator password hash, the wizard preserves that authority rather than replacing it. The current Seq administrator password is used once to authenticate the protected connection/provisioning step and is then discarded. Initialized Seq data without its matching administrator secret blocks guided setup and requires explicit recovery.

The private Seq UI URL is optional. Without one, deployment can still succeed. After deployment, a Platform Owner can use **Configure Seq access** to save or change a server-approved private LAN, VPN, or SSH-forwarded browser URL. MEM then exposes **Open Seq** in a new tab. Do not save `0.0.0.0`; it is a bind address rather than a browser destination.

If the runtime becomes healthy but connection provisioning, verification, or delivery preparation fails, MEM preserves the healthy Seq runtime and its data and reports that setup needs attention. Use the normal **Connect MEM to Seq** or delivery controls to retry the remaining step; do not redeploy a healthy runtime merely to recover the integration step.

When setup completes with event delivery selected, the current API process is not silently reconfigured. The Seq workspace reports that an API restart is required and uses the server-owned restart contract for the active runtime context. Where the server provides an exact restart command, MEM displays it for copying. After the restart, reopen the workspace and verify that current and desired delivery agree and that normal structured events are arriving in Seq.

## Lifecycle controls

Depending on server-provided capabilities, a Platform Owner can:

- deploy the approved local image;
- start, stop, or restart the MEM-managed runtime;
- run a bounded health check;
- enable or disable future event delivery;
- remove the managed container while retaining its data directory.

Deploy, delivery changes, and removal require fresh identity verification. Stop and restart use explicit confirmation. Start and restart report success only after Docker state and Seq health are verified.

An unmanaged same-name container or immutable-identity mismatch fails closed.

## Apply delivery changes

Delivery preference changes are staged. The workspace shows both current and desired state and tells you when an API restart is required.

After changing delivery:

1. confirm the desired state in `/diagnostics/seq`;
2. restart the MEM API deliberately;
3. reopen the workspace;
4. confirm current and desired state now agree;
5. run a health check or pipeline verification.

Do not assume the current process changed delivery merely because the preference was saved.

## Remove Seq safely

Removal is blocked while current or desired delivery remains enabled. A successful removal preserves the configured Seq data directory. Permanent Seq data deletion is not part of this workflow.

## Starter searches

Useful fields include incident ID, operation ID, feature, event code, resource kind, and warning/error level. The workspace provides copyable examples validated for the approved Seq release.

Official references:

- [Seq documentation](https://datalust.co/docs)
- [Run Seq with Docker](https://datalust.co/docs/getting-started?platform=docker)
- [Seq query language](https://datalust.co/docs/the-seq-query-language)
- [Query syntax](https://datalust.co/docs/query-syntax)

## Security boundary

Seq credentials remain server-side. Do not put an ingestion key, administrator password hash, or secret-file content in browser storage, a support report, a screenshot, or an ordinary log message.

Seq is private operator tooling. MEM does not create public ingress for it automatically.

## Rollback

To roll back the Web workspace, hide the Seq management route while leaving the runtime and MEM-native Diagnostics untouched. To stop delivery, set the desired state to disabled and restart the API. Removing the runtime preserves data; data deletion requires a separate future workflow.

## Related documentation

- [Use the Diagnostics command centre](diagnostics-command-centre.md)
- [Use Portainer for advanced container diagnostics](../tools/optional-portainer.md)

---

# Import and materialise a portable backup

Source: `docs/backups-and-restores/import-backup.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Import and materialise a portable backup

Use **Backups → Import ZIP** to bring a portable MEM backup into the current Control Plane.

## Validation first

1. Select the portable `.zip` file.
2. Choose **Validate**.
3. Review the archive size, entry count, uncompressed size, manifest, checksum results, validation checks, warnings, and errors.
4. Continue only when the result is valid and the manifest identifies the expected stack.

Validation checks the archive structure, manifest, required payload declarations, and file checksums. A failed validation does not create a usable restore source.

## Automatic materialisation

For a valid upload, MEM normally materialises the payload into Backup Catalog storage during ingestion and returns a catalog entry ID. Open that catalog entry to continue.

If a retained valid upload exists without a linked catalog entry, open its upload detail page and use **Materialise**. This copies recovery files into catalog-owned storage; it does not start a restore.

## Keep the identities separate

The upload has a **validation ID** used to inspect or delete the retained source archive. The recovery payload has a **catalog entry ID**. A later restore has a **restore-session ID**.

```text
validation ID  → uploaded ZIP provenance
catalog ID     → recovery-ready managed payload
restore ID     → durable Restore Workspace
```

Do not paste a validation ID into a Restore Workspace URL or treat it as a restore attempt.

## Warnings and advisories

A valid archive may retain non-blocking advisories. Examples include secure handling of the signing key, the requirement to stop an old server before recovering the same Matrix identity, or the fact that route and TURN metadata are snapshots.

Advisories do not mean checksum failure, but they still require operator review.

## Original upload lifecycle

After successful materialisation, the original uploaded ZIP and the catalog payload have separate lifecycles. Deleting the retained uploaded ZIP does not delete:

- the catalog-managed payload;
- restore sessions, logs, evidence, or support reports;
- a restored production stack.

Keep or delete the original upload according to your provenance and storage policy. Do not delete the catalog payload until recovery is no longer required.

---

# Reset passwords and manage account lifecycle

Source: `docs/chat-servers/passwords-and-accounts.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Reset passwords and manage account lifecycle

## Outcome

Use guarded server-admin workflows to reset a Matrix password, deactivate an account, or reactivate it with a new password.

## Establish password-reset authority

MEM requires validated authority from an existing active Matrix server administrator before it can reset passwords.

Select **Set reset authority** or **Replace authority**. The normal mode uses a Matrix administrator ID and password. MEM uses the password to obtain and validate authority and does not store that password. An access token is available as an advanced recovery option and is encrypted with the MEM Data Protection key ring.

Setting or replacing authority requires a recent Control Plane step-up verification.

## Reset a password

Select **Reset password** beside an active Matrix user, enter and confirm the new password, and complete step-up when requested.

Synapse signs the user out of existing Matrix devices when the password changes. MEM cannot display or recover the previous password.

> [!WARNING]
> A password reset does not recreate end-to-end encryption keys. A user who has no verified device and cannot unlock server-side key backup may lose access to old encrypted history even though the account itself is usable again.

When the password being changed belongs to the administrator used as reset authority, MEM invalidates that authority. Establish it again before the next reset.

## Deactivate an account

Select **Deactivate account** and review the consequences. The current MEM workflow deactivates without requesting data erasure.

Synapse removes devices, encryption keys, access tokens, room memberships, third-party IDs, and the password. Existing room messages remain in room history. The last active Matrix administrator cannot be deactivated.

Deactivation is a high-risk action and requires recent step-up verification.

## Reactivate an account

A deactivated account can be reactivated with a new password. Select **Reactivate account**, enter the password twice, and complete step-up when requested.

Reactivation restores account access. It does not reconstruct removed devices, encryption keys, room memberships, or missing recovery material.

## Evidence to retain

Record the affected Matrix user ID, action time, operator, and any operation or error reference. Do not copy passwords, access tokens, recovery keys, or TOTP secrets into support reports.

---

# Use the MEM CLI

Source: `docs/cli/index.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Use the MEM CLI

The MEM CLI is the scriptable operator interface for the MEM Control Plane. The installed command is:

```bash
mem
```

## Outcome

After this section you can:

- point the CLI at the correct private Control Plane;
- authorize a named CLI device through the browser;
- inspect account, host, and chat-server state;
- transfer and inspect Backup Catalog material;
- inspect and run supported Restore Workspace actions;
- collect stable JSON evidence without depending on the React UI.

## Current MEM 0.2.0 boundary

The CLI uses the same server-side identity, role, capability, audit, and step-up policy as the browser. Running it on the Ubuntu host, over SSH, or from a private workstation does not create a trust bypass.

The current command surface is deliberately narrower than the browser UI:

| Available | Not currently available |
|---|---|
| Profiles, language, device login, account status, logout | Create or remove a chat server |
| Host status and stack inspection/doctor/history | Start or stop a stack |
| Backup Catalog list, inspect, lifecycle, export, import, and deletion | Create a new backup capture |
| Restore Workspace inspection and supported actions | Migration-session control |
| Stable JSON output | Global Diagnostics and Seq control |

Use the Control Plane for tasks not shown by `mem --help`. Do not invent command names from browser labels.

## First operator journey

```bash
mem --version

mem profile create home \
  --server https://mem.example.internal

mem profile select home
mem login --device --profile home
mem account show --profile home
mem host status --profile home
```

For local development on the Control Plane host, an explicit loopback HTTP endpoint is allowed:

```bash
mem profile create local --server http://127.0.0.1:7105
```

Production and private remote profiles must use HTTPS.

## Command families

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

The singular roots `backup` and `restore` remain parser compatibility aliases. New documentation and scripts should use `backups` and `restores`.

## Human output and JSON

Human-readable output can be English or German. Command names, option names, identifiers, JSON property names, machine status values, and error codes remain stable English.

```bash
mem host status --profile home --language de
mem host status --profile home --json | jq
```

`mem login --device` is interactive and intentionally does not support `--json`.

## During an incident

Start with read-only evidence:

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

An exit code of `2` usually means the command ran but the requested operational result was not healthy, ready, found, or completed. Preserve the JSON and the exit code.

## Read next

1. [Install the MEM CLI](install.md).
2. [Create profiles](profiles.md).
3. [Sign in with device login](device-login.md).
4. [Use JSON safely](json-and-scripting.md).
5. [Understand the CLI security model](security-model.md).

---

# Portable Sicherung importieren und materialisieren

Source: `docs/de/backups-und-wiederherstellen/importieren.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Portable Sicherung importieren und materialisieren

Unter **Sicherungen → ZIP importieren** bringen Sie eine portable MEM-Sicherung in die aktuelle Control Plane.

## Zuerst validieren

1. Portable `.zip`-Datei auswählen.
2. **Validieren** wählen.
3. Archivgröße, Eintragszahl, entpackte Größe, Manifest, Prüfsummen, Prüfungen, Warnungen und Fehler kontrollieren.
4. Nur fortfahren, wenn das Ergebnis gültig ist und das Manifest den erwarteten Stack identifiziert.

Die Validierung prüft Archivstruktur, Manifest, erforderliche Payload-Deklarationen und Dateiprüfsummen. Eine fehlgeschlagene Validierung erzeugt keine nutzbare Wiederherstellungsquelle.

## Automatische Materialisierung

Bei einem gültigen Upload materialisiert MEM den Payload normalerweise während der Aufnahme in den Sicherungskatalog und liefert eine Katalog-ID zurück.

Existiert ein aufbewahrter gültiger Upload ohne verknüpften Katalogeintrag, öffnen Sie die Upload-Detailseite und verwenden **Materialisieren**. Dadurch werden Wiederherstellungsdateien in Katalogspeicher kopiert; es startet keine Wiederherstellung.

## Identitäten trennen

```text
Validierungs-ID  → Herkunft des hochgeladenen ZIPs
Katalog-ID       → verwalteter wiederherstellbarer Payload
Restore-ID       → dauerhafter Wiederherstellungsarbeitsbereich
```

Eine Validierungs-ID gehört nicht in eine Restore-URL.

## Warnungen und Hinweise

Ein gültiges Archiv kann nicht blockierende Hinweise behalten, etwa zum sicheren Umgang mit dem Signaturschlüssel, zur notwendigen Abschaltung eines alten Servers oder zum Snapshot-Charakter von Routen und TURN. Hinweise sind keine Prüfsummenfehler, müssen aber gelesen werden.

## Lebenszyklus des Original-Uploads

Nach erfolgreicher Materialisierung besitzen Original-ZIP und Katalog-Payload getrennte Lebenszyklen. Die Löschung des Original-ZIPs löscht nicht:

- den Katalog-Payload;
- Wiederherstellungssitzungen, Logs, Nachweise oder Supportberichte;
- einen wiederhergestellten Produktions-Stack.

Bewahren oder löschen Sie den Original-Upload entsprechend Ihrer Herkunfts- und Speicherrichtlinie. Löschen Sie den Katalog-Payload nicht, solange Wiederherstellung benötigt wird.

---

# Passwörter und Kontolebenszyklus verwalten

Source: `docs/de/chat-servers/passwoerter-und-konten.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Passwörter und Kontolebenszyklus verwalten

## Ergebnis

Verwenden Sie geschützte Server-Admin-Workflows, um ein Matrix-Passwort zurückzusetzen, ein Konto zu deaktivieren oder es mit neuem Passwort zu reaktivieren.

## Autorität für Passwortzurücksetzung einrichten

MEM benötigt geprüfte Autorität eines bestehenden aktiven Matrix-Serveradministrators.

Wählen Sie **Reset-Autorität setzen** oder **Autorität ersetzen**. Der normale Modus verwendet Matrix-Administrator-ID und Passwort. MEM verwendet das Passwort einmalig zur Beschaffung und Prüfung der Autorität und speichert es nicht. Ein Zugriffstoken steht als erweiterte Wiederherstellungsoption bereit und wird mit dem MEM-Data-Protection-Schlüsselring verschlüsselt.

Das Setzen oder Ersetzen der Autorität erfordert eine aktuelle Control-Plane-Step-up-Prüfung.

## Passwort zurücksetzen

Wählen Sie bei einem aktiven Matrix-Benutzer **Passwort zurücksetzen**, geben Sie das neue Passwort zweimal ein und führen Sie bei Aufforderung Step-up aus.

Synapse meldet den Benutzer beim Passwortwechsel von vorhandenen Matrix-Geräten ab. MEM kann das alte Passwort weder anzeigen noch wiederherstellen.

> [!WARNING]
> Eine Passwortzurücksetzung stellt keine Ende-zu-Ende-Verschlüsselungsschlüssel wieder her. Ohne verifiziertes Gerät und entsperrbare serverseitige Schlüsselsicherung kann der Zugriff auf alten verschlüsselten Verlauf verloren bleiben.

Gehört das geänderte Passwort zum Administrator der Reset-Autorität, verwirft MEM diese Autorität. Richten Sie sie vor dem nächsten Reset neu ein.

## Konto deaktivieren

Wählen Sie **Konto deaktivieren** und prüfen Sie die Folgen. Der aktuelle MEM-Workflow deaktiviert ohne Datenlöschanforderung.

Synapse entfernt Geräte, Verschlüsselungsschlüssel, Zugriffstoken, Raummitgliedschaften, Drittanbieter-IDs und Passwort. Bestehende Raumnachrichten bleiben im Verlauf. Der letzte aktive Matrix-Administrator kann nicht deaktiviert werden.

Deaktivierung ist eine Hochrisikoaktion und erfordert aktuelle Step-up-Prüfung.

## Konto reaktivieren

Ein deaktiviertes Konto kann mit neuem Passwort reaktiviert werden. Wählen Sie **Konto reaktivieren**, geben Sie das Passwort zweimal ein und führen Sie Step-up aus.

Die Reaktivierung stellt Kontozugriff her. Entfernte Geräte, Verschlüsselungsschlüssel, Raummitgliedschaften oder fehlendes Wiederherstellungsmaterial werden nicht rekonstruiert.

## Nachweise

Erfassen Sie betroffene Matrix-Benutzer-ID, Zeitpunkt, Operator und Vorgangs- oder Fehlerreferenz. Kopieren Sie keine Passwörter, Zugriffstoken, Wiederherstellungsschlüssel oder TOTP-Geheimnisse in Supportberichte.

---

# MEM CLI verwenden

Source: `docs/de/cli/index.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

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

---

# Serverprüfung ausführen

Source: `docs/de/installation/server-pruefen.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Serverprüfung ausführen

Die Setup-Startseite klassifiziert den Host, bevor eine neue Installation angeboten wird.

Mögliche Ergebnisse:

- neue Installation;
- Legacy-Migration erforderlich;
- Reparatur oder vorhandene Ressourcen;
- bereits installiert;
- unbekannt, weil Docker oder Statusprüfung fehlgeschlagen ist.

## Legacy-Erkennung bedeutet Stopp

Wenn `mem-api` oder `mem-web` ohne aktuellen Installationsdatensatz erkannt wird, behandelt MEM den Host als Legacy-MEM-0.1.0-Quelle.

Führen Sie keine neue Installation aus. Lassen Sie die Quelle bestehen und verwenden Sie `mem-migrate`.

Die alte Installation kann außerdem PostgreSQL, NPM, Compose-Dateien, `.env`, `stack.sh`, Matrix-Container, Datenpfade und Zertifikate enthalten, die nicht unbedacht überschrieben werden dürfen.

## Vorhandene Ressourcen ohne Datensatz

Wenn MEM-Container, Netzwerke oder Volumes vorhanden sind, aber kein aktueller Installationsdatensatz existiert, bietet MEM einen Reparatur- oder Untersuchungsweg.

Prüfen Sie die Ressourcen und Diagnosen. Löschen Sie nichts nur, um eine Warnung zu beseitigen.

## Prüfung starten

Wählen Sie im Setup **Serverprüfungen ausführen**. Während der aktiven Ersteinrichtung erfasst MEM gruppierte Host-Prüfnachweise und übernimmt eine kompakte Zusammenfassung in den Installationsplan für die spätere Überprüfung und Supportnachweise.

Die Prüfungen umfassen:

### Host

- Ubuntu;
- CPU-Architektur;
- CPU-Anzahl;
- RAM.

### Docker

- Docker- und Daemon-Erreichbarkeit;
- Compose-Plugin;
- Docker-Datenpfad;
- Docker-Speichernutzung;
- vorhandene MEM-Container;
- Netzwerke und Volumes.

### Speicher

- freier Speicher auf `/`;
- freier Speicher im Docker-Datenpfad.

### Ports

Beobachtet werden:

```text
80
443
8443
8080
5432
```

8443 darf vom Installer belegt sein. Andere Listener müssen verstanden werden.

## Kompakte Ergebnisse richtig lesen

Die Seite Serverprüfungen lässt Blocker, Warnungen, nicht verfügbare Prüfungen und andere Punkte mit Handlungsbedarf sichtbar. Routineprüfungen mit erfolgreichem Ergebnis sind pro Gruppe eingeklappt, damit Entscheidungen im Vordergrund stehen. Öffnen Sie **Technische Details** und **Rohdaten** nur, wenn Sie Begründung oder begrenzte Befehlsnachweise benötigen.

Die meisten Browser-Prüfungen sind Hinweise. Eine Warnung ist kein automatisches Verbot; ein erfolgreiches Ergebnis ist keine Produktionsgarantie. Das Host-Bootstrap hat bereits seine harten Ubuntu-, RAM- und Speicherregeln angewendet; die Browser-Prüfung liefert eine zweite, laufzeitbewusste Betriebssicht.

Nach Abschluss der Einrichtung ist die Entwicklungsvorschau schreibgeschützt. Detaillierte Prüfläufe gehören zur aktiven Einrichtungssitzung und müssen einen Neustart der Control Plane nicht überleben; die Installation behält die kompakte Prüfübersicht für Review und Supportberichte.

## Weiter

Fahren Sie nur fort, wenn Docker erreichbar ist, keine Legacy-Migration erforderlich ist, vorhandene Ressourcen verstanden sind und private Zugriffs- sowie Speicherwarnungen akzeptabel sind.

---

# Zielanfrage importieren, erfassen und Paket erstellen

Source: `docs/de/migrieren/create-package.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Zielanfrage importieren, erfassen und Paket erstellen

Die Ziel-Control-Plane erstellt die öffentliche Verschlüsselungsanfrage. Der Source Assistant erzeugt damit ein Paket, das nur das Ziel entschlüsseln kann.

## Ergebnis

Eine frische Erfassung des ausgewählten Stacks liegt als verschlüsselte Datei `.memmigration.zip.age` mit Paketbericht und Prüfsummennachweis für die Übertragung nach MEM 0.2.0 vor.

## Ziel-Migrationsanfrage erstellen

Öffnen Sie auf dem MEM-0.2.0-Ziel **Migrationen** und erstellen Sie eine sichere Migrationsaufnahme. Schließen Sie bei Aufforderung die Step-up-Authentifizierung ab.

Laden Sie die Migrationsanfrage als JSON herunter. Sie enthält Aufnahmeidentität und öffentliche `age`-Empfängerinformationen. Die private Entschlüsselungsidentität verbleibt in der Ziel-Control-Plane und wird nie an den Source Assistant übertragen.

Übertragen Sie die JSON-Datei über Ihren normalen sicheren Administrationsweg zum Arbeitsplatz oder Quellhost.

## Anfrage importieren

Im Source-Assistant-Arbeitsbereich:

1. **Import request** wählen.
2. JSON-Datei hochladen oder vollständigen JSON-Inhalt einfügen.
3. Aufnahme-ID, Empfängerfingerabdruck, Anfrageart und Gültigkeit prüfen.
4. Unerwartetes Ziel, Fingerabdruckabweichung, fehlerhafte oder abgelaufene Anfrage ablehnen.

## Verschlüsselungsbereitschaft bestätigen

Vor der Erfassung verlangt der Source Assistant die Bestätigung, dass betroffene Benutzer ihre verschlüsselte Historie schützen sollen. Bestätigen Sie erst, nachdem die Benutzer aufgefordert wurden, angemeldet zu bleiben und ein anderes Gerät, Secure Backup samt Geheimnis oder exportierte Raumschlüssel zu prüfen.

## Erfassung erstellen

Wählen Sie eine frische Erfassung, außer der Source Assistant bietet ausdrücklich eine geeignete aufbewahrte Erfassung mit passender Identität und Zweckbindung an.

Die Erfassung ist eine dauerhafte serverseitige Operation. Schließen oder Aktualisieren des Browsers bricht sie nicht ab. Warten Sie auf Abschluss und prüfen Sie Warnungen.

Die aktive Quelle wird dabei nur gelesen. Arbeitsmaterial entsteht im Zustandsbereich des Source Assistant.

## Paket erstellen und herunterladen

Führen Sie aus:

1. **Create capture** ausführen und den Abschluss prüfen;
2. **Create package** wählen;
3. auf Verschlüsselung und Paketprüfung warten;
4. **Download package** wählen;
5. **Download package report** wählen.

Bewahren Sie Paket und Bericht zusammen auf. Notieren oder prüfen Sie nach jeder Übertragung die gemeldete SHA-256-Prüfsumme.

> [!IMPORTANT]
> Das verschlüsselte Paket ist keine normale MEM-Sicherung. Benennen Sie es nicht wie einen Sicherungskatalog-Export um und entpacken oder verändern Sie es nicht manuell.

## Lokale Löschgrenze

Das Löschen des fertigen lokalen Pakets entfernt das verschlüsselte Paket und die Paketberichte. Quellbewertung, Erfassungsjournal, Klartext-Erfassung, aktive Matrix-Daten und alter Server bleiben erhalten.

Weiter: [Paket hochladen und alten Server prüfen](upload-and-review.md).

---

# Sicherheits- und Zugriffseinstellungen

Source: `docs/de/operations/sicherheits-und-zugriffseinstellungen.md`
Locale: de
Section: Operations (Deutsch)
Status: supported
Applies to: 0.2.x

# Sicherheits- und Zugriffseinstellungen

Die MEM Control Plane ist eine privilegierte Verwaltungsoberfläche mit Docker-Autorität. Netzwerkprivatheit, benannte Anmeldung, MFA, Rollen und serverseitige Autorisierung wirken zusammen; keine dieser Grenzen ersetzt die anderen.

## Verwaltung privat halten

Verwenden Sie eine ausdrücklich ausgewählte Trusted-LAN-Adresse, VPN oder SSH/local-only-Weiterleitung. Veröffentlichen Sie die Control Plane nicht über NPM, öffentliches DNS oder Internet-NAT.

## Benannte Operatoridentität

Platform Owner und andere MEM-Operatoren verwenden Control-Plane-Konten. Diese sind getrennt von Matrix-Benutzern, die sich in Element anmelden.

## MFA und Recovery

Der erste Platform Owner richtet TOTP-MFA ein und erhält Recovery-Codes. Bewahren Sie Recovery-Codes offline auf und behandeln Sie sie wie Zugangsdaten.

## Operator-Passwörter und Wiederherstellung bei vergessenem Passwort

Verwenden Sie **Passwort ändern** im Kontomenü oder in der aktuellen Zeile unter **Operator-Zugriff**, solange Sie Ihr Passwort kennen. MEM verlangt eine frische Bestätigung mit aktuellem Passwort und Authenticator und widerruft nach der Änderung bestehende Browser- und CLI-/Gerätesitzungen.

Wenn ein Platform Owner sein Passwort vergessen hat, verwenden Sie den hostautoritativen Konsolenbefehl anstelle eines Browser-Reset-Ablaufs. Siehe [Operator-Passwort ändern oder wiederherstellen](operator-passwort-aendern-und-wiederherstellen.md) für den genauen Befehl, die erhaltenen Kontodaten und die aktuelle Wiederherstellungsgrenze.

## Step-up für Hochrisikoaktionen

MEM kann für destruktive oder hochriskante Browseraktionen eine frische Identitätsprüfung verlangen. Das konfigurierte Wiederverwendungsfenster bestimmt, wie lange eine erfolgreiche Prüfung in derselben serververwalteten Sitzung gültig bleibt.

Das Abschalten der zusätzlichen Hochrisiko-Step-up-Prüfung deaktiviert weder Anmeldung noch TOTP-MFA, Rollen, serverseitige Autorisierung oder Audit-Nachweise. Die Änderung dieser Policy bleibt selbst eine sensible Aktion.

## Sitzungs- und Rollenänderungen

Kennwort-, MFA-, Rollen-, Kontostatus-, Sitzungs- und Sicherheitsrichtlinienänderungen sollen Autorität gemäß Serververtrag invalidieren. Verlassen Sie sich nicht auf browserlokale Flags, um privilegierte Autorität zu verlängern.

## CLI

Die CLI verwendet benannte Geräteautorität und, wo unterstützt, OS Secret Service. Sie darf kein Kommandozeilen-Bypass für Browser-Step-up werden und keine wiederverwendbaren Geheimnisse akzeptieren, nur um Hochrisikoaktionen zu vereinfachen.

---

# Installieren oder migrieren?

Source: `docs/de/start/install-or-migrate.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# Installieren oder migrieren?

Wählen Sie den Installationsweg, bevor MEM Änderungen am Host vornimmt. Eine Neuinstallation und eine Migration sind unterschiedliche Workflows mit unterschiedlichen Sicherheitsgrenzen.

## Neuinstallation

Verwenden Sie den normalen Einrichtungsablauf auf einem sauberen oder bewusst vorbereiteten Ziel ohne ältere MEM-0.1.0-Anwendung.

Der aktuelle Ablauf bestimmt Installations- oder Reparaturmodus, prüft Host und Docker, bewertet Speicher und Ports, konfiguriert Domain und Zertifikat, zeigt geplante Ressourcen, erstellt oder prüft MEM-Netzwerk und dauerhaften Speicher, startet PostgreSQL und Nginx Proxy Manager, verifiziert das Ergebnis und übergibt an das Operator-Dashboard.

Die Control Plane ist bereits die private Anwendung, in der die Einrichtung läuft. Neue Pläne stellen keine separaten Anwendungscontainer `mem-api` und `mem-web` bereit.

## Vorhandenes MEM 0.1.0

Wenn MEM alte Container `mem-api` oder `mem-web` ohne aktuellen Installationsdatensatz erkennt, wird der Operator zu MEM Migrate geführt. Führen Sie keine Neuinstallation über den alten Server aus.

Der sichere Weg ist: sauberes Ziel vorbereiten, Zielanfrage erstellen, einen Quell-Stack bewerten und erfassen, verschlüsseltes Paket erstellen, importieren und validieren, Kandidaten konvertieren und privat bereitstellen, dann prüfen, übernehmen, verifizieren und abschließen.

Der aktuelle Adapter unterstützt definierte MEM-0.1.0-Quellen. Er ist kein universeller Importer für jeden manuell aufgebauten Synapse-Server.

## Unvollständige MEM-Ressourcen

Vorhandene MEM-eigene Dienste oder Stacks ohne abgeschlossenen Installationsdatensatz gelten als Reparatur- oder Prüfzustand und nicht automatisch als saubere Neuinstallation.

Prüfen Sie Ressourcen und Diagnosen. Löschen Sie keine Datenbank, kein Volume, Netzwerk oder keinen Container nur deshalb, weil die Ressource noch nicht korrekt in der Oberfläche erscheint.

## Recovery für verschlüsselte Nachrichten

Servermigration und Restore erhalten serverseitig gespeicherte verschlüsselte Ereignisse. Ende-zu-Ende-Schlüssel, die ausschließlich auf Benutzergeräten vorhanden waren, können sie nicht neu erzeugen.

Prüfen Sie vor Passwort-Resets, Gerätewechsel oder Migration, ob wichtige Benutzer einen Recovery Key oder ein Key-Backup und möglichst ein weiteres verifiziertes Gerät besitzen.

| Aktuelle Situation | Richtiger Start |
|---|---|
| Sauberer Zielhost | Normale MEM-Einrichtung |
| Aktuelle Installation mit ausgefallenem Dienst | Reparatur und Diagnose |
| Alte Quelle mit `mem-api` / `mem-web` | MEM Migrate |
| Beliebiger Nicht-MEM-Synapse-Server | Vom aktuellen Migrationsadapter nicht unterstützt |

---

# Run the server checks

Source: `docs/installation/check-server.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Run the server checks

The Setup start page classifies the host before it offers a fresh installation.

Possible results include:

- fresh install;
- legacy migration required;
- repair or existing resources detected;
- already installed;
- unknown because Docker or state inspection failed.

## Legacy detection is a stop condition

When `mem-api` or `mem-web` is detected without a current installation record, MEM treats the host as a legacy MEM 0.1.0 source.

Do not continue with fresh installation. Keep the source intact and use `mem-migrate`.

The old deployment may also contain PostgreSQL, Nginx Proxy Manager, Compose files, `.env`, `stack.sh`, Matrix containers, data directories, and certificates that the new installer must not overwrite by assumption.

## Existing resources without a current record

When MEM-related containers, networks, or volumes exist but the Control Plane cannot find a current installation record, it offers a repair/investigation path.

Inspect the resources and Diagnostics before continuing. Do not delete containers or volumes solely to clear the warning.

## Start the check

From Setup, choose **Run server checks**. During active first-time Setup, MEM records grouped host-check evidence and keeps a compact summary with the installation plan for later Review and support evidence.

The current checks cover:

### Host

- Ubuntu detection;
- CPU architecture;
- CPU count;
- total memory.

### Docker

- Docker command and daemon reachability;
- Docker Compose plugin;
- Docker data-root location;
- Docker disk usage;
- existing MEM containers;
- existing networks and volumes.

### Storage

- root filesystem free space;
- Docker data-root free space.

### Ports

MEM inspects listeners on:

```text
80
443
8443
8080
5432
```

Port 8443 is expected to be occupied by the installer. Other listeners require operator review unless they are known parts of the intended environment.

## Read the compact results

The Server Checks page keeps blockers, warnings, unavailable checks, and other attention findings visible. Routine successful checks are collapsed by group so the operator can focus on what needs a decision. Expand **Technical details** and **Raw evidence** only when you need the underlying explanation or bounded command evidence.

Most browser checks are advisory. A warning does not automatically mean the host is unusable, and a pass does not prove production capacity. The earlier host bootstrap has already enforced its hard Ubuntu, memory, and disk rules; the browser checks provide a second, runtime-aware operational view.

After Setup completes, development preview is read-only. Detailed check runs are active-session evidence and may not survive a Control Plane restart; the installation keeps the compact check snapshot used by Review and support reporting.

## Continue

Continue to the domain stage only when:

- Docker is reachable;
- no legacy install requires migration;
- unexpected resources are understood;
- storage and port warnings are acceptable;
- you know how the Control Plane will remain private.

---

# Import the target request, capture, and create the package

Source: `docs/migrate/create-package.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Import the target request, capture, and create the package

The target Control Plane creates the public encryption request. The Source Assistant uses it to produce a package that only the target can decrypt.

## Outcome

A fresh capture of the selected source stack is packaged as an encrypted `.memmigration.zip.age` file, with a package report and checksum evidence ready for transfer to MEM 0.2.0.

## Create the target Migration Request

On the MEM 0.2.0 target, open **Migrations** and create a secure migration intake. Complete operator step-up when requested.

Download the Migration Request JSON. The request contains the intake identity and public `age` recipient information. The target private decryption identity remains inside the target Control Plane and is never sent to the Source Assistant.

Transfer the request JSON to your operator workstation or source host through your normal secure administration path.

## Import the request

In the Source Assistant workspace:

1. Choose **Import request**.
2. Upload the JSON file or paste its complete JSON content.
3. Review the intake ID, recipient fingerprint, request kind, and expiry or validity information shown.
4. Reject an unexpected target, fingerprint mismatch, malformed request, or expired request.

## Confirm encryption readiness

Before capture, the Source Assistant requires acknowledgement that affected users have been told to protect their encrypted history. Confirm only after users have been advised not to sign out and to verify another device, Secure Backup and its recovery secret, or exported room keys.

## Create the capture

Choose a fresh capture unless the Source Assistant explicitly offers an eligible retained capture whose source identity and purpose still match this request.

Capture is a durable server-side operation. Closing or refreshing the browser does not cancel it. Wait for the Source Assistant to report completion and inspect any warnings before packaging.

The capture is read-only with respect to the live legacy stack. It creates migration working material beneath the Source Assistant state root.

## Create and download the package

Continue through:

1. **Create package**;
2. wait for encryption and package verification;
3. **Download package**;
4. **Download package report**.

Store the package and report together. Record or verify the reported SHA-256 checksum after any copy or transfer.

> [!IMPORTANT]
> The encrypted package is not a normal MEM backup. Do not rename it to look like a Backup Catalog export and do not unpack or modify it manually.

## Local deletion boundary

Deleting the completed local package removes the encrypted package and its package reports. It does not delete the source assessment, capture journal, plaintext source capture, live Matrix data, or old server.

Next: [Upload and review the old server](upload-and-review.md).

---

# Security and access settings

Source: `docs/operations/security-and-access-settings.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Security and access settings

The MEM Control Plane is a privileged administration surface with Docker authority. Network privacy, named authentication, MFA, roles, and server-side authorization work together; none replaces the others.

## Keep administration private

Use an explicitly selected Trusted LAN address, VPN, or SSH/local-only forwarding. Do not publish the Control Plane through NPM, public DNS, or Internet-facing NAT.

## Named operator identity

Platform Owners and other MEM operators use Control Plane accounts. These accounts are separate from Matrix users who sign in to Element.

## MFA and recovery

The first Platform Owner establishes TOTP MFA and receives recovery codes. Store recovery codes offline and treat them as credentials.

## Operator passwords and forgotten-password recovery

Use **Change password** from the account menu or the current row in **Operator access** when you still know your password. MEM requires fresh current-password and authenticator verification, then revokes existing browser and CLI/device sessions after the password changes.

If a Platform Owner has forgotten the password, use the host-authoritative console recovery command rather than a browser reset flow. See [Change or recover an operator password](operator-password-change-and-recovery.md) for the exact command, preservation rules, and recovery boundary.

## High-risk step-up

MEM can require fresh identity verification for destructive/high-risk browser actions. The configured reuse window controls how long a successful verification remains valid for the same server-managed session.

Disabling high-risk step-up does not disable login, TOTP MFA, roles, server-side authorization, or audit evidence. Changing the step-up policy itself remains a sensitive operation.

## Session and role changes

Password, MFA, role, account-status, session, and security-policy changes should invalidate authority where the server contract requires it. Do not rely on browser-local flags to extend privileged authority.

## CLI

The CLI uses named-device authority and OS Secret Service where supported. It must not become a command-line bypass for browser step-up or accept reusable secrets merely to make a high-risk operation easier.

---

# Install or migrate?

Source: `docs/start/install-or-migrate.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# Install or migrate?

Choose the installation path before MEM changes the host. A fresh installation and a migration are different workflows with different safety boundaries.

## Fresh installation

Use the normal setup flow on a clean or deliberately prepared target without a legacy MEM 0.1.0 application.

The current setup journey determines installation or repair mode, checks the host and Docker, reviews storage and ports, configures domain and certificate, reviews planned resources, creates or verifies the MEM network and persistent storage, starts PostgreSQL and Nginx Proxy Manager, runs verification, and hands control to the operator dashboard.

The Control Plane is already the private application running setup. New plans do not deploy separate `mem-api` and `mem-web` application containers.

## Existing legacy MEM 0.1.0

If MEM detects old `mem-api` or `mem-web` containers without a current installation record, it directs the operator to MEM Migrate. Do not run a fresh installation over the legacy server.

The safe path is to prepare a clean target, create a target request, assess and capture one source stack, create the encrypted package, import and validate it, convert and privately stage the candidate, then review, adopt, verify, and finish.

The current adapter is for supported MEM 0.1.0 sources. It is not a universal importer for every manually assembled Synapse server.

## Partial MEM resources

Existing MEM-owned services or stacks without a completed installation record are treated as a repair or review condition, not automatically as a clean installation.

Inspect the reported resources and diagnostics. Do not delete a database, volume, network, or container merely because it is not yet represented correctly in the UI.

## Protect encrypted-message recovery

Server migration and restore preserve encrypted events stored on the server, but cannot recreate end-to-end encryption keys that existed only on user devices.

Before password resets, device replacement, or migration, confirm that important users have a recovery key or key backup and, where possible, another verified device.

| Current situation | Correct starting point |
|---|---|
| Clean target host | Normal MEM setup |
| Current installation with a failed dependency | Repair and diagnostics |
| Old `mem-api` / `mem-web` source | MEM Migrate |
| Arbitrary non-MEM Synapse server | Not supported by the current migration adapter |

---

# Install the MEM CLI

Source: `docs/cli/install.md`
Locale: en
Section: CLI and automation
Status: advanced
Applies to: 0.2.x

# Install the MEM CLI

## Outcome

A correct installation provides one stable command:

```text
/opt/mem/cli/<version>/mem
/usr/local/bin/mem -> /opt/mem/cli/<version>/mem
```

The versioned binary is root-owned and executable. Normal operators invoke `mem` without `sudo`.

## Check whether the release installed it

```bash
command -v mem
mem --version
mem --help
ls -l /usr/local/bin/mem
```

Do not assume the CLI is installed merely because the Control Plane is installed. In the current source baseline, the installer integration exists but is disabled by default until release packaging supplies and enables a prebuilt CLI binary. The public release package must be checked before this page is treated as an automatic-install promise.

## Linux credential-store prerequisite

Device login stores its opaque credential through Secret Service using `secret-tool`:

```bash
sudo apt install libsecret-tools
secret-tool --help
```

Package presence is not enough. The non-root account that runs `mem login --device` must have a usable, unlocked Secret Service-compatible keyring. A headless SSH session may not have one.

MEM fails closed when the secure store is unavailable. It does not fall back to a plaintext file, profile, environment variable, command argument, or stdin.

## Install a supplied release binary

Use the CLI-owned installer when you have the release binary and matching source script:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --dry-run \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --binary ./artifacts/mem-cli/linux-x64/mem \
  --version 0.2.0
```

The script checks for `secret-tool`, installs the binary with mode `0755`, and updates `/usr/local/bin/mem` to the selected version.

## Development-only source publish

From the repository root on a development machine with the .NET 8 SDK:

```bash
./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --dry-run \
  --version dev

sudo ./cli/src/Mem.Cli/Scripts/install-host-command.sh \
  --version dev
```

This publishes a self-contained `linux-x64` single-file binary before installing it. It is not a substitute for proving the official release artifact.

## Verify success

```bash
command -v mem
readlink -f /usr/local/bin/mem
ls -l /opt/mem/cli/<version>/mem
mem --version
mem --help
```

If the shell still resolves an older command, run:

```bash
hash -r
```

## Safety and failure handling

- Use `sudo` only for package installation or updating the root-owned command.
- Do not run normal profile, login, backup, or restore commands with `sudo`; root has a different configuration directory and keyring context.
- Do not copy the binary into an operator-writable production path.
- Do not skip the `secret-tool` check for a normal installation.
- Preserve the old version directory until the replacement has passed `mem --version`, `mem --help`, and a device-login proof.

## Related documentation

- [Profiles and servers](profiles.md)
- [Sign in with device login](device-login.md)
- [CLI troubleshooting](troubleshooting.md)

---

# MEM CLI installieren

Source: `docs/de/cli/installieren.md`
Locale: de
Section: CLI und Automatisierung
Status: advanced
Applies to: 0.2.x

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

---

# Configure profiles and servers

Source: `docs/cli/profiles.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Configure profiles and servers

A profile identifies one MEM Control Plane and an optional human-output language. It does not authenticate the operator.

## What a profile stores

A profile may contain only:

- a normalized profile name;
- the Control Plane server URL;
- optional `en` or `de` preference;
- default-profile selection.

It must not contain a password, TOTP value, recovery code, browser cookie, installer token, bearer credential, Secret Service value, raw response, or runtime evidence.

The normal Linux configuration file is typically:

```text
~/.config/mem/config.json
```

`XDG_CONFIG_HOME` takes precedence when set. MEM writes the directory as user-only and the file as user read/write, rejects symbolic-link paths, and writes updates atomically.

## Create and select a profile

```bash
mem profile create home \
  --server https://mem.example.internal \
  --language en

mem profile select home
mem profile list
```

Profile names are normalized to lowercase, must be 1–32 characters, must begin and end with a letter or number, and may contain only letters, numbers, and hyphens.

## Server URL rules

Remote and production endpoints must use HTTPS:

```bash
mem profile create home --server https://mem.example.internal
```

HTTP is accepted only for an explicit loopback development endpoint:

```bash
mem profile create local --server http://127.0.0.1:7105
```

The URL must contain only scheme, host, and optional port. User information, query strings, fragments, and non-root paths are rejected. Use the Control Plane address, not a Matrix or Element public URL.

## Default profile

Both commands select the default profile:

```bash
mem profile select home
mem config set default-profile home
```

Use one profile for a single command without changing the default:

```bash
mem host status --profile lab
```

Removing the default profile also clears the default selection:

```bash
mem profile remove old-lab
```

Removing profile metadata does not revoke or delete an associated Secret Service credential. Run `mem logout --profile <name>` before removing a profile when a device session exists.

## Server precedence

For an operational invocation, MEM resolves the server in this order:

```text
--server <url>
MEM_SERVER_URL
selected or default profile
http://localhost:7105 development default
```

The hidden `--host-agent-url` and `MEM_HOST_AGENT_URL` compatibility inputs are still parsed for older scripts but must not be used in new documentation or automation.

Operational commands still require a named profile because the secure credential is keyed by profile and server. An explicit `--server` does not remove that requirement.

## Language precedence

```text
--language <en|de>
MEM_CLI_LANGUAGE
selected/default profile language
global local language preference
system locale
English
```

Examples:

```bash
mem config set language de
mem config get language
mem host status --profile home --language en
MEM_CLI_LANGUAGE=de mem --help
```

JSON field names and machine values remain English regardless of the selected language.

## Verify success

```bash
mem profile list --json | jq
mem config get default-profile --json | jq
mem config get language --json | jq
```

Review the server URL before any destructive backup or restore command.

## Related documentation

- [Sign in with device login](device-login.md)
- [Account status and logout](account-and-logout.md)
- [CLI security model](security-model.md)

---

# Profile und Server konfigurieren

Source: `docs/de/cli/profile.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

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

---

# Sign in with device login

Source: `docs/cli/device-login.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Sign in with device login

Device login is the normal MEM CLI authority path. It connects a selected local profile to a named MEM operator without placing passwords, TOTP codes, or bearer credentials on the command line.

## Before you begin

- Create or select a valid profile.
- Confirm the profile reaches the private Control Plane.
- Run the command as the normal non-root operator.
- Ensure `secret-tool` and an unlocked Secret Service-compatible keyring are available.
- Have an existing browser session for a named MEM operator with TOTP configured.

## Start login

```bash
mem login --device --profile home
```

`--json` is intentionally not supported because the command is an interactive, multi-step browser approval flow.

## What MEM does

1. Validates the selected profile and server.
2. Performs a write/read/delete probe against the OS secret store.
3. Reuses an already stored valid credential without creating another server authorization.
4. Creates a high-entropy verifier held only in CLI process memory.
5. Asks the server for a browser approval URL and short code.
6. Prints the private `/cli/authorize` URL and code.
7. Polls while the operator reviews and approves the device in the browser.
8. Stores the issued opaque device credential only after the original CLI proves its verifier.

The browser never receives the device credential. The displayed code cannot replace the verifier.

## Approve in the browser

Open the URL printed by the CLI, enter the short code, review the device label and expiry, and approve only the device you started. MEM uses the current named browser operator and requests fresh identity verification when policy requires it.

The current server default gives a pending authorization 10 minutes. The server owns this lifetime and may change it. If the code expires, start login again.

## Verify success

```bash
mem account show --profile home
mem host status --profile home
```

The current server defaults are:

- 8 hours maximum idle lifetime;
- 7 days absolute lifetime.

The CLI prints the actual idle and absolute expiry returned by the server. Server configuration remains authoritative.

## Common failures

| Result | Meaning | Action |
|---|---|---|
| `cli_login_profile_required` | No named profile was selected | Create or select a profile |
| `cli_login_secure_store_unavailable` | Secret Service probe or read failed | Unlock/configure the non-root keyring |
| `cli_login_rate_limited` | Shared authorization-start budget was exceeded | Wait, then retry once |
| `cli_login_authorization_denied` | Browser operator denied the device | Review the device and start again |
| `cli_login_authorization_expired` | Approval window ended | Start a new login |
| `cli_login_unreachable` | Control Plane became unreachable | Restore private connectivity and retry |
| `cli_login_secure_store_write_failed` | Approval succeeded but local secure storage failed | Fix the keyring and start a new login |

## Safety boundary

Do not approve a device you did not start. Do not paste approval codes into public channels. Never extract or copy the stored credential from Secret Service. MEM rejects password, TOTP, recovery-code, bearer-token, and device-credential command options.

## Related documentation

- [Profiles and servers](profiles.md)
- [Account status and logout](account-and-logout.md)
- [CLI security model](security-model.md)

---

# Mit Geräteanmeldung anmelden

Source: `docs/de/cli/geraeteanmeldung.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

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

---

# Inspect account status and sign out

Source: `docs/cli/account-and-logout.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Inspect account status and sign out

## Inspect the current session

```bash
mem account show --profile home
mem account show --profile home --json | jq
```

A successful result may show only safe session facts:

- profile and server;
- `authenticated` status;
- operator display name;
- assigned roles;
- idle expiry;
- absolute expiry.

It must not expose the device credential, browser cookie, installation identity, password, TOTP value, recovery code, raw claims, or secret-store entry.

`account show` verifies both local credential presence and current server acceptance. A locally stored credential is not proof that the server still accepts it.

## Interpret results

| Status/error | Meaning |
|---|---|
| `authenticated` | The server accepts the stored session |
| `signed_out` | No credential exists for this profile and server |
| `unauthenticated` | A local credential exists but the server rejected or expired it |
| `cli_account_secure_store_unavailable` | The local secret store could not be read safely |
| `cli_account_unavailable` | The Control Plane could not verify the session |

A signed-out or rejected result exits non-zero so scripts do not mistake it for authenticated state.

## Sign out

```bash
mem logout --profile home
```

MEM attempts to revoke the current server-side device session, then removes the local Secret Service credential.

Possible outcomes:

- `revoked` — server revocation was confirmed;
- `unauthenticated` — the server already rejected or expired the session;
- `unavailable` — server revocation could not be confirmed, but the local credential was removed;
- no stored session — logout is idempotent and succeeds without a server request.

If local deletion fails, logout fails rather than claiming the credential was removed.

## Verify logout

```bash
mem account show --profile home --json | jq
mem host status --profile home --json | jq
```

The account command should report signed out, and operational commands should require device login. MEM must not fall back to installer tokens or agent secrets.

## Replace a stale session

```bash
mem logout --profile home
mem login --device --profile home
mem account show --profile home
```

A Control Plane reinstall or change of the current installation identity invalidates old CLI device credentials even when the server URL is unchanged.

## Related documentation

- [Sign in with device login](device-login.md)
- [CLI troubleshooting](troubleshooting.md)
- [CLI security model](security-model.md)

---

# Kontostatus prüfen und abmelden

Source: `docs/de/cli/konto-und-abmeldung.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

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

---

# Inspect the host and chat servers

Source: `docs/cli/host-and-stack-commands.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Inspect the host and chat servers

The current CLI provides observation and diagnostic commands for the Control Plane host and managed chat servers. It does not currently create, start, stop, or remove stacks.

## Check Control Plane readiness

```bash
mem host status --profile home
mem host status --profile home --json | jq
```

Exit code `0` requires the returned host status to be `ready`. A reachable but unready result exits `2` so automation can distinguish operational health from invocation errors.

## List chat servers

```bash
mem stack list --profile home
mem stack list --profile home --json | jq
```

The list includes stable stack identity, last verified state, and public Matrix and Element URLs where recorded.

## Inspect one stack safely

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
```

The CLI deliberately projects public service facts rather than raw Docker container IDs, host paths, environment values, or secret-bearing runtime configuration.

## Run doctor checks

```bash
mem stack doctor <slug-or-id> --profile home
mem stack doctor <slug-or-id> --profile home --json | jq
```

Doctor records or reads a server-owned diagnostic operation and returns structured checks. Exit code `0` means all returned checks passed. Exit code `2` means at least one check failed or the requested operational result was not successful.

A failed doctor check is evidence, not permission to mutate Docker manually. Review the check code, URL, status, operation ID, report ID, and safe detail before choosing a repair.

## Review operation history

```bash
mem stack operations <slug-or-id> --profile home --json | jq
```

Operation history can show requested action, status, requester, mutation level, current step, timestamps, idempotency key, and a safe last-error summary.

## Unsupported commands

The following are not in the current release command inventory:

```text
mem stack create
mem stack start
mem stack stop
mem stack delete
mem stack compare
```

Use the current Control Plane UI and documented operator workflow for lifecycle changes.

## Evidence bundle

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Share only the evidence required for the incident. Remove private hostnames where necessary and never include credentials, passwords, TOTP or recovery codes, signing keys, private keys, raw backup contents, or Secret Service output.

## Related documentation

- [JSON output and scripting](json-and-scripting.md)
- [CLI troubleshooting](troubleshooting.md)
- [Operate chat servers](../chat-servers/index.md)

---

# Host und Chatserver prüfen

Source: `docs/de/cli/host-und-stack-befehle.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

# Host und Chatserver prüfen

Die aktuelle CLI stellt Beobachtungs- und Diagnosebefehle für den Control-Plane-Host und verwaltete Chatserver bereit. Sie erstellt, startet, stoppt oder entfernt derzeit keine Stacks.

## Bereitschaft der Control Plane prüfen

```bash
mem host status --profile home
mem host status --profile home --json | jq
```

Exit-Code `0` verlangt den Hoststatus `ready`. Ein erreichbares, aber nicht bereites Ergebnis liefert `2`.

## Chatserver auflisten

```bash
mem stack list --profile home
mem stack list --profile home --json | jq
```

Die Liste enthält stabile Stack-Identität, letzten geprüften Zustand und bekannte öffentliche Matrix- und Element-URLs.

## Einen Stack sicher prüfen

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
```

Die CLI projiziert bewusst öffentliche Servicedaten statt roher Docker-Container-IDs, Hostpfade, Umgebungswerte oder geheimnishaltiger Laufzeitkonfiguration.

## Doctor-Checks ausführen

```bash
mem stack doctor <slug-or-id> --profile home
mem stack doctor <slug-or-id> --profile home --json | jq
```

Doctor erfasst oder liest einen servereigenen Diagnosevorgang. Exit-Code `0` bedeutet, dass alle gelieferten Checks bestanden. Exit-Code `2` bedeutet mindestens einen fehlgeschlagenen Check oder ein nicht erfolgreiches Betriebsergebnis.

Ein fehlgeschlagener Check ist ein Nachweis, keine Erlaubnis zur manuellen Docker-Mutation. Prüfen Sie Code, URL, Status, Vorgangs-ID, Bericht-ID und sichere Details.

## Vorgangsverlauf prüfen

```bash
mem stack operations <slug-or-id> --profile home --json | jq
```

Der Verlauf kann Aktion, Status, Anforderer, Mutationsstufe, aktuellen Schritt, Zeitstempel, Idempotency Key und sichere letzte Fehlerzusammenfassung zeigen.

## Nicht unterstützte Befehle

```text
mem stack create
mem stack start
mem stack stop
mem stack delete
mem stack compare
```

Verwenden Sie für Lifecycle-Änderungen die aktuelle Control Plane und den dokumentierten Operatorablauf.

## Nachweisbündel

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Teilen Sie nur notwendige Nachweise und niemals Credentials, Passwörter, TOTP- oder Wiederherstellungscodes, Signatur- oder private Schlüssel, rohe Backup-Inhalte oder Secret-Service-Ausgabe.

## Verwandte Dokumentation

- [JSON-Ausgabe und Skripting](json-und-skripting.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
- [Chatserver betreiben](../chat-servers/index.md)

---

# Operator-Passwort ändern oder wiederherstellen

Source: `docs/de/operations/operator-passwort-aendern-und-wiederherstellen.md`
Locale: de
Section: Operations (Deutsch)
Status: supported
Applies to: 0.2.x

# Operator-Passwort ändern oder wiederherstellen

MEM trennt die normale selbstständige Passwortänderung von der hostautoritativen Wiederherstellung. Verwenden Sie den Browser, solange Sie Ihr aktuelles Passwort kennen. Verwenden Sie die Host-Konsole nur, wenn ein Platform Owner sein Passwort vergessen hat und den normalen Änderungsablauf nicht abschließen kann.

Wenn Sie aus der Control Plane ausgesperrt sind und die integrierte Seite **Dokumentation** nicht öffnen können, rufen Sie die öffentliche Dokumentation auf **messageeasymode.com** auf und suchen Sie nach **Operator-Passwort ändern oder wiederherstellen**. Die öffentliche Dokumentation wird aus derselben MEM-Dokumentationsquelle synchronisiert.

## Passwort im angemeldeten Zustand ändern

Für Ihr eigenes benanntes Operatorkonto:

1. Öffnen Sie das Kontomenü oder **Operator-Zugriff**.
2. Wählen Sie für den aktuellen Operator **Passwort ändern**.
3. Bestätigen Sie Ihre Identität mit Ihrem **aktuellen Passwort** und einem aktuellen **Authenticator-App-Code**.
4. Geben Sie das neue Passwort zweimal ein. Verwenden Sie bei Bedarf **Passwort anzeigen**, um komplexe Zeichen vor dem Absenden zu prüfen.
5. Wählen Sie **Passwort ändern**.

Das neue Passwort muss die von MEM angezeigte Passwortrichtlinie erfüllen. In MEM 0.2.x verlangt das Formular mindestens 14 Zeichen mit Groß- und Kleinbuchstaben, einer Zahl, einem Sonderzeichen und mindestens vier unterschiedlichen Zeichen.

Nach einer erfolgreichen Änderung:

- macht MEM bestehende Browser- und CLI-/Gerätesitzungen dieses Operators ungültig;
- kehrt der aktuelle Browser zur Anmeldung zurück;
- wird das alte Passwort abgelehnt;
- bleibt die bestehende TOTP-Authenticator-Konfiguration erhalten;
- bleiben ungenutzte Wiederherstellungscodes erhalten;
- bleiben Rollen und normaler Kontostatus erhalten.

Melden Sie sich anschließend mit dem neuen Passwort und dem bestehenden Authenticator erneut an.

Ein Wiederherstellungscode kann für diesen Vorgang das aktuelle Passwort nicht ersetzen und kann kein Step-up für Hochrisikoaktionen erfüllen.

## Vergessenes Platform-Owner-Passwort wiederherstellen

MEM 0.2.x stellt für ein vergessenes Operator-Passwort keinen E-Mail-Reset-Link, keine Sicherheitsfragen und keine Web-Schaltfläche bereit, mit der ein Administrator das Passwort eines anderen Operators zurücksetzt.

Wenn ein **Platform Owner** sein Passwort vergessen hat, verwenden Sie die hostautoritative Wiederherstellung über die Konsole des MEM-Servers.

Führen Sie auf dem MEM-Host aus:

```bash
sudo docker exec -it mem-control-plane \
  dotnet Api.dll operator reset-password PLATFORM_OWNER_USERNAME
```

Der Befehl ist interaktiv. Er fordert Sie auf, den ausgewählten Platform-Owner-Benutzernamen zur Bestätigung des Ziels einzugeben, und fragt das Ersatzpasswort anschließend zweimal ohne Terminal-Echo ab.

Geben Sie das neue Passwort **nicht** in der Befehlszeile, im Shell-Verlauf, als Skriptargument oder in einem Supportbericht an.

Bei Erfolg meldet der Befehl die Passwortzurücksetzung. Bestehende TOTP-Konfiguration, ungenutzte Wiederherstellungscodes, Rollen und Plattformdaten bleiben erhalten; vorhandene MEM-Sitzungen dieses Kontos werden widerrufen.

Danach:

1. Kehren Sie zur MEM-Anmeldeseite zurück.
2. Melden Sie sich mit dem Ersatzpasswort an.
3. Schließen Sie MFA mit dem bestehenden Authenticator-Code ab oder verwenden Sie, falls passend, in der MFA-Stufe einen vorhandenen Wiederherstellungscode.

## Aktuelle Wiederherstellungsgrenze

Der obige Host-Befehl ist in MEM 0.2.x bewusst ein **Platform-Owner-Wiederherstellungsbefehl**. Er ist kein allgemeiner Web- oder Konsolenmechanismus, mit dem ein Operator das Passwort eines anderen benannten Operators zurücksetzen kann.

Umgehen Sie diese Grenze nicht, indem Sie die MEM-SQLite-Datenbank bearbeiten, Passwort-Hashes manuell ersetzen oder Identity-Felder zwischen Konten kopieren.

## Wenn auch der Authenticator nicht verfügbar ist

Die Host-Passwortwiederherstellung behält die aktuelle MFA-Konfiguration bei. TOTP wird dadurch weder entfernt noch ersetzt.

Wenn das Passwort bekannt, der Authenticator aber nicht verfügbar ist, verwenden Sie in der MFA-Stufe einen ungenutzten MEM-Wiederherstellungscode. Wenn weder Passwortautorität noch MFA-Wiederherstellungsautorität verfügbar sind, stoppen Sie und verwenden Sie den unterstützten Wiederherstellungs-/Supportweg, statt Authentifizierungsdaten direkt zu verändern.

## Verwandte Hinweise

Siehe [Sicherheits- und Zugriffseinstellungen](sicherheits-und-zugriffseinstellungen.md) für das umfassendere Modell zu Authentifizierung, Rollen, Sitzungen und Step-up für Hochrisikoaktionen.

---

# Change or recover an operator password

Source: `docs/operations/operator-password-change-and-recovery.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Change or recover an operator password

MEM separates a normal self-service password change from host-authoritative recovery. Use the browser when you still know your current password. Use the host console only when a Platform Owner has forgotten the password and cannot complete the normal change flow.

If you are locked out of the Control Plane and cannot open its built-in Documentation page, open the public documentation on **messageeasymode.com** and search for **Change or recover an operator password**. The public documentation is synchronized from the same MEM documentation source.

## Change a password while signed in

For your own named operator account:

1. Open the account menu or **Operator access**.
2. Choose **Change password** for the current operator.
3. Verify your identity with your **current password** and a current **authenticator-app code**.
4. Enter the new password twice. Use **Show password** when needed to verify complex characters before submitting.
5. Choose **Change password**.

The new password must satisfy the password policy shown by MEM. In MEM 0.2.x the form requires at least 14 characters including uppercase, lowercase, a number, a symbol, and at least four unique characters.

After a successful change, MEM:

- invalidates existing MEM browser sessions and CLI/device sessions for that operator;
- returns the current browser to sign-in;
- rejects the old password;
- preserves the existing TOTP authenticator configuration;
- preserves unused recovery codes;
- preserves roles and normal account state.

Sign in again with the new password and the existing authenticator.

A recovery code cannot replace the current password for this operation and cannot satisfy high-risk step-up.

## Recover a forgotten Platform Owner password

MEM 0.2.x does not provide an email reset link, security-question flow, or Web administrator button for a forgotten operator password.

For a **Platform Owner** who has forgotten the password, use host-authoritative recovery from the MEM server console.

Run this on the MEM host:

```bash
sudo docker exec -it mem-control-plane \
  dotnet Api.dll operator reset-password PLATFORM_OWNER_USERNAME
```

The command is interactive. It asks you to type the selected Platform Owner username to confirm the target, then prompts for the replacement password twice without echoing it to the terminal.

Do **not** place the new password in the command line, shell history, a script argument, or a support report.

On success, the command reports that the password was reset. Existing TOTP configuration, unused recovery codes, roles, and platform data are preserved, while existing MEM sessions for that account are revoked.

Then:

1. Return to the MEM sign-in page.
2. Sign in with the replacement password.
3. Complete MFA with the existing authenticator code, or use an existing recovery code at the MFA stage when appropriate.

## Current recovery boundary

The host command above is deliberately a **Platform Owner recovery** command in MEM 0.2.x. It is not a general Web or console mechanism for one operator to reset another named operator's password.

Do not attempt to work around that boundary by editing the MEM SQLite database, replacing password hashes manually, or copying Identity fields between accounts.

## If the authenticator is also unavailable

Host password recovery preserves the current MFA configuration. It does not remove or replace TOTP.

If the password is known but the authenticator is unavailable, use an unused MEM recovery code at the MFA stage. If both password authority and all MFA recovery authority are unavailable, stop and use the supported recovery/support procedure rather than modifying authentication data directly.

## Related guidance

See [Security and access settings](security-and-access-settings.md) for the wider authentication, role, session, and high-risk step-up model.

---

# Use Backup Catalog commands

Source: `docs/cli/backup-commands.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Use Backup Catalog commands

The CLI operates on existing Backup Catalog material. It does not currently create a new backup capture; create backups through the Control Plane.

## Keep the identities separate

```text
validationId       retained uploaded-ZIP provenance
catalogEntryId     durable Backup Catalog source
restoreSessionId   Restore Workspace identity
```

A validated portable ZIP must materialise into a catalog entry before it can become a restore source.

## List and inspect catalog entries

```bash
mem backups list --profile home
mem backups list --profile home --json | jq

mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

`lifecycle` is the safest check before deletion because it reports whether the source can be removed and whether an active Restore Workspace blocks the action.

`mem backups list --stack ...` is not supported. List the catalog, then inspect the selected entry.

## Export a portable backup

```bash
mem backups export <catalog-entry-id> \
  --out ./mem-backup.zip \
  --profile home \
  --json | jq
```

MEM creates a server-side portable export, downloads it, creates the local parent directory when needed, and writes the requested path. Choose the path carefully because an existing file can be replaced.

Verify the reported output path, bytes written, warnings, source stack, and catalog ID before moving the ZIP to external storage.

## Import a portable ZIP

```bash
mem backups import ./mem-backup.zip --profile home --json | jq
```

Exit code `0` requires both a valid intake result and a materialised catalog entry ID. A validation record without a catalog entry is not restore-ready.

Inspect retained upload provenance when needed:

```bash
mem backups uploads inspect <validation-id> --profile home --json | jq
```

Delete only the retained uploaded ZIP archive:

```bash
mem backups uploads delete <validation-id> --yes --profile home --json | jq
```

This does not delete the materialised catalog payload or a Restore Workspace.

## Permanently delete a catalog entry

```bash
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
mem backups delete <catalog-entry-id> --yes --profile home --json | jq
```

Deletion is irreversible and requires `--yes`. The CLI reads lifecycle state first and refuses the server mutation when an active restore session blocks deletion. The result reports payload, original archive, portable exports, and detached terminal restore history separately.

The server may require recent step-up for deletion. The current CLI fails closed on HTTP 403 and does not accept passwords, TOTP codes, recovery codes, bearer tokens, or device credentials as options.

## Verify success

After import, export, or deletion:

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem restores list --profile home --json | jq
```

## Related documentation

- [Back up and restore chat servers](../backups-and-restores/index.md)
- [Restore commands](restore-commands.md)
- [CLI security model](security-model.md)

---

# Befehle für den Sicherungskatalog verwenden

Source: `docs/de/cli/backup-befehle.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

# Befehle für den Sicherungskatalog verwenden

Die CLI arbeitet mit vorhandenem Material im Sicherungskatalog. Sie erstellt derzeit keine neue Sicherungserfassung; erstellen Sie Sicherungen in der Control Plane.

## Identitäten trennen

```text
validationId       Herkunft eines aufbewahrten Upload-ZIPs
catalogEntryId     dauerhafte Quelle im Sicherungskatalog
restoreSessionId   Identität des Wiederherstellungsarbeitsbereichs
```

Ein validiertes portables ZIP muss zu einem Katalogeintrag materialisiert werden, bevor es Restore-Quelle ist.

## Katalogeinträge auflisten und prüfen

```bash
mem backups list --profile home
mem backups list --profile home --json | jq

mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

`lifecycle` ist die sicherste Prüfung vor dem Löschen. Sie zeigt, ob die Quelle entfernt werden darf und ob ein aktiver Wiederherstellungsarbeitsbereich blockiert.

`mem backups list --stack ...` wird nicht unterstützt. Listen Sie den Katalog auf und prüfen Sie dann den gewählten Eintrag.

## Portables Backup exportieren

```bash
mem backups export <catalog-entry-id> \
  --out ./mem-backup.zip \
  --profile home \
  --json | jq
```

MEM erstellt serverseitig einen portablen Export, lädt ihn herunter, erstellt bei Bedarf das lokale Elternverzeichnis und schreibt den gewünschten Pfad. Wählen Sie ihn sorgfältig; eine bestehende Datei kann ersetzt werden.

Prüfen Sie Ausgabepfad, geschriebene Bytes, Hinweise, Quell-Stack und Katalog-ID, bevor Sie das ZIP extern ablegen.

## Portables ZIP importieren

```bash
mem backups import ./mem-backup.zip --profile home --json | jq
```

Exit-Code `0` verlangt sowohl gültige Aufnahme als auch eine materialisierte Katalog-ID. Ein Validierungsdatensatz ohne Katalogeintrag ist nicht restore-bereit.

Upload-Herkunft prüfen:

```bash
mem backups uploads inspect <validation-id> --profile home --json | jq
```

Nur das aufbewahrte Upload-ZIP löschen:

```bash
mem backups uploads delete <validation-id> --yes --profile home --json | jq
```

Dies löscht weder den materialisierten Katalog-Payload noch einen Wiederherstellungsarbeitsbereich.

## Katalogeintrag dauerhaft löschen

```bash
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
mem backups delete <catalog-entry-id> --yes --profile home --json | jq
```

Das Löschen ist irreversibel und benötigt `--yes`. Die CLI liest zuerst den Lebenszyklus und verweigert die Servermutation, wenn eine aktive Restore-Sitzung blockiert. Das Ergebnis weist Payload, Originalarchiv, portable Exporte und getrennte terminale Restore-Historie separat aus.

Der Server kann aktuelle Step-up-Bestätigung verlangen. Die CLI schlägt bei HTTP 403 geschlossen fehl und akzeptiert keine Passwörter, TOTP-Codes, Wiederherstellungscodes, Bearer-Tokens oder Geräte-Credentials als Optionen.

## Erfolg prüfen

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem restores list --profile home --json | jq
```

## Verwandte Dokumentation

- [Chatserver sichern und wiederherstellen](../backups-und-wiederherstellen/index.md)
- [Restore-Befehle](restore-befehle.md)
- [CLI-Sicherheitsmodell](sicherheitsmodell.md)

---

# Use Restore Workspace commands

Source: `docs/cli/restore-commands.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Use Restore Workspace commands

Restore commands are catalog-first and session-based. A restore source is identified by `catalogEntryId`; all later work uses the durable `restoreSessionId`.

## List restore sessions

```bash
mem restores list --profile home
mem restores list --page 1 --page-size 25 --profile home --json | jq
```

Available filters include:

```bash
mem restores list --search <text> --profile home --json | jq
mem restores list --status active --profile home --json | jq
mem restores list --target-stack demo --profile home --json | jq
mem restores list --sort-by updatedAtUtc --sort-direction desc --profile home --json | jq
```

The server validates supported filter and sort values. `--page` and `--page-size` must be positive integers.

## Create or resume a workspace

```bash
mem restores create <catalog-entry-id> --profile home --json | jq
```

The server may return an existing active Restore Workspace for the same source. Record the returned restore session ID; do not substitute the catalog ID or upload validation ID.

## Inspect state and evidence

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Logs support page, page size, severity, stage, and text filters. `support-report` reads the report already associated with the workspace; it does not create one implicitly.

Terminal restore history remains inspectable even if its source catalog item was later deleted. The source is presented as a deleted backup rather than fabricated as available.

## Run a private test

```bash
mem restores private-test <restore-session-id> --profile home --json | jq
```

A successful private test returns `ready`. It creates isolated staging, not a public production stack.

When evidence shows retained staging can be removed:

```bash
mem restores private-test destroy <restore-session-id> \
  --yes \
  --profile home \
  --json | jq
```

This removes only the private-test containers, network, and workspace. It retains the Backup Catalog source, production stack, and durable restore evidence.

## Preflight Standard Recreate

```bash
mem restores recreate preflight <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --matrix-host <matrix-host> \
  --profile home \
  --json | jq
```

`--matrix-host` and `--requested-domain-id` are optional. Review every check, target claim, hostname conflict, and warning. Do not execute until the preflight result is ready and the target values are deliberate.

## Execute Standard Recreate

```bash
mem restores recreate execute <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --execute-production-recreate \
  --acknowledge-creates-real-stack \
  --acknowledge-mutates-production-postgres \
  --acknowledge-mutates-npm-routes \
  --acknowledge-no-automatic-rollback \
  --profile home \
  --json | jq
```

Use the same target values reviewed during preflight. When you supplied `--matrix-host` or `--requested-domain-id` during preflight, supply them again during execution.

This action can create a real stack, import production PostgreSQL data, start production containers, and change Nginx Proxy Manager routes. There is no automatic rollback. The four acknowledgement flags are mandatory and are not decorative.

Server role and step-up policy remains authoritative. On HTTP 403 the CLI fails closed; complete the protected workflow in the browser or wait for an approved CLI step-up design rather than passing secrets through the shell.

## Cancel or complete handover

```bash
mem restores cancel <restore-session-id> --yes --profile home --json | jq
mem restores handover complete <restore-session-id> --yes --profile home --json | jq
```

Cancellation releases temporary claims only when no queued or running operation could be left half-mutated. It retains the catalog source and audit history. Handover completion marks a verified workspace complete and does not delete the record.

## Related documentation

- [Start and use a Restore Workspace](../backups-and-restores/restore-workspace.md)
- [Recover with Standard Recreate](../backups-and-restores/standard-recreate.md)
- [JSON output and scripting](json-and-scripting.md)

---

# Befehle für Wiederherstellungsarbeitsbereiche verwenden

Source: `docs/de/cli/restore-befehle.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

# Befehle für Wiederherstellungsarbeitsbereiche verwenden

Restore-Befehle sind katalogzentriert und sitzungsbasiert. Die Quelle trägt `catalogEntryId`; alle späteren Arbeiten verwenden `restoreSessionId`.

## Restore-Sitzungen auflisten

```bash
mem restores list --profile home
mem restores list --page 1 --page-size 25 --profile home --json | jq
```

Filter:

```bash
mem restores list --search <text> --profile home --json | jq
mem restores list --status active --profile home --json | jq
mem restores list --target-stack demo --profile home --json | jq
mem restores list --sort-by updatedAtUtc --sort-direction desc --profile home --json | jq
```

`--page` und `--page-size` müssen positive Ganzzahlen sein. Der Server prüft unterstützte Filter und Sortierung.

## Arbeitsbereich erstellen oder fortsetzen

```bash
mem restores create <catalog-entry-id> --profile home --json | jq
```

Der Server kann den vorhandenen aktiven Arbeitsbereich derselben Quelle zurückgeben. Notieren Sie die Restore-Sitzungs-ID; ersetzen Sie sie nicht durch Katalog- oder Validierungs-ID.

## Zustand und Nachweise prüfen

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Logs unterstützen Seite, Seitengröße, Schweregrad, Stufe und Textsuche. `support-report` liest den vorhandenen Bericht und erzeugt keinen implizit.

Terminale Restore-Historie bleibt auch nach späterer Löschung des Quellkatalogeintrags lesbar. Die Quelle wird ehrlich als gelöschtes Backup dargestellt.

## Privaten Test ausführen

```bash
mem restores private-test <restore-session-id> --profile home --json | jq
```

Erfolg liefert `ready`. Der private Test erstellt isoliertes Staging, keinen öffentlichen Produktions-Stack.

Wenn Nachweise die Löschung des aufbewahrten Stagings erlauben:

```bash
mem restores private-test destroy <restore-session-id> \
  --yes \
  --profile home \
  --json | jq
```

Dies entfernt nur Container, Netzwerk und Arbeitsbereich des privaten Tests. Katalogquelle, Produktions-Stack und dauerhafte Restore-Nachweise bleiben erhalten.

## Standard-Neuerstellung vorprüfen

```bash
mem restores recreate preflight <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --matrix-host <matrix-host> \
  --profile home \
  --json | jq
```

`--matrix-host` und `--requested-domain-id` sind optional. Prüfen Sie jeden Check, jede Zielreservierung, jeden Hostnamenkonflikt und Hinweis.

## Standard-Neuerstellung ausführen

```bash
mem restores recreate execute <restore-session-id> \
  --target-stack <slug> \
  --element-host <element-host> \
  --execute-production-recreate \
  --acknowledge-creates-real-stack \
  --acknowledge-mutates-production-postgres \
  --acknowledge-mutates-npm-routes \
  --acknowledge-no-automatic-rollback \
  --profile home \
  --json | jq
```

Verwenden Sie dieselben Zielwerte wie in der Vorprüfung. Wenn dort `--matrix-host` oder `--requested-domain-id` angegeben wurde, geben Sie diese bei der Ausführung erneut an.

Die Aktion kann einen echten Stack erstellen, Produktions-PostgreSQL importieren, Produktionscontainer starten und Nginx-Proxy-Manager-Routen ändern. Es gibt kein automatisches Rollback. Alle vier Bestätigungsflags sind verpflichtend.

Serverrollen und Step-up-Policy bleiben maßgeblich. Bei HTTP 403 schlägt die CLI geschlossen fehl; verwenden Sie den geschützten Browserablauf statt Secrets in der Shell.

## Abbrechen oder Übergabe abschließen

```bash
mem restores cancel <restore-session-id> --yes --profile home --json | jq
mem restores handover complete <restore-session-id> --yes --profile home --json | jq
```

Abbruch gibt temporäre Reservierungen nur frei, wenn kein laufender Vorgang halb mutiert bleiben kann. Katalogquelle und Auditverlauf bleiben. Die Übergabe markiert einen verifizierten Arbeitsbereich als abgeschlossen und löscht den Datensatz nicht.

## Verwandte Dokumentation

- [Wiederherstellungsarbeitsbereich starten](../backups-und-wiederherstellen/wiederherstellungsarbeitsbereich.md)
- [Standard-Neuerstellung](../backups-und-wiederherstellen/standard-neuerstellung.md)
- [JSON-Ausgabe und Skripting](json-und-skripting.md)

---

# Use JSON output and scripting

Source: `docs/cli/json-and-scripting.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Use JSON output and scripting

Use `--json` when another program, a support workflow, or an AI-assisted diagnostic process needs stable structure.

```bash
mem host status --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

## Stable machine language

JSON property names, status values, error codes, IDs, and command options remain English even when human output is German:

```bash
mem host status --profile home --language de --json | jq
```

Do not parse human-readable output in automation. It may be localized or reformatted.

## Preserve the exit code

A pipeline can hide the CLI exit status. With Bash and `jq`, use `pipefail`:

```bash
set -o pipefail
mem host status --profile home --json | jq
status=$?
printf 'mem status: %s\n' "$status"
```

Current convention:

| Exit code | Meaning |
|---:|---|
| `0` | Requested result met the command's success contract |
| `1` | Usage, local configuration, profile, login, secret-store, or authorization-input failure |
| `2` | The request ran, but the operational result was not ready, healthy, found, valid, or completed |

Read the JSON `status`, `error`, warnings, and details as well as the process exit code.

## Interactive exception

`mem login --device` does not support `--json`. It prints a browser URL and short code, waits, and stores one credential. Automation must not scrape that interactive output or attempt to approve devices automatically.

## Minimal safe evidence bundle

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

Add only the affected resource:

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Keep log pages bounded. Prefer the redacted support report when it contains the needed evidence.

## Common safe errors

```text
cli_profile_required
cli_device_login_required
cli_secret_store_unavailable
cli_device_credential_invalid
agent_secret_retired
installer_token_retired
cli_secret_material_rejected
cli_recovery_arm_not_available
```

The exact error is safe to share. Raw transport response bodies and exception text are deliberately not exposed.

## Secret-handling rules

Never put these in command arguments, environment variables, JSON files, stdin, logs, tickets, or prompts:

- passwords or TOTP values;
- recovery codes;
- CLI device credentials or bearer tokens;
- browser cookies;
- Matrix signing keys or private keys;
- raw database dumps or backup payloads.

MEM explicitly rejects `--password`, `--totp`, `--totp-code`, `--recovery-code`, `--device-credential`, and `--bearer-token`.

## Related documentation

- [CLI security model](security-model.md)
- [CLI troubleshooting](troubleshooting.md)
- [Restore Workspace commands](restore-commands.md)

---

# JSON-Ausgabe und Skripting verwenden

Source: `docs/de/cli/json-und-skripting.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

# JSON-Ausgabe und Skripting verwenden

Verwenden Sie `--json`, wenn Programme, Supportabläufe oder KI-gestützte Diagnose stabile Struktur benötigen.

```bash
mem host status --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

## Stabile Maschinensprache

JSON-Feldnamen, Statuswerte, Fehlercodes, IDs und Optionen bleiben Englisch, auch bei deutscher lesbarer Ausgabe:

```bash
mem host status --profile home --language de --json | jq
```

Parsen Sie keine menschenlesbare Ausgabe in Automatisierung.

## Exit-Code bewahren

Eine Pipeline kann den CLI-Status verdecken. Mit Bash und `jq`:

```bash
set -o pipefail
mem host status --profile home --json | jq
status=$?
printf 'mem status: %s\n' "$status"
```

Aktuelle Konvention:

| Exit-Code | Bedeutung |
|---:|---|
| `0` | Ergebnis erfüllt den Erfolgsvertrag |
| `1` | Nutzung, lokale Konfiguration, Profil, Login, Secret Store oder Autorisierungseingabe fehlerhaft |
| `2` | Anfrage lief, aber Betriebsergebnis war nicht bereit, gesund, gefunden, gültig oder abgeschlossen |

Lesen Sie JSON-`status`, `error`, Hinweise und Details zusammen mit dem Prozessstatus.

## Interaktive Ausnahme

`mem login --device` unterstützt kein `--json`. Automatisierung darf die interaktive Ausgabe nicht scrapen oder Geräte automatisch autorisieren.

## Minimales sicheres Nachweisbündel

```bash
mem --version
mem account show --profile home --json | jq
mem host status --profile home --json | jq
mem stack list --profile home --json | jq
mem backups list --profile home --json | jq
mem restores list --profile home --json | jq
```

Nur die betroffene Ressource ergänzen:

```bash
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

Logseiten begrenzen. Den redigierten Supportbericht bevorzugen, wenn er die nötigen Nachweise enthält.

## Häufige sichere Fehlercodes

```text
cli_profile_required
cli_device_login_required
cli_secret_store_unavailable
cli_device_credential_invalid
agent_secret_retired
installer_token_retired
cli_secret_material_rejected
cli_recovery_arm_not_available
```

Der genaue Fehlercode ist teilbar. Rohe Transportantworten und Exception-Texte werden bewusst nicht ausgegeben.

## Regeln für Secrets

Niemals in Argumente, Umgebungsvariablen, JSON-Dateien, stdin, Logs, Tickets oder Prompts schreiben:

- Passwörter oder TOTP-Werte;
- Wiederherstellungscodes;
- CLI-Geräte-Credentials oder Bearer-Tokens;
- Browser-Cookies;
- Matrix-Signatur- oder private Schlüssel;
- rohe Datenbankdumps oder Backup-Payloads.

MEM lehnt `--password`, `--totp`, `--totp-code`, `--recovery-code`, `--device-credential` und `--bearer-token` explizit ab.

## Verwandte Dokumentation

- [CLI-Sicherheitsmodell](sicherheitsmodell.md)
- [CLI-Fehlerbehebung](fehlerbehebung.md)
- [Restore-Befehle](restore-befehle.md)

---

# Troubleshoot the MEM CLI

Source: `docs/cli/troubleshooting.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Troubleshoot the MEM CLI

Work from the local command outward. Do not begin with destructive backup or restore actions.

## 1. Verify the installed binary

```bash
command -v mem
readlink -f "$(command -v mem)"
mem --version
mem --help
```

If the command is absent, remember that current source contains installer integration but leaves it disabled by default unless release packaging supplies and enables the binary. Follow [Install the MEM CLI](install.md).

If an old binary is cached:

```bash
hash -r
```

## 2. Verify local configuration

```bash
mem profile list --json | jq
mem config get default-profile --json | jq
mem config get language --json | jq
```

Typical fixes:

```bash
mem profile create home --server https://mem.example.internal
mem profile select home
```

Profile errors include invalid names, non-HTTPS remote URLs, a missing profile, malformed JSON, unsupported schema, unsafe symbolic-link paths, and unreadable configuration.

## 3. Verify the non-root keyring

```bash
command -v secret-tool
secret-tool --help
```

Install the client when missing:

```bash
sudo apt install libsecret-tools
```

Then return to the normal user. Do not run login with `sudo`. A headless SSH shell may have `secret-tool` installed but no unlocked Secret Service session. MEM intentionally fails instead of writing plaintext credentials.

## 4. Verify account state

```bash
mem account show --profile home --json | jq
```

When signed out or rejected:

```bash
mem logout --profile home
mem login --device --profile home
mem account show --profile home
```

If the Control Plane was reinstalled, old credentials are installation-bound and must be replaced.

## 5. Verify private Control Plane reachability

```bash
mem host status --profile home --json | jq
```

Check the profile URL, private DNS, VPN or management network, TLS certificate, and reverse-proxy reachability. Do not change the profile to a public Matrix or Element URL.

A 401 means the server rejected the CLI device session. A 403 means the role or recent-verification policy refused the action. The current CLI does not accept password or TOTP step-up material through the shell.

## 6. Narrow to the affected resource

Chat server:

```bash
mem stack list --profile home --json | jq
mem stack inspect <slug-or-id> --profile home --json | jq
mem stack doctor <slug-or-id> --profile home --json | jq
mem stack operations <slug-or-id> --profile home --json | jq
```

Backup Catalog:

```bash
mem backups list --profile home --json | jq
mem backups inspect <catalog-entry-id> --profile home --json | jq
mem backups lifecycle <catalog-entry-id> --profile home --json | jq
```

Restore Workspace:

```bash
mem restores inspect <restore-session-id> --profile home --json | jq
mem restores evidence <restore-session-id> --profile home --json | jq
mem restores logs <restore-session-id> --page-size 50 --profile home --json | jq
mem restores support-report <restore-session-id> --profile home --json | jq
```

## Common symptoms

### `cli_profile_required`

Operational credentials are keyed by a named profile and server. Create/select a profile; `--server` alone is not enough.

### `cli_secret_store_unavailable`

The keyring is absent, locked, or not connected to the current shell session. Fix Secret Service for the invoking non-root user.

### `cli_device_login_required`

No credential exists for the selected profile. Run device login.

### `cli_device_credential_invalid`

Remove the invalid local session with `mem logout`, then sign in again.

### `agent_secret_retired` or `installer_token_retired`

Remove the retired option from the command. Do not replace it with an environment variable. Use named-device login.

### Exit code `2`

The command syntax and local authority were accepted, but the operational result did not meet success criteria. Inspect the returned status, warnings, checks, and detail.

### UI unavailable but CLI works

Keep to read-only evidence first. The CLI does not currently expose global Diagnostics, Seq, stack lifecycle, backup creation, or migration-session commands. Do not invent substitutes or mutate Docker outside the documented recovery boundary.

## Related documentation

- [JSON output and scripting](json-and-scripting.md)
- [CLI security model](security-model.md)
- [Inspect the host and chat servers](host-and-stack-commands.md)

---

# MEM CLI fehlerbeheben

Source: `docs/de/cli/fehlerbehebung.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

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

---

# Start and use a Restore Workspace

Source: `docs/backups-and-restores/restore-workspace.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Start and use a Restore Workspace

A Restore Workspace is the durable control surface for one catalog-backed recovery attempt.

## Start or resume

1. Open **Backups** and select an available catalog entry.
2. Review provenance, payload state, integrity, advisories, and Matrix identity.
3. Choose **Restore**.
4. When an active workspace already exists, the same action becomes **Resume**.

MEM redirects to `/restores/<restoreSessionId>`. The restore-session ID remains stable across refreshes and is the reference to keep in incident notes.

## Standard workflow

The **Standard** tab presents five operator steps:

1. **Backup ready** — confirms the catalog source is usable.
2. **Private test** — optional isolated database import and Synapse health proof.
3. **Choose and create restored server** — preflight target details and execute Standard Recreate.
4. **Check restored server** — run fresh public Matrix, Element, route, and connectivity checks.
5. **Complete and hand over** — acknowledge the verified service and retain the workspace as an audit record.

The current actionable, failed, running, or latest completed step opens automatically when the workspace is revisited.

## Workspace tabs

- **Standard** — guided recovery stages.
- **Activity** — durable stage and event timeline.
- **Logs** — structured, filterable restore events.
- **Evidence** — curated successes, warnings, and failures.
- **Configuration** — safe source, target, claim, and operation facts.
- **Advanced** — exceptional tools; unavailable tools show an explicit reason.

## Source and target are separate

The source card identifies the catalog payload. The target is initially unselected. Standard Recreate preserves the Matrix server identity from the backup while allowing a new stack slug and an available Element host.

## Durable evidence

Refreshing the browser does not create a new attempt. Operations, target claims, warnings, errors, evidence, and logs remain associated with the restore-session ID.

When the catalog payload is later permanently deleted, the workspace remains readable for audit and support, but guided actions that need the source are blocked.

> [!IMPORTANT]
> Do not operate a restore from the uploaded-ZIP detail page. Validation and retained archive management are ingestion concerns. All recovery execution begins from the Backup Catalog and continues in the Restore Workspace.

---

# Inspect network and domains

Source: `docs/chat-servers/network-and-domains.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Inspect network and domains

## Outcome

Confirm which public and internal addresses MEM recorded for the stack, then use Doctor when you need a fresh route check.

Open the stack workspace and select **Network & domains**.

## Public naming

For a stack slug such as `family` under `example.org`, the normal creation plan uses:

- `matrix-family.example.org` for the Matrix homeserver;
- `chat-family.example.org` for the Element web client.

These hosts are derived from the selected active platform domain. Matrix identity is tied to the Matrix server name, so hostname changes are not ordinary cosmetic edits.

## What the page shows

For Matrix and Element, MEM can report:

- public domain and HTTPS address;
- Nginx Proxy Manager route ID;
- NPM certificate ID;
- internal container hostname and URL;
- the last recorded verification time.

These values come from the runtime manifest. MEM does not invent a route when the manifest is incomplete.

## Read-only boundary

The page does **not** directly inspect:

- current DNS-provider records;
- certificate expiry;
- the complete live Nginx Proxy Manager configuration;
- reachability from every external network.

Select **Run doctor** to check the current NPM service, route configuration, internal HTTP, and public HTTPS path. When external users still fail, verify authoritative DNS, firewall and NAT rules, and certificate status outside this read-only projection.

## Avoid unmanaged changes

Do not casually change Matrix or Element hosts directly in NPM or Synapse. An out-of-band route, certificate, or server-name change can cause manifest drift, break Element discovery, or change Matrix identity expectations.

Use the dedicated migration or recovery workflow when the intended outcome is a new hostname or adopted server identity.

---

# Understand the CLI security model

Source: `docs/cli/security-model.md`
Locale: en
Section: CLI and automation
Status: supported
Applies to: 0.2.x

# Understand the CLI security model

## Normal authority path

```text
named MEM operator
→ existing browser sign-in and TOTP
→ reviewed CLI device approval
→ server-issued opaque device credential
→ OS Secret Service storage
→ server role and capability policy
→ server audit and step-up policy
```

This authority is the same whether `mem` runs on the host, through SSH, or on a private workstation. Localhost is not a role or authorization bypass.

## Local configuration versus credentials

The profile file contains only non-secret names, server URLs, language preferences, and default selection. The device credential is stored separately through `secret-tool` and keyed by normalized profile plus server URL.

The CLI probes secure storage before starting an authorization. It never falls back to:

- plaintext profile or config files;
- environment variables;
- command arguments;
- stdin;
- shell history;
- copied browser cookies.

## Server-owned session controls

The server binds a CLI device authorization to the current completed MEM installation. Reinstalling or replacing that installation invalidates an old credential even if the URL remains the same.

Current server defaults are:

- pending approval: 10 minutes;
- idle session lifetime: 8 hours;
- absolute session lifetime: 7 days.

These values are server-controlled. `mem account show` and successful login output report the actual expiry values.

## Step-up boundary

The server decides whether a high-risk operation requires recent identity verification. The CLI neither creates nor bypasses that grant.

The current CLI does not implement a secret-safe interactive password-and-TOTP retry. When the server refuses an action with HTTP 403, the CLI fails closed. Use the approved browser workflow for the protected action.

These command options are rejected:

```text
--password
--totp
--totp-code
--recovery-code
--device-credential
--bearer-token
```

## Retired authority

The following must not appear in new scripts or instructions:

```text
--installer-token
MEM_INSTALLER_TOKEN
--agent-secret
MEM_AGENT_SECRET
X-MEM-Agent-Secret
```

`--installer-token` and `--agent-secret` are explicitly rejected. Ambient `MEM_INSTALLER_TOKEN` is ignored. The old shared-secret authority model must not return.

`--host-agent-url` and `MEM_HOST_AGENT_URL` remain hidden address-compatibility inputs only. They provide no special authority and should be replaced by `--server` and `MEM_SERVER_URL`.

## Reserved host-local recovery

```bash
sudo mem auth arm-recovery
```

This command is reserved for a future local Unix-socket or named-pipe recovery bridge. In the current build it returns `cli_recovery_arm_not_available`, prints no recovery grant, and makes no Control Plane network request. Remote server and profile options are rejected.

Do not treat this reserved refusal as an available recovery workflow.

## Safe support material

Generally safe when needed and reviewed:

- `mem --version` output;
- profile names and redacted server URL;
- account status without credential material;
- bounded JSON status, checks, evidence, and redacted support reports;
- exact safe error codes and process exit codes.

Never share credentials, passwords, TOTP or recovery codes, browser cookies, Secret Service output, signing keys, private keys, raw database dumps, or backup payloads.

## Related documentation

- [Sign in with device login](device-login.md)
- [JSON output and scripting](json-and-scripting.md)
- [CLI troubleshooting](troubleshooting.md)

---

# Wiederherstellungsarbeitsbereich starten und verwenden

Source: `docs/de/backups-und-wiederherstellen/wiederherstellungsarbeitsbereich.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Wiederherstellungsarbeitsbereich starten und verwenden

Ein Wiederherstellungsarbeitsbereich ist die dauerhafte Steuerungsoberfläche für einen kataloggebundenen Wiederherstellungsversuch.

## Starten oder fortsetzen

1. **Sicherungen** öffnen und einen verfügbaren Katalogeintrag auswählen.
2. Herkunft, Payload-Zustand, Integrität, Hinweise und Matrix-Identität prüfen.
3. **Wiederherstellen** wählen.
4. Bei vorhandenem aktiven Arbeitsbereich wird dieselbe Aktion zu **Fortsetzen**.

MEM öffnet `/restores/<restoreSessionId>`. Die ID bleibt über Browseraktualisierungen hinweg stabil und gehört in Vorgangs- oder Incident-Notizen.

## Standardablauf

1. **Sicherung bereit** — Katalogquelle ist nutzbar.
2. **Privater Test** — optionale isolierte Datenbank- und Synapse-Prüfung.
3. **Wiederhergestellten Server wählen und erstellen** — Zieldaten vorprüfen und Standard-Neuerstellung ausführen.
4. **Wiederhergestellten Server prüfen** — öffentliche Matrix-, Element-, Routen- und Konnektivitätsprüfungen.
5. **Abschließen und übergeben** — verifizierten Dienst bestätigen und Auditdatensatz behalten.

Beim erneuten Öffnen wird die aktuelle, fehlgeschlagene, laufende oder zuletzt abgeschlossene Stufe angezeigt.

## Registerkarten

- **Standard** — geführte Wiederherstellung.
- **Aktivität** — dauerhafte Zeitleiste.
- **Logs** — strukturierte, filterbare Ereignisse.
- **Nachweise** — kuratierte Erfolge, Warnungen und Fehler.
- **Konfiguration** — sichere Quellen-, Ziel-, Reservierungs- und Vorgangsdaten.
- **Erweitert** — Sonderwerkzeuge mit expliziten Verfügbarkeitsgründen.

## Quelle und Ziel

Die Quellenkarte identifiziert den Katalog-Payload. Das Ziel ist zunächst nicht ausgewählt. Die Standard-Neuerstellung bewahrt die Matrix-Serveridentität und erlaubt einen neuen Stack-Slug sowie einen verfügbaren Element-Host.

## Dauerhafte Nachweise

Eine Browseraktualisierung erzeugt keinen neuen Versuch. Vorgänge, Zielreservierungen, Warnungen, Fehler, Nachweise und Logs bleiben an die Restore-ID gebunden.

Wird der Katalog-Payload später permanent gelöscht, bleibt der Arbeitsbereich für Audit und Support lesbar; Aktionen mit Quellenbedarf werden jedoch blockiert.

> [!IMPORTANT]
> Führen Sie keine Wiederherstellung von der Upload-Detailseite aus. Aufnahme und Archivverwaltung gehören zum Import. Die Ausführung beginnt im Sicherungskatalog und läuft im Wiederherstellungsarbeitsbereich weiter.

---

# Netzwerk und Domains prüfen

Source: `docs/de/chat-servers/netzwerk-und-domains.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Netzwerk und Domains prüfen

## Ergebnis

Bestätigen Sie die für den Stack erfassten öffentlichen und internen Adressen und verwenden Sie Diagnose für eine aktuelle Routenprüfung.

Öffnen Sie im Stack-Arbeitsbereich **Netzwerk & Domains**.

## Öffentliche Namensgebung

Für den Slug `familie` unter `example.org` verwendet der normale Erstellungsplan:

- `matrix-familie.example.org` für den Matrix-Homeserver;
- `chat-familie.example.org` für den Element-Webclient.

Diese Hosts werden aus der aktiven Plattform-Domain abgeleitet. Die Matrix-Identität ist an den Matrix-Servernamen gebunden. Hostnamenänderungen sind daher keine gewöhnliche kosmetische Bearbeitung.

## Angezeigte Daten

Für Matrix und Element kann MEM melden:

- öffentliche Domain und HTTPS-Adresse;
- Nginx-Proxy-Manager-Routen-ID;
- NPM-Zertifikats-ID;
- interner Container-Hostname und URL;
- zuletzt erfasster Prüfzeitpunkt.

Die Werte stammen aus dem Laufzeitmanifest. Bei unvollständigem Manifest erfindet MEM keine Route.

## Schreibgeschützte Grenze

Die Seite prüft nicht direkt:

- aktuelle DNS-Provider-Einträge;
- Zertifikatsablauf;
- vollständige Live-Konfiguration von Nginx Proxy Manager;
- Erreichbarkeit aus jedem externen Netz.

Führen Sie **Diagnose** aus, um NPM, Routen, internes HTTP und öffentliches HTTPS aktuell zu prüfen. Wenn externe Benutzer weiterhin scheitern, prüfen Sie autoritatives DNS, Firewall- und NAT-Regeln sowie Zertifikatsstatus außerhalb dieser Projektion.

## Nicht verwaltete Änderungen vermeiden

Ändern Sie Matrix- oder Element-Hosts nicht beiläufig direkt in NPM oder Synapse. Eine externe Routen-, Zertifikats- oder Servernamenänderung kann Manifest-Drift erzeugen, Element-Erkennung brechen oder Matrix-Identität verändern.

Verwenden Sie Migration oder Wiederherstellung, wenn eine neue Hostname- oder Serveridentität beabsichtigt ist.

---

# CLI-Sicherheitsmodell verstehen

Source: `docs/de/cli/sicherheitsmodell.md`
Locale: de
Section: CLI und Automatisierung
Status: supported
Applies to: 0.2.x

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

---

# Öffentliche Domain und Zertifikatsplan wählen

Source: `docs/de/installation/domain-und-zertifikat.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Öffentliche Domain und Zertifikatsplan wählen

Die Stufe **Öffentliche Domain** legt Domain und Wildcard-Zertifikatsabsicht für Review fest.

Diese Stufe dient **nur der Validierung und Planung**. Sie veröffentlicht die private Control Plane nicht und erstellt noch keine DNS-Challenge und kein Zertifikat.

## Erforderliche Angaben

- Basisdomain, zum Beispiel `example.org`;
- ACME-Kontaktadresse;
- deSEC-API-Token mit Berechtigung für die DNS-Zone;
- Entscheidung über Let's-Encrypt-Staging oder Produktion.

MEM leitet daraus `*.example.org` ab.

## Was die Validierung jetzt ausführt

MEM:

1. validiert Basisdomain und ACME-E-Mail;
2. führt eine **schreibgeschützte** deSEC-Zonenprüfung aus;
3. speichert den deSEC-Token geschützt serverseitig;
4. bereitet Wildcard-Zertifikat und ACME-Umgebung für Review vor;
5. speichert sichere Planungsnachweise.

Vor Review und **Plattform installieren** führt MEM **nicht** aus:

- `_acme-challenge`-TXT-Einträge erstellen;
- ACME-Bestellung auslösen;
- Zertifikat oder privaten Schlüssel erzeugen oder speichern;
- NPM erstellen oder den ersten Administrator anlegen;
- Zertifikat in NPM importieren;
- öffentliche Ingress-Routen konfigurieren.

## deSEC ist der aktuelle geführte Anbieter

Der geführte DNS-01-Ablauf von MEM 0.2.0 unterstützt deSEC. Behandeln Sie den API-Token als Geheimnis und verwenden Sie nur die für die Zone erforderlichen Berechtigungen.

Andere DNS-Anbieter und beliebige bestehende Proxy-Systeme sind derzeit keine geführten Auswahlmöglichkeiten.

## deSEC-Delegation und DNSSEC vorbereiten

Bevor Sie sich auf die Zertifikatsausstellung durch MEM verlassen, muss die öffentliche DNS-Autorität der Zone korrekt sein:

1. DNS-Zone in deSEC anlegen oder vorbereiten;
2. Domain beim Registrar an die von deSEC angezeigten autoritativen Nameserver delegieren;
3. bei aktiviertem DNSSEC die **von deSEC gelieferten DS-Werte beim Registrar/in der Parent-Zone** veröffentlichen;
4. keinen Apex-DS-Eintrag in der deSEC-Child-Zone als Ersatz für die DS-Delegation beim Registrar anlegen;
5. Nameserver- und DNSSEC-Änderungen vor einer Produktionsausstellung propagieren lassen.

Eine DNS-01-TXT-Challenge kann auf den deSEC-Nameservern korrekt sein, während ACME die Bestellung wegen eines falschen Parent-DNSSEC-DS weiterhin ablehnt. Reparieren Sie dann den Registrar-/Delegationszustand, statt die DNS- oder ACME-Sicherheitsprüfungen von MEM abzuschwächen.

## Nach dem erstmaligen Setup

Der normale nachträgliche Ablauf **Domains → Domain hinzufügen** unterscheidet sich bewusst von dieser Setup-Stufe. Er erstellt nur einen Domain-Registry-Eintrag; er kontaktiert deSEC nicht, stellt kein Zertifikat aus und speichert kein Token für die Ausstellung. Zertifikatsausstellung, Auswahl des aktiven Zertifikats, Bereitschaft der Hauptdomain und automatische Verlängerung werden im Domain-eigenen Operator-Arbeitsbereich verwaltet.

Siehe [Domains, Zertifikate und Verlängerung betreiben](../operations/domains-und-zertifikate.md).

## Staging und Produktion

Verwenden Sie Let's Encrypt Staging, wenn Sie eine neue DNS-Konfiguration zunächst testen möchten. Ein Staging-Zertifikat wird von normalen Browsern nicht als vertrauenswürdig akzeptiert.

Für echte öffentliche Matrix- und Element-Dienste sollte Review ein Let's-Encrypt-Produktions-Wildcard-Zertifikat anzeigen.

## DNS-Propagation und Retry

Autoritative DNS-Server übernehmen Änderungen nicht immer exakt gleichzeitig. Während der DNS-01-Ausstellung wartet MEM darauf, dass die autoritativen Server den erwarteten `_acme-challenge`-Wert zeigen, bevor die ACME-Validierung fortgesetzt wird.

Wenn MEM einen Timeout der autoritativen DNS-Sichtbarkeit meldet:

- behalten Sie Domain und dauerhaften Vorgang bei;
- prüfen Sie die autoritativen DNS-Server, statt die Domain sofort zu löschen und neu anzulegen;
- lassen Sie die Propagation abschließen oder korrigieren Sie Delegation/DNSSEC, falls diese falsch sind;
- verwenden Sie nach Behebung der Ursache den unterstützten **Retry**-Pfad.

Mehrere Minuten für DNS-Bereitschaft, ACME-Validierung, Finalisierung, geschützte Speicherung und NPM-Import können normal sein. Ein Timeout ist ein Fail-Closed-Ergebnis und kein Grund, die autoritative DNS-Prüfung zu umgehen.

## Was erst nach Review geschieht

Erst nach akzeptiertem Review und dem ausdrücklichen Start von **Plattform installieren** beginnt die externe Mutation. Dann erstellt MEM die DNS-01-Challenge, wartet auf DNS-Sichtbarkeit, führt die ACME-Validierung aus, speichert das Zertifikat und importiert oder findet es in NPM.

Wenn die Zertifikatsausstellung fehlschlägt, verwenden Sie dieselbe dauerhafte Installation und Retry, sobald die gemeldete Ursache behoben ist. Bereits erfolgreiche Plattformschritte werden nicht blind wiederholt.

---

# Paket hochladen und alten Server prüfen

Source: `docs/de/migrieren/upload-and-review.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Paket hochladen und alten Server prüfen

Kehren Sie mit dem für diese Aufnahme erstellten verschlüsselten Paket zur MEM-0.2.0-Control-Plane zurück.

## Ergebnis

Das Ziel hat das Paket angenommen und validiert, die Migrationssitzung an den einzelnen Quell-Stack gebunden und Identität, Kompatibilität und Warnungen des alten Servers vor der Konvertierung angezeigt.

## Paket hochladen

Öffnen Sie die passende Migrationsaufnahme im Schritt **Paket erstellen und hochladen** und wählen Sie den Paket-Upload, schließen Sie gegebenenfalls Step-up ab und wählen Sie die Datei `.memmigration.zip.age`.

MEM prüft Aufnahmebindung, Empfängeridentität, Paketstruktur, Archivinhalte, Quellprofil, ausgewählte Stack-Identität und unterstützte Verträge. Entschlüsseltes Arbeitsmaterial bleibt innerhalb der Ziel-Migrationsgrenze.

Ein Paket für eine andere Aufnahme oder ein anderes Ziel muss geschlossen fehlschlagen. Erstellen Sie keine neue Zielauswahl als Umgehung, sondern erzeugen Sie auf der Quelle das korrekte Paket.

## Alten Server prüfen

Unter **Alten Server überprüfen** bestätigen Sie:

- älteres Produkt und Version;
- ausgewählte Stack-Identität;
- Matrix- und Element-Hostnamen;
- Quell- und Paketfingerabdrücke;
- erfassten Konfigurations- und Speicherumfang;
- Kompatibilitätsergebnis;
- Warnungen, Einschränkungen und Betreiberbestätigungen;
- normalen Vorschau-/vereinfachten Pfad oder erweiterten final eingefrorenen Handoff.

Beheben Sie Blocker vor der Konvertierung. Bewahren Sie Paketbericht und Zielvalidierungsnachweis auf.

## Was MEM speichert

Verschlüsseltes Paket, validiertes Quellarchiv, Hashes und sichere Herkunft bleiben gemäß Sitzungslebenszyklus als geschützte Migrationsnachweise auf dem Ziel. Sie erscheinen nicht als normale Sicherungskatalog-Einträge.

Der ausgewählte Quell-Stack ist bereits maßgeblich. Die Aufnahme bietet keine zweite Stack-Auswahl.

## Erfolg prüfen

Upload und Aufnahme passen zusammen, Validierung ist abgeschlossen, die Identität entspricht dem Plan, kein Kompatibilitätsblocker bleibt und die Sitzung kann zu **Vorbereiten und testen** wechseln.

Verändern Sie ein fehlgeschlagenes Paket nicht manuell. Vergleichen Sie Anfrage, Aufnahme-ID, Empfängerfingerabdruck, Paketbericht, Quellbewertung und Übertragungsprüfsumme und erstellen Sie das Paket bei Bedarf neu.

Weiter: [Konvertieren und privaten Test ausführen](private-test.md).

---

# Anforderungen und unterstützte Umgebung

Source: `docs/de/start/requirements.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# Anforderungen und unterstützte Umgebung

MEM führt mehrere Prüfungen aus. Bereiten Sie den Host jedoch nach der **Bootstrap-Mindestgrenze** vor und nicht nur nach den niedrigeren Browser-Preflight-Warnungen.

## Primäres Release-Ziel

Das normale MEM-0.2.0-Releaseziel ist **Ubuntu Server 24.04 LTS auf amd64/x86_64**.

Der Bootstrap kann weitere unterstützte Ubuntu-LTS-Versionen und Architekturen erkennen. Eine bestandene Architekturprüfung beweist jedoch nicht, dass jedes Upstream-Image oder Release-Artefakt für diese Architektur verfügbar ist. Prüfen Sie vor ARM-Einsatz die Release-Capability-Matrix.

## Bootstrap-Mindestgrenze

Der offizielle Bootstrap verwendet derzeit:

- weniger als **2 CPU-Kerne** → Warnung;
- weniger als **3.500 MB RAM** → Abbruch;
- etwa **7.800 MB RAM** → empfohlene Größe;
- weniger als **20.000 MB frei auf `/`** → Abbruch;
- etwa **50.000 MB frei auf `/`** → empfohlene Größe.

Der spätere Browser-Preflight kann niedrigere Warnschwellen für frühe Tests anzeigen. Diese ersetzen weder die Bootstrap-Grenze noch eine sinnvolle Produktionsdimensionierung.

Synapse-Historie, Medien, PostgreSQL, Images, Logs, Diagnosen, Backups, Restores und Migrationsarbeitsbereiche können deutlich mehr Kapazität benötigen.

Hinweise zu Ubuntu-LVM und Festplattenzuweisung finden Sie unter [Ubuntu Server für eine lokale MEM-Installation vorbereiten](../installation/ubuntu-server-on-prem.md).

## Docker

Docker muss installiert und über den Docker-Socket für die Control Plane erreichbar sein. Auf einem frischen Ubuntu-Host kann der Bootstrap die unterstützten Docker-Engine- und Compose-Plugin-Pakete installieren.

Docker-Socket-Zugriff ist hoch privilegiert. Halten Sie die Control Plane in einem vertrauenswürdigen LAN, VPN oder per SSH-Weiterleitung statt sie öffentlich freizugeben.

## Netzwerk und öffentliche Dienste

Für öffentliches Matrix- und Element-HTTPS wird TCP **443** benötigt. TCP **80** ist optional, wenn bewusst eine HTTP-/Weiterleitungs-Topologie verwendet wird; der aktuelle geführte deSEC-DNS-01-Zertifikatspfad benötigt Port 80 nicht für die Zertifikatsausstellung. Produktions-TURN verwendet zusätzlich:

```text
3478/tcp
3478/udp
49160-49200/udp
```

Die MEM Control Plane, die NPM-Verwaltung, PostgreSQL und weitere Verwaltungs-/Datenports sind private Oberflächen und sollten nicht breit aus dem Internet erreichbar sein.

## Domain, DNS und Zertifikate

Bereiten Sie Domain, DNS-Provider-Zugriff und ACME-Kontaktadresse vor. Der aktuelle geführte Pfad verwendet Nginx Proxy Manager und deSEC DNS-01 für Wildcard-Zertifikate.

Bei On-Premises-Betrieb sollte entschieden werden, ob interne Clients Split DNS verwenden sollen, damit öffentliche Dienstnamen im LAN direkt auf die private Serveradresse auflösen.

## Operator-Arbeitsplatz und Backups

Die Browserverwaltung benötigt Zugriff auf die private Control Plane. Unter Linux verwendet die Geräteanmeldung der MEM CLI `secret-tool` und einen funktionierenden Secret-Service-kompatiblen Schlüsselbund für den aufrufenden Nicht-Root-Benutzer.

Bewahren Sie mindestens eine portable Backup-Kopie außerhalb des MEM-Hosts auf. Ein Backup nur auf der ausgefallenen Serverplatte ist keine Disaster-Recovery-Kopie.

---

# Choose the public domain and certificate plan

Source: `docs/installation/domain-and-certificate.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Choose the public domain and certificate plan

The **Public domain** stage determines the domain and wildcard-certificate intent that will appear in Review.

This stage is **validation and planning only**. It does not publish the private MEM Control Plane and it does not yet create DNS challenges or request a certificate.

## Information required

Provide:

- the base domain, such as `example.org`;
- an ACME contact email;
- a deSEC API token with permission for the DNS zone;
- whether to use Let's Encrypt staging during testing.

MEM derives the wildcard name `*.example.org`. Future stack hostnames are created beneath the chosen zone.

## What validation does now

When you validate the public-domain plan, MEM:

1. validates the base domain and ACME email;
2. performs a **read-only** deSEC zone-access probe;
3. stores the deSEC credential protected server-side;
4. prepares the wildcard-certificate and ACME-environment plan;
5. records safe planning evidence for Review.

Before Review and `Install platform`, MEM does **not**:

- create `_acme-challenge` TXT records;
- place an ACME order;
- issue or store a certificate/private key;
- create or bootstrap Nginx Proxy Manager;
- import a certificate into NPM;
- configure public ingress.

The page should report that external mutation is `none` after successful validation.

## deSEC is the current guided provider

MEM 0.2.0's guided DNS-01 workflow supports deSEC. Treat its API token as a secret and use the minimum permissions practical for the zone.

Other DNS providers and arbitrary existing proxy systems are not yet guided choices.

## Prepare deSEC delegation and DNSSEC

Before relying on MEM certificate issuance, make sure the public DNS authority for the zone is already correct:

1. add or prepare the DNS zone in deSEC;
2. delegate the domain at the registrar to the authoritative nameservers shown by deSEC;
3. when DNSSEC is enabled, publish the **DS values supplied by deSEC at the registrar/parent zone**;
4. do not create an apex DS record inside the deSEC child zone as a substitute for the registrar DS delegation;
5. allow nameserver and DNSSEC changes to propagate before starting production issuance.

A DNS-01 TXT challenge can be correct on the deSEC nameservers while ACME still rejects the order if the parent DNSSEC DS is wrong. Repair the registrar/delegation state rather than weakening MEM's DNS or ACME safety checks.

## After first-time Setup

The normal post-install **Domains → Add Domain** workflow is intentionally different from this first-time Setup stage. It creates a Domain registry entry only; it does not contact deSEC, issue a certificate, or store an issuance token. Certificate issuance, active-certificate selection, main-Domain readiness, and automatic renewal are managed from the Domain-owned operator workspace.

See [Operate domains, certificates, and renewal](../operations/domains-and-certificates.md).

## Staging versus production

Use Let's Encrypt staging while proving a new DNS setup if you are uncertain about provider access. A staging certificate is not trusted by normal browsers.

For real public Matrix and Element services, Review should show a Let's Encrypt production wildcard certificate.

## DNS propagation and Retry

Authoritative DNS servers do not always converge at exactly the same moment. During DNS-01 issuance, MEM waits for the authoritative servers to show the expected `_acme-challenge` value before it continues to ACME validation.

If MEM reports an authoritative DNS visibility timeout:

- keep the existing Domain and durable operation;
- check the authoritative DNS servers rather than immediately deleting and recreating the Domain;
- allow propagation to settle or correct delegation/DNSSEC if it is wrong;
- use the supported **Retry** path after the cause is resolved.

Several minutes can be normal for DNS readiness, ACME validation, finalization, protected storage, and NPM import. A timeout is a fail-closed result, not evidence that MEM should skip the authoritative-DNS check.

## What happens after Review

Only after you accept the exact reviewed plan and explicitly choose **Install platform** does MEM begin external mutation. The installation worker then:

1. prepares the managed Docker services;
2. creates the DNS-01 challenge through deSEC;
3. waits for DNS visibility;
4. requests and validates the wildcard certificate through ACME;
5. stores the certificate and private key in protected MEM-managed storage;
6. validates the stored certificate;
7. imports or resolves the certificate in Nginx Proxy Manager;
8. preserves durable evidence for Retry, verification, and support reports.

If certificate issuance fails, use the same durable installation and Retry when the reported cause is corrected. Do not delete successful earlier platform steps merely to restart certificate work.

---

# Upload and review the old server

Source: `docs/migrate/upload-and-review.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Upload and review the old server

Return to the target MEM 0.2.0 Control Plane with the encrypted migration package created for that intake.

## Outcome

The target has accepted and validated the package, bound the Migration Session to its single source stack, and shown the operator the old-server identity, compatibility findings, and warnings before conversion.

## Upload the package

Open the matching migration intake and choose the package upload action. Complete step-up authentication if requested, then select the `.memmigration.zip.age` file.

MEM verifies the intake binding, recipient identity, package structure, archive integrity, source profile, selected-stack identity, and supported contracts. It decrypts working material only inside the target migration boundary.

A package produced for another intake or target must fail closed. Do not create a new target selection to work around a mismatch; return to the source workflow and create the correct package.

## Review the old server

Use **Review the old server** to confirm:

- legacy product and version;
- selected stack identity;
- Matrix and Element hostnames;
- source and package fingerprints;
- captured configuration and storage scope;
- compatibility result;
- warnings, limitations, or operator acknowledgements;
- whether the package represents the normal preview/simplified path or an advanced final-frozen handoff.

Resolve blocking findings before conversion. Preserve the package report and the target validation evidence.

## What MEM stores

The encrypted package, validated source archive, hashes, and safe provenance remain migration evidence in protected target storage according to the session lifecycle. They do not appear as ordinary Backup Catalog entries.

The selected source stack is already authoritative. Intake does not offer a second stack choice.

## Verify success

- The upload belongs to the intended intake.
- Package validation is complete.
- The old-server identity matches the operator’s plan.
- No blocking compatibility issue remains.
- The session can advance to **Prepare and test**.

If validation fails, do not manually alter the package. Compare the request, intake ID, recipient fingerprint, package report, source assessment, and transfer checksum, then recreate the package where necessary.

Next: [Convert and run the private test](private-test.md).

---

# Troubleshooting

Source: `docs/operations/troubleshooting.md`
Locale: en
Section: Operations
Status: supported
Applies to: 0.2.x

# Troubleshooting

Start with MEM's server-owned evidence. Do not begin by deleting containers, changing network membership, or editing generated files.

## Establish runtime truth

Use the Control Plane UI and, when required, the bounded runtime endpoint:

```text
GET /health/runtime
```

Confirm `runtimeMode`, Control Plane identity, API process identity, validation state, and private exposure before diagnosing a mutation.

## Use Diagnostics

For a failed operation, capture:

- operation ID;
- incident ID/event code;
- failed stage;
- current resource identity;
- redacted support report;
- current Docker evidence when available.

An occurrence count can represent multiple events for one failed operation; it does not necessarily mean the operator attempted the action multiple times.

## Control Plane unreachable

Check the supported private administration path and current container health. A healthy container is not permission to expose 8443 publicly.

## Platform service failure

Open the relevant Services page. Use its current readiness/functional evidence before choosing Restart & Verify or Repair. Do not repair simply because an old incident remains in history.

## Stack creation failure

Use the failed stage to narrow the problem. `generate-synapse-config`, route publication, readiness, and public verification are distinct stages. Preserve a partial operation before retrying.

For current production storage and NPM route details, see [Troubleshoot a chat server](../chat-servers/troubleshooting.md).

## Support evidence safety

Never include passwords, TOTP/recovery codes, TURN secrets, signing keys, private keys, unrestricted `.env`/configuration dumps, or raw database exports in support material.

---

# Requirements and supported environment

Source: `docs/start/requirements.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# Requirements and supported environment

MEM performs several checks, but the operator should prepare the host against the **bootstrap admission floor**, not only the lighter browser preflight warnings.

## Primary release target

The normal MEM 0.2.0 release target is **Ubuntu Server 24.04 LTS on amd64/x86_64**.

The bootstrap can recognise other supported Ubuntu LTS versions and architectures, but passing architecture detection does not prove every upstream image or release artifact exists for that architecture. Use the release capability matrix before relying on ARM.

## Bootstrap admission floor

The official bootstrap currently uses:

- fewer than **2 CPU cores** → warning;
- less than **3,500 MB RAM** → hard stop;
- about **7,800 MB RAM** → recommended level;
- less than **20,000 MB free on `/`** → hard stop;
- about **50,000 MB free on `/`** → recommended level.

The later browser preflight may display lower warning thresholds for early evaluation. Those do not replace the bootstrap floor and are not production-sizing guidance.

Synapse history, media, PostgreSQL, images, logs, diagnostics, backups, restores, and migration workspaces can consume substantially more capacity.

For Ubuntu Server LVM and disk-allocation guidance, see [Prepare Ubuntu Server for an on-premises MEM install](../installation/ubuntu-server-on-prem.md).

## Docker

Docker must be installed and reachable by the Control Plane through the Docker socket. The bootstrap can install the supported Docker Engine and Compose plugin packages on a fresh Ubuntu host.

Docker-socket access is highly privileged. Keep the Control Plane on a trusted LAN, VPN, or SSH-forwarded path rather than exposing it publicly.

## Network and public services

Normal public Matrix and Element HTTPS needs TCP **443**. TCP **80** is optional when you deliberately want an HTTP/redirect topology; the current guided deSEC DNS-01 certificate path does not require public port 80 for certificate issuance. Production TURN additionally uses:

```text
3478/tcp
3478/udp
49160-49200/udp
```

The MEM Control Plane, NPM administration, PostgreSQL, and other management/data ports are private surfaces and should not be opened broadly to the Internet.

## Domain, DNS, and certificates

Prepare a domain, DNS provider access, and an ACME contact email. The current guided path uses Nginx Proxy Manager and deSEC DNS-01 for wildcard certificate issuance.

For an on-premises deployment, decide whether internal clients should use split DNS so public service names resolve directly to the private server address on the LAN.

## Operator workstation and backups

Browser administration requires access to the private Control Plane. On Linux, MEM CLI named-device login uses `secret-tool` and a working Secret Service-compatible keyring for the invoking non-root user.

Keep at least one portable backup copy outside the MEM host. A backup stored only on the failed server disk is not disaster recovery.

---

# Run the private restore test

Source: `docs/backups-and-restores/private-test.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Run the private restore test

The private test is optional, but it is the safest way to discover a damaged or incompatible payload before production PostgreSQL, Docker, or public routes are changed.

## Safety boundary

The canonical private test creates disposable recovery infrastructure on an internal Docker network. Its evidence records that:

- the runtime is private-only;
- the Docker network is internal;
- no public routes are created;
- DNS and certificates are not changed;
- production containers and production databases are not touched;
- the recovered database import and Synapse health check can be evaluated independently.

It is not a public preview URL and must not be made one by manually attaching Nginx Proxy Manager routes.

## Run the test

1. Open the Restore Workspace **Standard** tab.
2. Expand **Private test**.
3. Review blockers and choose **Run private test**.
4. Wait for the operation to complete.
5. Review database import, Synapse health, Matrix identity, completion time, Evidence, and Logs.

A successful test proves that the payload could be imported and that the isolated Synapse runtime passed its health check. It does not prove public DNS, certificates, NPM route ownership, federation reachability, Element client behaviour, or live voice/video calls.

## Retained staging runtime

A successful or deliberately retained private test may require explicit destruction. Use **Retire private test** after reviewing the evidence. MEM removes the private containers, internal network, and disposable workspace while retaining the safe historical result in the Restore Workspace.

Do not remove staging containers manually unless the Control Plane is unavailable and you have preserved their staging ID and operation evidence. Manual removal can leave lifecycle history unclear.

## Failure handling

When the test fails:

- read the safe failure summary;
- open **Evidence** and **Logs**;
- record the restore-session ID, operation ID, event code, and staging ID;
- fix source or target prerequisites rather than editing the materialised payload in place;
- rerun only after the reason is understood.

Skipping the private test is permitted because it is optional. Skipping transfers more risk to Standard Recreate and should be an explicit operator decision, not an accidental click-through.

---

# Configure voice and video TURN

Source: `docs/chat-servers/voice-and-video.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Configure voice and video TURN

## Outcome

Inspect whether the stack uses the MEM platform TURN service, then apply a reviewed connect or disconnect operation when required.

Open the stack workspace and select **Voice & video**.

## Why TURN matters

Matrix clients can often connect directly for calls, but users behind restrictive NAT or firewalls may need a relay. MEM exposes the shared platform coturn service to Synapse without displaying its shared secret.

When coturn is ready during stack creation, MEM writes the platform TURN configuration into Synapse. When it is unavailable, the stack can be created without TURN and will show a warning.

## Understand the state

**Connected** means the live Synapse settings match the recorded MEM-managed association.

**Not connected** means no TURN service is configured for the stack.

**External TURN configuration** means Synapse has TURN settings not owned by this MEM platform. MEM will not overwrite or remove them automatically.

**Configuration needs attention** indicates drift between live Synapse configuration, recorded metadata, or the current platform service.

**State unavailable** means MEM could not establish an authoritative state from the active runtime and configuration.

The page also reports platform readiness, Matrix runtime identity, TURN URIs, configuration hash, and recorded metadata. Inspection is read-only and does not place a test call.

## Platform TURN startup supervision

In containerized MEM runtimes, the Control Plane supervises the shared coturn service when the API starts. MEM waits briefly for Docker and the shared gateway network to settle, then inspects the exact owned runtime.

Safe automatic recovery is deliberately narrow. MEM may start the same stopped MEM-owned coturn container and may correct restart-policy-only drift. It then requires exact runtime readiness and a current functional TURN check. If that functional check fails, MEM may perform **one** bounded automatic Restart & Verify attempt. A repeated failure enters cooldown and requires operator review instead of creating an automatic restart loop.

If an operator-requested Coturn install or maintenance operation is already active in the current API process, startup supervision defers to that explicit operation rather than competing with it.

MEM does not silently recreate coturn, pull a new image, rotate the shared secret, replace protected configuration, take over a foreign container, or repair wrong network/mount/command drift. Those states become **Repair required** or **Conflict** with Diagnostics evidence.

Direct Source and Managed Local development intentionally do not receive this protected automatic mutation authority. They can inspect safe runtime/evidence and use the documented Portainer or Docker development recovery path instead.

## Production firewall and NAT

The current production TURN service publishes:

```text
3478/tcp
3478/udp
49160-49200/udp
```

If the MEM server sits behind a firewall/router, public clients need those paths forwarded/allowed to the server. DNS alone does not open ports or create NAT rules.

## Split DNS and local functional checks

On an on-premises LAN, it is often useful for the internal resolver to answer the TURN hostname with the server's private address while public DNS answers with the WAN address. This keeps local platform checks on a direct LAN path rather than making them depend on NAT reflection.

When Coturn is Running and Ready but the allocation check fails, inspect the **Probe-runtime DNS resolution** result on the Coturn service page. If an internal probe unexpectedly resolves `turn.<domain>` to the public WAN address, correct the resolver path before treating the result as a Coturn defect.

A successful local allocation check proves the configured MEM/server path. It does not replace a later real client call test from the network paths you intend users to use.

## Prove that a real call is using TURN relay

A successful Element call does not by itself prove that TURN was used. WebRTC may connect the clients directly and never carry media through Coturn.

For acceptance testing, place a fresh call between clients on genuinely different networks where practical. If `tcpdump` is available on the MEM host and packet capture is acceptable in your environment, observe only the bounded TURN ports during the call:

```bash
sudo tcpdump -ni any \
  '(udp port 3478 or tcp port 3478 or udp portrange 49160-49200)'
```

Interpret the evidence carefully:

- traffic on `3478/tcp` or `3478/udp` shows TURN/STUN negotiation activity;
- sustained UDP traffic in the configured `49160-49200` relay range during the call is strong server-side evidence that Coturn is relaying media;
- browser WebRTC diagnostics showing a selected ICE candidate of type `relay` are useful additional evidence.

Stop the capture after the bounded test. Packet captures can expose client IP addresses and timing information, so review them before sharing. Do not include TURN shared secrets or unrestricted packet captures in ordinary support reports.

## Connect to platform TURN

Select **Connect to platform TURN**. MEM first creates a server-authored review.

The review may:

- configure the owned Synapse TURN block and restart Matrix; or
- adopt an already matching configuration without rewriting Synapse or restarting Matrix.

A configured connection validates the candidate, changes the owned settings atomically, restarts Matrix, and verifies the resulting state. Active calls may be interrupted during restart. Matrix identity, users, rooms, messages, and media remain unchanged.

## Disconnect from platform TURN

Select **Disconnect from platform TURN** only for a verified MEM-managed configuration. MEM reviews the candidate removal, removes only its owned TURN settings, restarts Matrix, and verifies the disconnected state.

Calls may become less reliable for users on restrictive networks.

## Failure and rollback

Candidate rejection occurs before mutation. When a post-mutation step fails, MEM attempts to restore and verify the previous state. A result marked unresolved requires technical recovery before another change.

Do not paste the shared secret into troubleshooting notes. Retain the operation ID, state, configuration hash, and redacted diagnostics instead.

---

# Privaten Wiederherstellungstest ausführen

Source: `docs/de/backups-und-wiederherstellen/privater-test.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Privaten Wiederherstellungstest ausführen

Der private Test ist optional, aber der sicherste Weg, einen beschädigten oder inkompatiblen Payload vor Änderungen an Produktions-PostgreSQL, Docker oder öffentlichen Routen zu erkennen.

## Sicherheitsgrenze

Der Test erstellt wegwerfbare Infrastruktur in einem internen Docker-Netz. Die Nachweise bestätigen:

- nur privaten Betrieb;
- internes Docker-Netz;
- keine öffentlichen Routen;
- keine DNS- oder Zertifikatsänderung;
- keine Berührung von Produktionscontainern oder Produktionsdatenbanken;
- getrennte Prüfung von Datenbankimport und Synapse-Health.

Er ist keine öffentliche Vorschau. Fügen Sie keine manuellen Nginx-Proxy-Manager-Routen hinzu.

## Test ausführen

1. Im Arbeitsbereich **Standard** öffnen.
2. **Privater Test** erweitern.
3. Blocker prüfen und den Test starten.
4. Abschluss abwarten.
5. Datenbankimport, Synapse-Health, Matrix-Identität, Zeitpunkt, Nachweise und Logs prüfen.

Ein erfolgreicher Test beweist Import und Health des isolierten Synapse-Runtimes. Er beweist nicht öffentliche DNS-Auflösung, Zertifikate, NPM-Routen, Föderation, Element-Benutzererlebnis oder reale Sprach-/Videoanrufe.

## Aufbewahrtes Staging

Ein erfolgreicher oder bewusst aufbewahrter Test kann explizite Zerstörung erfordern. Verwenden Sie **Privaten Test entfernen**, nachdem die Nachweise geprüft wurden. MEM entfernt Container, internes Netz und Wegwerf-Arbeitsbereich, behält aber das sichere historische Ergebnis.

Entfernen Sie Staging-Container nicht manuell, außer die Control Plane ist nicht verfügbar und Staging-ID sowie Vorgangsnachweise wurden gesichert.

## Fehlerbehandlung

Bei Fehlern:

- sichere Fehlerzusammenfassung lesen;
- **Nachweise** und **Logs** öffnen;
- Restore-ID, Vorgangs-ID, Ereigniscode und Staging-ID notieren;
- Voraussetzungen korrigieren statt den materialisierten Payload direkt zu bearbeiten;
- erst nach geklärter Ursache erneut ausführen.

Das Überspringen ist zulässig, verlagert aber zusätzliches Risiko in die Standard-Neuerstellung und sollte eine bewusste Betreiberentscheidung sein.

---

# TURN für Sprache und Video konfigurieren

Source: `docs/de/chat-servers/sprache-und-video.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# TURN für Sprache und Video konfigurieren

## Ergebnis

Prüfen Sie, ob der Stack den MEM-Plattform-TURN-Dienst verwendet, und führen Sie bei Bedarf einen geprüften Verbindungs- oder Trennungsvorgang aus.

Öffnen Sie im Stack-Arbeitsbereich **Sprache & Video**.

## Warum TURN wichtig ist

Matrix-Clients können Anrufe häufig direkt verbinden. Benutzer hinter restriktivem NAT oder Firewalls benötigen jedoch möglicherweise ein Relay. MEM stellt Synapse den gemeinsamen Plattform-coturn-Dienst bereit, ohne das gemeinsame Geheimnis anzuzeigen.

Ist coturn während der Stack-Erstellung bereit, schreibt MEM die Plattform-TURN-Konfiguration in Synapse. Andernfalls kann der Stack ohne TURN mit Warnung erstellt werden.

## Zustand verstehen

**Verbunden** bedeutet, dass Live-Synapse-Einstellungen und erfasste MEM-Zuordnung übereinstimmen.

**Nicht verbunden** bedeutet, dass kein TURN-Dienst konfiguriert ist.

**Externe TURN-Konfiguration** bedeutet, dass Synapse nicht von dieser MEM-Plattform verwaltete Einstellungen enthält. MEM überschreibt oder entfernt sie nicht automatisch.

**Konfiguration erfordert Aufmerksamkeit** zeigt Drift zwischen Live-Konfiguration, Metadaten oder Plattformdienst.

**Zustand nicht verfügbar** bedeutet, dass MEM keinen verbindlichen Zustand ermitteln konnte.

Die Seite zeigt außerdem Plattformbereitschaft, Matrix-Laufzeitidentität, TURN-URIs, Konfigurationshash und Metadaten. Die Prüfung ist schreibgeschützt und führt keinen Testanruf aus.

## Startüberwachung des Plattform-TURN-Dienstes

In containerisierten MEM-Laufzeiten überwacht die Control Plane den gemeinsamen coturn-Dienst beim Start der API. MEM wartet kurz, bis sich Docker und das gemeinsame Gateway-Netzwerk stabilisiert haben, und prüft anschließend die exakte eigene Laufzeit.

Die sichere automatische Wiederherstellung ist bewusst eng begrenzt. MEM darf denselben gestoppten MEM-eigenen coturn-Container starten und ausschließlich eine Abweichung der Neustartrichtlinie korrigieren. Anschließend sind exakte Laufzeitbereitschaft und eine aktuelle funktionale TURN-Prüfung erforderlich. Schlägt diese funktionale Prüfung fehl, darf MEM **genau einen** begrenzten automatischen Vorgang **Neu starten & prüfen** ausführen. Ein erneuter Fehler führt in eine Abkühlzeit und erfordert die Prüfung durch den Operator, statt eine automatische Neustartschleife zu erzeugen.

Wenn im aktuellen API-Prozess bereits ein vom Operator angeforderter Coturn-Installations- oder Wartungsvorgang aktiv ist, stellt die Startüberwachung ihre automatische Arbeit zurück, statt mit diesem ausdrücklichen Vorgang zu konkurrieren.

MEM erstellt coturn nicht still neu, lädt kein neues Image herunter, rotiert das gemeinsame Geheimnis nicht, ersetzt keine geschützte Konfiguration, übernimmt keinen fremden Container und repariert keine falsche Netzwerk-, Mount- oder Befehlsabweichung automatisch. Solche Zustände werden mit Diagnostics-Nachweisen als **Reparatur erforderlich** oder **Konflikt** gemeldet.

Direct Source und Managed Local Development erhalten absichtlich keine solche geschützte automatische Mutationsautorität. Sie können sichere Laufzeit- und Nachweisdaten prüfen und stattdessen den dokumentierten Entwicklungs-Wiederherstellungspfad über Portainer oder Docker verwenden.

## Produktions-Firewall und NAT

Der aktuelle Produktions-TURN-Dienst veröffentlicht:

```text
3478/tcp
3478/udp
49160-49200/udp
```

Steht der MEM-Server hinter Firewall/Router, müssen diese Pfade für öffentliche Clients zum Server weitergeleitet bzw. erlaubt sein. DNS allein öffnet keine Ports und erstellt keine NAT-Regeln.

## Split DNS und lokale Funktionsprüfungen

In einem On-Premises-LAN ist es oft sinnvoll, dass der interne Resolver den TURN-Hostnamen mit der privaten Serveradresse beantwortet, während öffentliches DNS die WAN-Adresse liefert. So bleiben lokale Plattformprüfungen auf einem direkten LAN-Pfad und hängen nicht von NAT-Reflection ab.

Wenn Coturn **Running** und **Ready** ist, die Allocation-Prüfung aber fehlschlägt, prüfen Sie auf der Coturn-Dienstseite das Ergebnis **Probe-runtime DNS resolution**. Löst ein interner Probe `turn.<domain>` unerwartet auf die öffentliche WAN-Adresse auf, korrigieren Sie zuerst den Resolverpfad, bevor der Zustand als Coturn-Defekt bewertet wird.

Eine erfolgreiche lokale Allocation-Prüfung beweist den konfigurierten MEM-/Serverpfad. Sie ersetzt keinen späteren echten Client-Anruftest aus den Netzen, die Benutzer tatsächlich verwenden.

## Beweisen, dass ein echter Anruf TURN-Relay verwendet

Ein erfolgreicher Element-Anruf beweist allein nicht, dass TURN verwendet wurde. WebRTC kann die Clients direkt verbinden, ohne Medien über Coturn zu leiten.

Führen Sie für die Abnahme nach Möglichkeit einen neuen Anruf zwischen Clients in tatsächlich unterschiedlichen Netzen durch. Wenn `tcpdump` auf dem MEM-Host verfügbar ist und Paketmitschnitt in Ihrer Umgebung zulässig ist, beobachten Sie während des Anrufs nur die begrenzten TURN-Ports:

```bash
sudo tcpdump -ni any \
  '(udp port 3478 or tcp port 3478 or udp portrange 49160-49200)'
```

Bewerten Sie die Nachweise sorgfältig:

- Verkehr auf `3478/tcp` oder `3478/udp` zeigt TURN-/STUN-Aushandlungsaktivität;
- anhaltender UDP-Verkehr im konfigurierten Relay-Bereich `49160-49200` während des Anrufs ist ein starker serverseitiger Nachweis, dass Coturn Medien weiterleitet;
- Browser-WebRTC-Diagnosen mit einem ausgewählten ICE-Kandidaten vom Typ `relay` sind ein nützlicher zusätzlicher Nachweis.

Beenden Sie den Mitschnitt nach dem begrenzten Test. Paketmitschnitte können Client-IP-Adressen und Zeitinformationen enthalten und sollten vor dem Teilen geprüft werden. TURN-Shared-Secrets oder unbeschränkte Paketmitschnitte gehören nicht in normale Supportberichte.

## Mit Plattform-TURN verbinden

Wählen Sie **Mit Plattform-TURN verbinden**. MEM erstellt zuerst einen serverseitigen Prüfplan.

Der Plan kann:

- den eigenen Synapse-TURN-Block konfigurieren und Matrix neu starten; oder
- eine bereits passende Konfiguration ohne Umschreiben und Neustart übernehmen.

Bei Konfiguration prüft MEM den Kandidaten, ändert die eigenen Einstellungen atomar, startet Matrix neu und verifiziert den Zustand. Aktive Anrufe können unterbrochen werden. Matrix-Identität, Benutzer, Räume, Nachrichten und Medien bleiben unverändert.

## Von Plattform-TURN trennen

Verwenden Sie **Von Plattform-TURN trennen** nur für eine geprüfte MEM-verwaltete Konfiguration. MEM prüft die Entfernung, entfernt nur eigene TURN-Einstellungen, startet Matrix neu und verifiziert den getrennten Zustand.

Anrufe können für Benutzer in restriktiven Netzen unzuverlässiger werden.

## Fehler und Rollback

Eine Kandidatenablehnung erfolgt vor der Änderung. Scheitert ein Schritt danach, versucht MEM den vorherigen Zustand wiederherzustellen und zu prüfen. Ein ungeklärter Zustand benötigt technische Wiederherstellung vor einem weiteren Versuch.

Fügen Sie das gemeinsame Geheimnis nicht in Fehlerberichte ein. Bewahren Sie Vorgangs-ID, Zustand, Konfigurationshash und redigierte Diagnose auf.

---

# Installationsplan prüfen und ausführen

Source: `docs/de/installation/pruefen-und-installieren.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Installationsplan prüfen und ausführen

Review und Installation sind bewusst getrennte Stufen.

## Review vor externer Mutation

Review zeigt den exakten kanonischen Plattformplan mit Domain, Wildcard-Zertifikatsabsicht, Docker-Netzwerk, PostgreSQL, Nginx Proxy Manager, Ports, Support-Werkzeugen und den Aktionen nach Installationsstart.

Geschützte DNS-, PostgreSQL- und NPM-Zugangsdaten gehören nicht in das eingefrorene Review-JSON. Das Speichern des NPM-Administratorpassworts verändert daher den Plan-Fingerprint nicht.

Das Akzeptieren von Review:

- friert Plan und Fingerprint ein;
- dokumentiert die geprüfte Operatorabsicht;
- verändert **noch nicht** Docker, DNS, ACME, Zertifikatsspeicher oder NPM.

## Plattform installieren

Auf der nächsten Seite überschreitet **Plattform installieren** ausdrücklich die externe Mutationsgrenze.

Danach kann der dauerhafte serverseitige Worker unter anderem:

- `mem-gateway` und dauerhafte Volumes erstellen oder prüfen;
- `mem-postgres` starten und Bereitschaft prüfen;
- `mem-npm` starten;
- bei frischem NPM den ersten Administrator sicher initialisieren;
- die DNS-01-Challenge erstellen und das geprüfte Wildcard-Zertifikat anfordern;
- Zertifikat speichern, prüfen und in NPM importieren;
- ausgewählte Support-Werkzeuge starten;
- die Plattform verifizieren;
- jeden Schritt und Versuch dauerhaft protokollieren.

## Warten, Fehler und Retry

Ein Schritt kann `WaitingForUser` melden. Folgen Sie der angezeigten Aktion und setzen Sie dieselbe Installation fort.

Bei einem Fehler lesen Sie die sichere Fehlerursache, verwenden Sie bei Bedarf Diagnostics oder den begrenzten Supportbericht, beheben Sie die konkrete Ursache und verwenden Sie Retry nur, wenn MEM dies erlaubt.

Bereits abgeschlossene Schritte bleiben autoritativ. Löschen Sie nicht wiederholt Container, Volumes, Installationsdatensätze oder Zertifikatszustand.

Seq bleibt optional; Diagnostics muss auch ohne Seq funktionieren.

---

# Konvertieren und privaten Test ausführen

Source: `docs/de/migrieren/private-test.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Konvertieren und privaten Test ausführen

Das Ziel konvertiert das validierte alte Payload in einen migrationsgebundenen Kandidaten, bevor der normale Produktionsserver erstellt wird.

## Ergebnis

Die Konvertierung ist erfolgreich und der Kandidat läuft als private Matrix- und Element-Dienste ohne öffentliche Routen. Der Betreiber bestätigt Startfähigkeit und erwartete Daten.

## Konvertierung ausführen

Starten Sie unter **Vorbereiten und testen** die Konvertierung. MEM verwendet den aktuellen Worker-Vertrag, validiert strukturierte Fortschrittsereignisse und Ausgabe-Hashes und zeichnet einen dauerhaften Versuch auf.

Die Konvertierung kann dauern und läuft serverseitig weiter, wenn Sie die Seite verlassen oder aktualisieren. Kehren Sie zur selben Migrationssitzung zurück und prüfen Sie Fortschritt, Logs, Warnungen und Ergebnis.

Eine fehlgeschlagene Konvertierung erstellt keinen normalen Chatserver. Beheben Sie das gemeldete Problem oder erzeugen Sie ein kompatibles neues Paket, statt Teilergebnisse in einen Stack zu kopieren.

## Grenze des migrationsgebundenen Kandidaten

Ein erfolgreicher Kandidat ist kein Sicherungskatalog-Eintrag, kein Wiederherstellungsarbeitsbereich, keine normale verwaltete Laufzeit und nicht öffentlich erreichbar.

Rohpakete, temporäre Konvertierungsdateien, fehlgeschlagene Kandidaten und nicht angenommene Kandidaten bleiben im Migrationskontext.

## Privaten Test starten

Erstellen Sie die private Staging-Laufzeit. MEM importiert die konvertierte Datenbank, startet private Matrix- und Element-Dienste und prüft sie in einem internen Docker-Netzwerk.

Der private Test darf **keine öffentlichen Matrix- oder Element-Routen** erstellen und keine Produktionshostnamen übernehmen.

Prüfen Sie:

- Datenbankimport und Schema;
- Synapse-Start und Gesundheit;
- Element-Konfiguration und privaten Zugriff;
- erwartete Quellidentität;
- angezeigte repräsentative Benutzer-, Raum-, Ereignis- und Mediennachweise;
- fehlende öffentliche Routenhoheit.

## Fehlerbehandlung

Prüfen Sie bei Fehlern Konvertierungs- und Staging-Logs vor einem neuen Versuch. Fahren Sie nicht mit der Zielerstellung fort, solange die private Laufzeit ungesund oder ihre Identität unsicher ist.

Ein Refresh bricht keine laufende Staging-Operation ab. Verwenden Sie explizite Operationssteuerung, sofern Abbruch unterstützt wird.

## Aufbewahrungsgrenze

Kandidat und Staging bleiben migrationsgebunden. MEM bewahrt sie normalerweise bis Annahme und erster nativer Sicherung auf und versucht danach die Bereinigung. Ein Bereinigungsfehler kann erneut versucht werden, ohne die Annahme ungültig zu machen.

Weiter: [Neuen Server privat erstellen](create-new-server.md).

---

# Bekannte Einschränkungen

Source: `docs/de/start/known-limitations.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# Bekannte Einschränkungen

MEM 0.2.0 besitzt für das erste umfangreiche Release eine bewusst begrenzte Capability-Grenze. Prüfen Sie diese Grenzen, bevor Sie wichtige Kommunikation auf der Plattform betreiben.

## Nur Matrix

MEM 0.2.0 verwaltet Matrix- und Element-Umgebungen. XMPP, IRC, E-Mail, Mattermost und andere Messaging-Systeme werden derzeit nicht verwaltet. Multi-Protokoll-Unterstützung ist keine Funktion von 0.2.0.

## Docker-Modell auf einem Host

Das Release ist für einen herkömmlichen Docker-Host ausgelegt. Es bietet keine Clusterbildung über mehrere Knoten, automatische Ausfallsicherung, Kubernetes-Planung, hochverfügbare PostgreSQL-Cluster oder geografische Replikation. Getrennte Stacks teilen weiterhin den Ausfallbereich des Hosts und ausgewählter Plattformdienste.

## Migrationsumfang

Der aktuelle Migrationsadapter unterstützt ein definiertes älteres MEM-0.1.0-Quellprofil. Er ist kein allgemeiner Importer für jede Synapse-Version, beliebige Compose-Layouts, ESS Community, manuell aufgebaute Matrix-Installationen oder fremde Proxy-Konventionen.

## Clientseitige Verschlüsselungsschlüssel

MEM kann serverseitige Matrix-Daten sichern und wiederherstellen. Ende-zu-Ende-Schlüssel, die Benutzer weder auf Geräten noch im Key-Backup bewahrt haben, kann MEM nicht neu erzeugen. Passwort-Resets und Serverumzüge können deshalb alten verschlüsselten Verlauf unlesbar machen.

## Rechte der Control Plane

Die Control Plane kann Docker-Ressourcen und MEM-eigene Dateien verändern. Eine Kompromittierung der Control Plane kann die verwaltete Umgebung kompromittieren. Halten Sie die Verwaltungsoberfläche privat.

## Meinungsstarker Ingress

Der aktuelle geführte Installer unterstützt Nginx Proxy Manager und einen deSEC-basierten DNS-01-Workflow. Beliebige bestehende Proxies und DNS-Provider sind noch keine erstklassigen geführten Optionen.

## Allgemeine Dienste-Konsole

Die umfassende Services Operations Console gehört nicht zur normalen Oberfläche von 0.2.0. Spezialisierte coturn- und stackbezogene Steuerungen bleiben verfügbar; allgemeiner Docker-Notfallzugriff kann weiterhin Portainer oder Host-Befehle erfordern.

## Dimensionierung und Upstream-Verhalten

Vorprüfungswerte sind kein universeller Größenrechner. Föderation, öffentliche Räume, Medienaufbewahrung, Backups, Migrations-Workspaces und mehrere Stacks können den Ressourcenbedarf deutlich erhöhen.

MEM hängt außerdem vom Verhalten von Matrix, Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn und Docker ab. Prüfen Sie Release Notes und Capability-Status für den exakt unterstützten Build.

---

# Review and run the install plan

Source: `docs/installation/review-and-install.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Review and run the install plan

Review and installation are deliberately separate stages.

## Review before mutation

Review presents the exact canonical platform plan, including the selected domain, wildcard certificate intent, Docker network, PostgreSQL, Nginx Proxy Manager, ports, support tools, and the actions that will occur after installation starts.

The protected DNS, PostgreSQL, and NPM credentials do not belong in the frozen reviewed JSON. Saving the protected NPM administrator credential therefore does not rewrite the plan fingerprint.

Accepting Review:

- freezes the exact plan and fingerprint;
- records the operator's reviewed intent;
- does **not** yet mutate Docker, DNS, ACME, certificate storage, or NPM.

If plan-affecting input changes later, Review must be invalidated rather than silently reused.

## Install platform

Continue to **Install platform**. The page explicitly identifies this action as the external-mutation boundary.

When you choose **Install platform**, the durable server-owned worker can:

- create or verify `mem-gateway`;
- create persistent volumes;
- install the host `mem` command when the release payload is available;
- start or verify `mem-postgres` and wait for readiness;
- start or verify `mem-npm`;
- create and verify the first NPM administrator on a fresh NPM runtime;
- create the DNS-01 challenge and request the reviewed wildcard certificate;
- store and validate the certificate;
- import or resolve it in NPM;
- start selected support tools;
- run final platform verification;
- persist every durable step and attempt.

New plans do not install or route separate `mem-api` or `mem-web` containers. Old persisted step names are accepted only as retirement no-ops for compatibility.

## Waiting for operator action

A step can enter `WaitingForUser` rather than fail. Follow the action shown by MEM, correct the reported condition, and resume the same installation.

## Failure and Retry

When a step fails:

1. read the failed stage and safe error;
2. use **Run diagnostics** or download the bounded support report when useful;
3. correct the specific cause;
4. Retry the same durable installation when MEM says Retry is allowed.

Completed steps remain authoritative. Do not repeatedly delete containers, volumes, installation records, or certificate state between attempts.

## Optional support tools

The selected-support-tools stage may install release-approved support tooling such as Portainer. Seq remains optional, and Diagnostics must continue to work without Seq.

---

# Convert and run the private test

Source: `docs/migrate/private-test.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Convert and run the private test

The target converts the validated legacy payload into a migration-owned candidate before creating the normal production server.

## Outcome

Conversion completes successfully and the candidate runs as private Matrix and Element services with no public routes. The operator verifies that the captured server can start and that the expected data is present.

## Run conversion

In **Prepare and test**, start conversion. MEM runs the current migration worker contract, validates its structured progress and output hashes, and records a durable conversion attempt.

Conversion may take time. It continues on the server if you leave or refresh the page. Return to the same Migration Session to review progress, logs, warnings, and the final result.

A failed conversion does not create a normal chat server. Correct the reported issue or produce a new compatible package rather than copying partial output into a stack.

## Migration-owned candidate boundary

Successful conversion creates a verified candidate owned by the Migration Session. It is not:

- a Backup Catalog entry;
- a Restore Workspace;
- a normal managed runtime;
- publicly reachable.

Raw packages, temporary conversion files, failed candidates, and unaccepted candidates remain inside the migration bounded context.

## Start the private test

Create the private staging runtime. MEM imports the converted database, starts private Matrix and Element services, and verifies the candidate on an internal Docker network.

The private test must create **no public Matrix or Element routes**. It must not take ownership of the production hostname.

Review the available checks, including:

- database import and schema readiness;
- Synapse startup and health;
- Element configuration and private access;
- expected source identity;
- representative user, room, event, and media evidence exposed by the workspace;
- absence of public route ownership.

## Failure handling

If private staging fails, inspect conversion and staging logs before retrying. Do not proceed to target creation while the private runtime is unhealthy or its identity is uncertain.

A refresh does not cancel a running staging operation. Use the explicit operation controls where cancellation is supported.

## Retention boundary

The private candidate and staging resources remain migration-owned. MEM normally retains them through acceptance and the first native backup, then attempts cleanup. A cleanup failure can be retried without invalidating the accepted migration.

Next: [Create the new server privately](create-new-server.md).

---

# Upgrade and recreate the Control Plane

Source: `docs/operations/upgrading.md`
Locale: en
Section: Operations
Status: advanced
Applies to: 0.2.x

# Upgrade and recreate the Control Plane

MEM 0.2.x is designed so the canonical Control Plane container can be recreated without discarding its durable authority, but a general release upgrade must still follow the **release-specific instructions** for the target version.

## Durable state that must survive

A supported recreation preserves:

```text
mem-control-plane-data
/var/lib/message-easy-mode
private administration mode/address
ControlPlaneInstanceId
managed platform services and stack data
```

The API process identity should change after recreation while the durable Control Plane identity remains stable.

## Managed network topology matters too

An established containerized Control Plane must be able to reach managed services on `mem-gateway`. A supported recreation must therefore restore/reassert required managed-network membership before the replacement is accepted as operational.

## Do not use volume-destructive Docker commands

Do not use `docker rm -v`, delete `mem-control-plane-data`, or move `/var/lib/message-easy-mode` as an upgrade shortcut.

## Bootstrap is not an arbitrary blind updater

Do not assume that invoking an installer against an already-running canonical Control Plane automatically means "replace it with whatever image I requested". The supported behaviour is release-controlled and may deliberately retain or review an existing runtime.

Follow the exact update/recreation instructions shipped with the release candidate or final release.

## After recreation

Verify:

1. `/health/runtime` is valid and reports the intended runtime;
2. `ControlPlaneInstanceId` is unchanged and `ApiProcessInstanceId` rotated;
3. the private bind is unchanged;
4. platform services are healthy;
5. NPM is reachable from the Control Plane through the managed network;
6. Coturn functional verification is current;
7. a representative managed stack remains Healthy.

A future release may expand the guided upgrade workflow. Until then, prefer explicit release-specific evidence over legacy `stack.sh` or Compose-era upgrade instructions.

---

# Known limitations

Source: `docs/start/known-limitations.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# Known limitations

MEM 0.2.0 has a deliberately bounded first substantial release. Review these limits before placing important communications on the platform.

## Matrix only

MEM 0.2.0 manages Matrix and Element environments. It does not currently manage XMPP, IRC, email, Mattermost, or other messaging systems. Multi-protocol support is not a 0.2.0 feature.

## Single-host Docker model

The release is designed around a conventional Docker host. It does not provide multi-node clustering, automatic failover, Kubernetes scheduling, a high-availability PostgreSQL cluster, or geographic replication. Separate stacks on one host still share that host's failure domain and selected platform services.

## Migration scope

The current migration adapter supports a defined legacy MEM 0.1.0 source profile. It is not a general importer for every Synapse version, arbitrary Compose layout, ESS Community, manually assembled Matrix installation, or unrelated proxy convention.

## Client encryption keys

MEM can back up and restore server-side Matrix data. It cannot recreate end-to-end encryption keys that users failed to preserve on devices or in key backup. Password resets and server moves can therefore leave old encrypted history unreadable for a user with missing recovery material.

## Control-plane privilege

The Control Plane can change Docker resources and MEM-owned files. A compromise of the Control Plane can become a compromise of the managed environment. Keep the administration surface private.

## Opinionated ingress

The current guided installer supports Nginx Proxy Manager and a deSEC-based DNS-01 workflow. Arbitrary existing proxies and DNS providers are not yet first-class guided options.

## General Services console

The broad Services Operations Console is not part of the normal 0.2.0 surface. Specialist coturn and stack-level controls remain available; general Docker emergency access may still require Portainer or host commands.

## Sizing and upstream behaviour

Preflight thresholds are not a universal sizing calculator. Federation, public rooms, media retention, backups, migration workspaces, and multiple stacks can raise resource requirements substantially.

MEM also depends on upstream Matrix, Synapse, Element, PostgreSQL, Nginx Proxy Manager, coturn, and Docker behaviour. Consult release notes and capability status for the exact supported build.

---

# Recover with Standard Recreate

Source: `docs/backups-and-restores/standard-recreate.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Recover with Standard Recreate

**Standard Recreate** is the normal supported production recovery path for a Backup Catalog entry. It creates a real managed Matrix + Element stack; it is not a container preview.

## Before execution

Complete the read-only target preflight and confirm:

- the catalog payload is available;
- the backed-up Matrix server identity is present and preserved;
- the target stack slug is unused and unclaimed;
- the original Matrix address is ready for recovery and not claimed;
- the chosen Element host is available and unclaimed;
- required platform services, including coturn when the backup requires a platform rebind, are ready.

Preflight does not reserve the target. Execution repeats the checks and acquires claims atomically.

## Production mutations

The create action requires explicit acknowledgements because it can:

- provision and import a production PostgreSQL database;
- restore Matrix configuration, signing key, and media;
- restore and patch Element configuration;
- create Matrix and Element containers on the MEM runtime network;
- register the recovered stack and database ownership;
- create or update Nginx Proxy Manager routes;
- run internal and public readiness checks.

The action is protected by operator step-up. There is **no automatic rollback** for the production recreate. Preserve the backup and review all target details before confirming.

## Matrix identity

The Matrix server identity comes from the backup and is immutable in the guided workflow. This is necessary for existing Matrix user IDs, rooms, federation identity, and signing material to remain coherent.

The old server or old public route for the same Matrix identity must no longer be active. Running two public homeservers with the same identity can split traffic and damage federation behaviour.

## TURN fidelity

Standard Recreate applies a deterministic TURN policy:

- no recorded TURN association restores as **disconnected**;
- MEM-managed TURN rebinds the stack to the current platform coturn service and blocks when that service is not ready;
- external TURN preserves the backed-up Synapse TURN settings;
- legacy TURN settings without durable association metadata are preserved and classified as external rather than silently replaced.

The restore does not place a test call. Verify voice and video after handover.

## User inventory

Matrix accounts remain in the restored Synapse database. MEM attempts to synchronize its safe Users inventory after the recreate. A synchronization warning does not mean the Matrix accounts were deleted; retry synchronization from the restored stack's Users area.

## Completion boundary

A successful create operation is not final handover. Continue to [Verify and complete the restored server](verify-and-complete.md).

---

# Manage federation

Source: `docs/chat-servers/federation.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Manage federation

## Outcome

Choose how the stack exchanges Matrix federation traffic with other homeservers and apply the change through a reviewed, journalled operation.

Open the stack workspace and select **Federation**.

## Supported modes

**Public federation** permits normal communication with valid Matrix homeservers through the public Matrix route.

**Restricted federation** uses Synapse's built-in exact-domain allowlist. Enter one homeserver domain per line. Wildcards, URLs, IP addresses, paths, ports, and duplicates are rejected. This is an allowlist, not a separate federation border gateway.

**Local-only federation** applies an empty Synapse allowlist and blocks discovery, federation, and signing-key paths at the canonical NPM route. Matrix client access remains publicly available unless you separately restrict it with network or VPN controls.

Local-only does not delete existing users, rooms, messages, media, signing keys, or historical remote room state.

## Review before apply

The editor observes the active Synapse configuration, Matrix runtime, and canonical NPM route. Custom, ambiguous, incomplete, or unsupported state can disable management until the problem is resolved.

Select **Review change**. The review shows:

- current and proposed modes;
- domains added and removed;
- whether Matrix restart is required;
- whether NPM ingress changes;
- impact on existing federated rooms;
- automatic rollback behaviour.

A relevant stack change invalidates the review. Do not reuse a stale review.

## Apply the policy

Confirm the exact reviewed change and complete recent step-up verification when requested.

MEM journals the operation, validates the candidate with the active Synapse image, replaces configuration atomically, performs the controlled Matrix restart, changes NPM ingress when required, and verifies client and federation behaviour.

Connected clients can reconnect briefly during restart. Removing a homeserver from the restricted allowlist can stop new event exchange with that server; existing history remains.

## Failure and rollback

When restart or verification fails after mutation, MEM attempts to restore and verify the exact previous Synapse and ingress state. If the browser disconnects, do not submit a second change while the durable operation is still running.

Use the operation ID and final observed state to decide whether it is safe to retry. An unresolved state requires technical recovery.

---

# Mit Standard-Neuerstellung wiederherstellen

Source: `docs/de/backups-und-wiederherstellen/standard-neuerstellung.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Mit Standard-Neuerstellung wiederherstellen

Die **Standard-Neuerstellung** ist der normale unterstützte Produktionsweg für einen Katalogeintrag. Sie erstellt einen echten verwalteten Matrix- und Element-Stack und keine Vorschau.

## Vor der Ausführung

Die schreibgeschützte Zielprüfung muss bestätigen:

- Katalog-Payload ist verfügbar;
- gesicherte Matrix-Serveridentität ist vorhanden und bleibt erhalten;
- Ziel-Stack-Slug ist frei und nicht reserviert;
- ursprüngliche Matrix-Adresse ist zur Wiederherstellung bereit;
- gewählter Element-Host ist frei und nicht reserviert;
- benötigte Plattformdienste, einschließlich coturn bei Plattform-Neubindung, sind bereit.

Die Vorprüfung reserviert nichts. Die Ausführung prüft erneut und erwirbt Reservierungen atomar.

## Produktionsänderungen

Die bestätigte Aktion kann:

- Produktions-PostgreSQL-Datenbank bereitstellen und importieren;
- Matrix-Konfiguration, Signaturschlüssel und Medien wiederherstellen;
- Element-Konfiguration wiederherstellen und anpassen;
- Matrix- und Element-Container im MEM-Laufzeitnetz erstellen;
- Stack- und Datenbankeigentum registrieren;
- Nginx-Proxy-Manager-Routen erstellen oder ändern;
- interne und öffentliche Bereitschaft prüfen.

Die Aktion verlangt Operator-Step-up. Es gibt **keinen zugesagten automatischen Rollback**. Bewahren Sie die Sicherung auf und prüfen Sie alle Zieldaten.

## Matrix-Identität

Die Matrix-Serveridentität stammt aus der Sicherung und ist im geführten Ablauf unveränderlich. Bestehende Matrix-Benutzer-IDs, Räume, Föderationsidentität und Signaturmaterial hängen davon ab.

Der alte Server oder die alte öffentliche Route derselben Identität darf nicht mehr aktiv sein. Zwei öffentliche Homeserver mit derselben Identität können Datenverkehr aufteilen und Föderation beschädigen.

## TURN-Treue

- Keine aufgezeichnete TURN-Zuordnung wird **getrennt** wiederhergestellt.
- MEM-verwaltetes TURN wird an den aktuellen Plattform-coturn gebunden und blockiert, wenn dieser nicht bereit ist.
- Externes TURN bewahrt die gesicherten Synapse-TURN-Einstellungen.
- Alte TURN-Einstellungen ohne dauerhafte Zuordnungsmetadaten bleiben erhalten und werden als extern klassifiziert.

Die Wiederherstellung führt keinen Testanruf durch. Sprache und Video nach der Übergabe prüfen.

## Benutzerinventar

Matrix-Konten bleiben in der wiederhergestellten Synapse-Datenbank. MEM synchronisiert anschließend seine sichere Benutzeransicht. Eine Synchronisierungswarnung bedeutet nicht, dass Konten gelöscht wurden; synchronisieren Sie im Bereich **Benutzer** erneut.

Nach erfolgreicher Erstellung mit [Prüfung und Übergabe](pruefen-und-abschliessen.md) fortfahren.

---

# Föderation verwalten

Source: `docs/de/chat-servers/foederation.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Föderation verwalten

## Ergebnis

Wählen Sie, wie der Stack Matrix-Föderationsverkehr mit anderen Homeservern austauscht, und übernehmen Sie die Änderung über einen geprüften, journalisierten Vorgang.

Öffnen Sie im Stack-Arbeitsbereich **Föderation**.

## Unterstützte Modi

**Öffentliche Föderation** erlaubt normale Kommunikation mit gültigen Matrix-Homeservern über die öffentliche Matrix-Route.

**Eingeschränkte Föderation** verwendet die exakte Domain-Allowlist von Synapse. Geben Sie pro Zeile eine Homeserver-Domain ein. Wildcards, URLs, IP-Adressen, Pfade, Ports und Duplikate werden abgelehnt. Dies ist eine Allowlist und kein eigener Föderations-Gateway.

**Nur lokale Föderation** verwendet eine leere Synapse-Allowlist und blockiert Discovery-, Föderations- und Signing-Key-Pfade an der kanonischen NPM-Route. Matrix-Client-Zugriff bleibt öffentlich, sofern Sie ihn nicht zusätzlich per Netzwerk oder VPN einschränken.

Der lokale Modus löscht keine vorhandenen Benutzer, Räume, Nachrichten, Medien, Signaturschlüssel oder historischen entfernten Raumzustände.

## Vor Übernahme prüfen

Der Editor beobachtet aktive Synapse-Konfiguration, Matrix-Laufzeit und kanonische NPM-Route. Benutzerdefinierte, mehrdeutige, unvollständige oder nicht unterstützte Zustände können Verwaltung deaktivieren.

Wählen Sie **Änderung prüfen**. Die Prüfung zeigt:

- aktuellen und vorgeschlagenen Modus;
- hinzugefügte und entfernte Domains;
- erforderlichen Matrix-Neustart;
- NPM-Ingress-Änderung;
- Auswirkungen auf föderierte Räume;
- automatisches Rollback.

Eine relevante Stack-Änderung macht die Prüfung ungültig. Verwenden Sie keine alte Prüfung erneut.

## Richtlinie übernehmen

Bestätigen Sie die exakt geprüfte Änderung und führen Sie bei Aufforderung aktuelle Step-up-Prüfung aus.

MEM journalisiert den Vorgang, prüft den Kandidaten mit dem aktiven Synapse-Abbild, ersetzt Konfiguration atomar, führt den kontrollierten Matrix-Neustart aus, ändert bei Bedarf NPM-Ingress und prüft Client- und Föderationsverhalten.

Clients können während des Neustarts kurz neu verbinden. Entfernte Homeserver können keine neuen Ereignisse mehr austauschen; vorhandener Verlauf bleibt erhalten.

## Fehler und Rollback

Scheitert Neustart oder Prüfung nach einer Änderung, versucht MEM den exakten vorherigen Synapse- und Ingress-Zustand wiederherzustellen. Bei Browsertrennung senden Sie keine zweite Änderung, solange der dauerhafte Vorgang läuft.

Verwenden Sie Vorgangs-ID und beobachteten Endzustand für die Entscheidung über einen neuen Versuch. Ein ungeklärter Zustand benötigt technische Wiederherstellung.

---

# Plattform verifizieren und Übergabe abschließen

Source: `docs/de/installation/verifizieren-und-uebergeben.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

# Plattform verifizieren und Übergabe abschließen

Installationsabschluss und erfolgreiche Verifikation sind getrennte Tatsachen.

## Verifikationsbericht

Die Abschlussprüfung validiert den dauerhaften Installationslauf und erforderliche verwaltete Abhängigkeiten wie PostgreSQL, NPM, gemeinsamen Coturn sowie den ausgewählten Zertifikatszustand.

## Private Control-Plane-Grenze verifizieren

Prüfen Sie vor der Übergabe, dass dieselbe private Zugriffsart in folgenden Ansichten erscheint:

- Home → Host status;
- Diagnostics → Control Plane runtime;
- System Information.

Für SSH/local-only wird `127.0.0.1:<port>` erwartet. Für Trusted LAN wird genau die ausgewählte RFC1918-Adresse erwartet.

Bei einer gesunden Installation darf Diagnostics keinen `control_plane.exposure.unsupported`-Incident enthalten.

Akzeptieren Sie keine Übergabe, wenn MEM Wildcard-, öffentliche, mehrfache oder anderweitig nicht unterstützte Exposition meldet.

## Übergabe

Nach erfolgreicher Verifikation bestätigt **Einrichtung abschließen und Dashboard öffnen** den Wechsel in den normalen Operatorbetrieb. Matrix- und Element-Stack-Erstellung bleibt ein separater Workflow nach der Plattformübergabe. Eine erfolgreiche Plattforminstallation beweist noch nicht, dass der erste verwaltete Stack bereitgestellt werden kann.

## Empfohlene nächste Sicherheitsaktionen

- MFA-Anmeldung des Platform Owner erneut prüfen;
- Recovery-Codes sicher aufbewahren;
- privaten Verwaltungsmodus und die Adresse dokumentieren;
- SHA-256-Fingerprint des Control-Plane-Zertifikats dokumentieren;
- Control Plane aus öffentlichem DNS, NPM-Ingress und Internet-NAT heraushalten;
- SSH auch bei normalem Trusted-LAN-Zugriff als Break-Glass-Weg behalten;
- Diagnostics und Plattformzustand prüfen;
- unter **Services → Coturn** eine aktuelle erfolgreiche Funktionsprüfung verlangen, wenn TURN Teil der Bereitstellung ist;
- den ersten Matrix- und Element-Stack erstellen;
- Matrix und Element müssen **Healthy** erreichen;
- im Stack **Doctor** ausführen;
- die öffentlichen Matrix- und Element-Routen prüfen, bevor die Installation betrieblich als abgenommen gilt.

---

# Neuen Server privat erstellen

Source: `docs/de/migrieren/create-new-server.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Neuen Server privat erstellen

Nach erfolgreichem privaten Test kann MEM den Kandidaten als normalen verwalteten Chatserver materialisieren und zunächst privat halten.

## Ergebnis

Das Ziel besitzt eine privat gesunde normale MEM-Laufzeit mit bewahrter Matrix-Identität, gewähltem Stack-Namen und Element-Hostname, aber ohne öffentliche Routenhoheit.

## Identitätsentscheidungen

Die migrierte öffentliche Matrix-Adresse ist auf die erfasste Quellidentität festgelegt. Eine Änderung würde einen anderen Matrix-Server erzeugen und gehört nicht zu dieser Migration.

Wählen und prüfen Sie:

- normalen MEM-Stack-Namen oder Slug;
- bewahrten Matrix-Hostname;
- zu veröffentlichenden Element-Hostname;
- Zieldatenbank und Laufzeiteigentum;
- Nutzung normaler gemeinsamer Ziel-Dienste und Richtlinien.

## Ziel-Vorprüfung ausführen

MEM prüft vor Änderungen Konflikte bei Stack-Name, Datenbank, Matrix- und Element-Hostnamen, Hostpfaden, Containern, Laufzeiteigentum, bestehenden öffentlichen Routen sowie aktiven Migrations- oder Wiederherstellungsansprüchen.

Lösen Sie Konflikte bewusst. Löschen Sie keinen vorhandenen Produktions-Stack oder eine Route nur für eine grüne Vorprüfung, solange Sie Obsoleszenz und separaten Wiederherstellungsweg nicht nachgewiesen haben.

## Quellautorität wählen

Der normale geführte Pfad verwendet Paket und privaten Test als maßgeblichen Snapshot. Er ist einfacher, enthält aber keine späteren Änderungen am alten Server und bietet weniger formale Einfrier- und Wiederherstellungsnachweise.

Der erweiterte **final eingefrorene** Pfad erstellt nach formellem Einfrieren der Quelle ein finales Paket. Er liefert stärkere Drift- und Rollback-Nachweise, benötigt aber zusätzliche Quellschritte. Lesen Sie [Rollback-Grenzen](rollback.md).

## Server erstellen

Schließen Sie Bestätigung und Step-up ab. MEM erstellt normale Stack-Datenbank, Matrix- und Element-Dienste, Konfiguration und Eigentumsnachweise. Öffentliche Routen werden in diesem Schritt nicht veröffentlicht.

Bestätigen Sie eine gesunde private normale Laufzeit. Fahren Sie bei unklarer öffentlicher Routenhoheit nicht fort.

## Nutzung der Quelle

Beim normalen Snapshot-Pfad beenden Sie die normale Nutzung des alten Servers vor der Liveschaltung. Beim final eingefrorenen Pfad bleibt die Quelle eingefroren. Bewahren Sie den Host in beiden Fällen bis nach Produktionsprüfung und Annahme auf.

Weiter: [Neuen Server live schalten und Produktion prüfen](go-live.md).

---

# MEM-Glossar

Source: `docs/de/start/glossary.md`
Locale: de
Section: Erste Schritte
Status: supported
Applies to: 0.2.x

# MEM-Glossar

## Produkt- und Operatorbegriffe

**MEM** — Message Easy Mode, das Projekt und die Produktfamilie.

**MEM Control Plane** — die private Web- und API-Anwendung für Operatoridentität, Workflow-Zustand, Orchestrierung, Diagnose und Zugriff auf MEM-verwaltete Docker- und Host-Ressourcen.

**Operator** — eine benannte Person mit Berechtigung zur Nutzung der Control Plane. Fähigkeiten hängen von Rollen und Authentifizierungszustand ab.

**Step-up-Verifizierung** — erneute Identitätsprüfung vor ausgewählten risikoreichen Aktionen.

**Stack / Chatserver** — eine unabhängig verwaltete Matrix-Umgebung, normalerweise mit eigenem Synapse, Element, Datenbank, Identität, Medien, Konfiguration und öffentlichen Hostnamen.

## Matrix-Laufzeitbegriffe

**Matrix** — das offene Protokoll, das MEM 0.2.0 für Messaging und Föderation verwendet.

**Synapse** — die Matrix-Homeserver-Implementierung eines Stacks.

**Element** — der primäre Matrix-Client als Web-Client des Stacks.

**Föderation** — Server-zu-Server-Kommunikation zwischen unabhängigen Matrix-Homeservern.

**TURN / coturn** — TURN leitet Anrufmedien weiter, wenn direkte Verbindungen nicht funktionieren; coturn ist die gemeinsame Serverimplementierung.

**Nginx Proxy Manager / NPM** — die unterstützte Ingress-Komponente für öffentliche HTTPS-Hostnamen, Dienste und Zertifikate.

## Wiederherstellungsbegriffe

**Backup-Katalogeintrag** — die dauerhafte Inventaridentität einer Wiederherstellungsquelle einschließlich Herkunft, Payload-Zustand und Integrität.

**Portables Backup** — ein exportierbares Archiv für Übertragung und Import.

**Restore-Versuch** — der dauerhafte Workflow-Datensatz eines Wiederherstellungsvorgangs.

**Restore Workspace** — Oberfläche und Serverzustand für Phasen, Logs, Nachweise, privaten Test, Neuerstellung und Abschluss.

**Privater Test** — eine Wiederherstellungs- oder Migrationskandidatenumgebung ohne Übernahme der öffentlichen Produktionsrouten.

**Standard Recreate** — normaler Ablauf zur Neuerstellung eines Stacks aus einem validierten Backup-Katalogeintrag.

**Target Claim** — dauerhafte Reservierung gegen konkurrierende Restore-Versuche mit derselben Identität oder denselben Hostnamen.

## Migration und Diagnose

**MEM Migrate** — das separate Produkt mit quellversionsspezifischem Migrationswissen.

**Source Assistant** — die temporäre lokale Web-Anwendung auf dem alten Server.

**Zielanfrage** — die vom Ziel erzeugte Anfrage mit Zielidentität und Verschlüsselungsempfänger.

**Migrationspaket** — das verschlüsselte portable Paket von Quelle zu Ziel.

**Kandidatenartefakt** — eine validierte Konvertierungsausgabe für private Bereitstellung.

**Produktionsübernahme** — kontrollierter Vorgang, der einem verifizierten Kandidaten Produktionsidentität und Routen gibt.

**Vorfall** — ein für Operatoren relevanter Fehlerkontext mit stabiler ID und sicheren korrelierten Nachweisen.

**Sicheres Diagnoseereignis** — ein begrenztes, redigiertes Ereignis für Browser-APIs und Supportberichte.

**Technisches Log** — umfangreichere Serilog-Ausgabe für Konsole, dauerhaftes CLEF und optionales Seq; nicht automatisch browser-sicher.

**Supportbericht** — begrenztes JSON mit sicheren Vorfall-, Betriebs-, Soll-/Ist- und optionalen Docker-Nachweisen.

**Laufzeitabgleich** — Vergleich und Reparatur des erwarteten MEM-Zustands gegenüber Dateisystem- und Docker-Zustand.

---

# Verify the platform and complete handoff

Source: `docs/installation/verify-and-handoff.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Verify the platform and complete handoff

Installation completion and successful verification are separate facts.

## Verification report

The final platform verification validates the durable install run and required managed dependencies such as PostgreSQL, NPM, shared Coturn, the selected production/staging certificate state, and NPM certificate visibility.

Sensitive values remain protected.

## Verify the Control Plane administration boundary

Before completing handoff, confirm that the running Control Plane reports the same private access state in:

- Home → Host status;
- Diagnostics → Control Plane runtime;
- System Information.

For SSH/local-only, expect `127.0.0.1:<port>`. For Trusted LAN, expect the exact selected RFC1918 address.

Diagnostics must not contain `control_plane.exposure.unsupported` for a healthy supported deployment.

Do not accept handoff if MEM reports wildcard, public, multiple, or otherwise unsupported exposure.

## Handoff

After verification succeeds, **Finish setup and open dashboard** confirms the transition into normal operator mode.

A Control Plane restart before finish must reconstruct the same pending handoff. After finish, first-time Setup and first-owner bootstrap are locked.

Matrix and Element stack creation is a separate operator workflow after platform handoff. A successful platform install is not yet proof that the first managed stack can be provisioned.

## Recommended next security actions

- confirm the Platform Owner can sign out and sign back in with MFA;
- keep recovery codes safely offline;
- record the selected private administration mode/address;
- record the Control Plane certificate SHA-256 fingerprint;
- keep the Control Plane out of public DNS, NPM ingress, and Internet-facing NAT;
- retain SSH as the break-glass path even when Trusted LAN is the normal access method;
- review Diagnostics and platform health;
- open **Services → Coturn** and require a current successful functional check when TURN is part of the deployment;
- create the first Matrix + Element stack;
- require Matrix and Element to reach **Healthy**;
- open the stack and run **Doctor**;
- verify the public Matrix and Element routes before calling the installation operationally accepted.

The handoff finishes platform installation. It does not remove the operator's responsibility for host, platform, and stack backups.

---

# Create the new server privately

Source: `docs/migrate/create-new-server.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Create the new server privately

After a successful private test, MEM can materialize the candidate as a normal managed chat server while keeping it private.

## Outcome

The target has a privately healthy normal MEM runtime with preserved Matrix identity, a chosen stack name and Element hostname, and no public route ownership.

## Identity choices

The migrated Matrix public address is locked to the captured source identity. Changing it would create a different Matrix server and is not part of this migration.

Choose and review:

- the normal MEM stack name or slug;
- the preserved Matrix hostname;
- the Element hostname to publish;
- the target database and runtime ownership;
- whether the stack will use the target’s normal shared services and policies.

## Run target preflight

Before mutation, MEM checks for conflicts involving:

- stack name and database identity;
- Matrix and Element hostnames;
- host paths and retained migration workspaces;
- container names and runtime ownership;
- existing public routes;
- other active migration or restore claims.

Resolve a conflict deliberately. Do not delete an existing production stack or route merely to make the preflight green unless you have proven it is obsolete and have a separate recovery path.

## Choose the source-authority posture

The normal guided path uses the verified package and private test as the authoritative source snapshot. It is simpler, but changes made on the legacy server after capture are not included and formal source-freeze/source-restoration evidence is reduced.

The advanced **final-frozen** path creates a final package after formally freezing the source. It provides stronger drift and rollback evidence but requires additional source-side handoff steps. Read [Rollback boundaries](rollback.md) before choosing it.

## Create the server

Complete the confirmation and step-up prompts. MEM creates the normal stack database, Matrix and Element services, configuration, and ownership records. It does not publish the public routes during this step.

Verify that the new normal runtime is healthy and private. Do not proceed if public route ownership is already ambiguous.

## Source usage boundary

For the normal snapshot path, arrange to stop normal use of the old server before go-live. For the final-frozen path, keep the source frozen. In both cases, retain the old host until production verification and acceptance are complete.

Next: [Make the new server live and verify production](go-live.md).

---

# MEM glossary

Source: `docs/start/glossary.md`
Locale: en
Section: Start here
Status: supported
Applies to: 0.2.x

# MEM glossary

## Product and operator terms

**MEM** — Message Easy Mode, the project and product family.

**MEM Control Plane** — the private Web and API application that owns operator identity, workflow state, orchestration, diagnostics, and access to MEM-managed Docker and host resources.

**Operator** — a named person authorised to use the Control Plane. Capabilities depend on roles and authentication state.

**Step-up verification** — fresh identity verification required before selected high-risk actions.

**Stack / chat server** — one independently managed Matrix environment, normally with its own Synapse, Element, database, identity, media, configuration, and public hostnames.

## Matrix runtime terms

**Matrix** — the open protocol used by MEM 0.2.0 for messaging and federation.

**Synapse** — the Matrix homeserver implementation managed for each stack.

**Element** — the primary Matrix client supplied as the stack's Web client.

**Federation** — server-to-server communication between independent Matrix homeservers.

**TURN / coturn** — TURN relays call media when peers cannot connect directly; coturn is the shared server implementation.

**Nginx Proxy Manager / NPM** — the supported ingress component mapping public HTTPS hostnames to services and certificates.

## Recovery terms

**Backup Catalog entry** — the durable inventory identity for one recovery source, including provenance, payload state, and integrity.

**Portable backup** — an exportable archive designed for transfer and import.

**Restore attempt** — the durable workflow record for one recovery operation.

**Restore Workspace** — the UI and server state for stages, logs, evidence, private testing, recreation, and completion.

**Private test** — a recovery or migration candidate started without taking production public routes.

**Standard Recreate** — the normal workflow for recreating a stack from a validated Backup Catalog entry.

**Target claim** — a durable reservation preventing conflicting restore attempts from taking the same identity or hostnames.

## Migration and diagnostics terms

**MEM Migrate** — the separate product containing source-version-specific migration knowledge.

**Source Assistant** — the temporary local Web application on the old server.

**Target request** — the target-generated request containing destination identity and encryption recipient.

**Migration package** — the encrypted portable package transferred from source to target.

**Candidate artifact** — a validated conversion output that can be privately staged.

**Production adoption** — the controlled operation giving a verified candidate its production identity and routes.

**Incident** — an operator-relevant failure context with a stable identifier and safe correlated evidence.

**Safe diagnostic event** — a bounded, redacted event suitable for browser APIs and support reports.

**Technical log** — richer Serilog output for console, persistent CLEF, and optional Seq; not automatically browser-safe.

**Support report** — bounded JSON containing safe incident, operation, expected-versus-observed, and optional Docker evidence.

**Runtime reconciliation** — comparison and repair of expected MEM state against observed filesystem and Docker state.

---

# Resolve target claims and hostname conflicts

Source: `docs/backups-and-restores/target-claims.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Resolve target claims and hostname conflicts

MEM coordinates restore targets so that two active attempts cannot safely claim the same stack slug or public host.

## Target values

Standard Recreate uses three important values:

- **Matrix server identity / Matrix host** — preserved from the backup;
- **Target stack slug** — the new MEM runtime identity, often suggested as `<source>-restored`;
- **Element host** — the public Element address, which may reuse the source address when available or use another approved host.

You cannot solve a Matrix-host conflict by casually choosing a different Matrix domain. Changing it would create a different Matrix identity rather than restore the existing one.

## Preflight checks

The read-only preflight assesses:

- catalog source validity and payload availability;
- Matrix server identity preservation;
- target stack manifest and live-runtime availability;
- target stack claim availability;
- Matrix host runtime and claim availability;
- Element host runtime and claim availability;
- route ownership and TURN requirements.

A ready result is current evidence, not a reservation. Another operation could change availability before execution, so Standard Recreate checks again and claims the values atomically.

## Common conflicts

### Stack slug already exists

Choose a different target slug. Do not delete an unrelated stack merely to satisfy preflight.

### Matrix address is still active

Stop or retire the old runtime through its supported lifecycle and confirm the original public route no longer owns the Matrix host. Preserve the old server until you have the final backup and rollback decision you need, but do not leave both publicly active.

### Matrix or Element host is claimed

Open the other active Restore Workspace and decide which attempt owns the target. A safe cancellation releases temporary claims when no operation is queued or running.

### Element host is in use

Select another approved Element host or retire the conflicting route through its owning stack workflow. Do not edit Nginx Proxy Manager out of band unless working through a documented emergency procedure.

## Claim evidence

The Restore Workspace **Configuration** tab records resource type, value, claim status, claim time, release time, and release reason. Keep this evidence when diagnosing a conflict.

A completed restored stack is not undone by cancelling its old workspace. Once production recreation has completed, manage the stack through normal stack operations.

---

# Understand storage and media

Source: `docs/chat-servers/storage-and-media.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Understand storage and media

## Outcome

Use the top-level **Storage** workspace for a read-only fleet inventory of Matrix media across managed chat servers.

Open a chat server and select **Services** for detailed storage and media evidence owned by that stack.

The Storage surfaces are inspection tools. They are not file managers and do not edit, prune, move, or delete media.

## Fleet Storage workspace

Open **Storage** from the main operator navigation to compare managed chat servers without expanding every technical filesystem detail.

The fleet workspace starts with aggregate MEM-managed Matrix-media totals across all successfully inspected chat servers and the safely observed capacity of the filesystem that backs MEM data. The capacity figure is authoritative for the configured MEM data filesystem; it may be a dedicated volume and is not necessarily the Ubuntu host's entire disk. If MEM cannot prove the backing filesystem safely, the capacity is shown as unavailable instead of inferring the Control Plane container overlay. The overview includes:

- percentage and bytes still available on the MEM data filesystem;
- managed Matrix-media size;
- total media-file count;
- local-upload usage;
- available media stores;
- remote-media cache, generated-thumbnail, and URL-preview-cache usage.

The inventory below those totals shows each chat server's Matrix-media size, local uploads, remote cache, media-store availability, and a link to the owning chat server's detailed **Services** workspace.

The page refreshes read-only runtime evidence automatically and also provides one fleet Refresh action. Technical filesystem paths are intentionally kept out of the fleet table.

## Detailed storage evidence

Open **Chat servers → <server> → Services** to review the detailed storage evidence for one chat server.

The Services workspace can show:

- Matrix data path;
- media-store path and total size;
- media file count;
- `homeserver.yaml` presence and size;
- the Matrix signing key presence and size;
- Element data and config paths;
- media-section inventory such as local uploads, remote cache, thumbnails, and URL previews.

Technical filesystem paths are support and recovery evidence. They remain in the detailed stack workspace instead of the fleet inventory.

## Recovery boundary

Matrix media works as a pair:

- PostgreSQL stores event, metadata, and media references;
- the Synapse media directory stores the file bytes.

A recoverable backup therefore needs both the stack database dump and the media directory. Synapse configuration, signing key, and Element configuration are also recovery-relevant.

> [!WARNING]
> Copying only the media directory is not a complete Matrix backup. Copying only PostgreSQL can leave media references without their files.

## Capacity planning

Use the **MEM data storage available** figure to watch the filesystem that actually carries MEM's configured data root. The Storage workspace reports available bytes, total capacity, and the percentage remaining when that filesystem can be observed safely in the active runtime. Home uses the same authoritative MEM-data capacity projection.

Watch media growth together with database, backup, and migration workspace usage. The installation preflight threshold is not a lifetime capacity guarantee.

Remote media cache and generated derivatives can consume space even when local users upload little. Use the fleet inventory to identify growth, then open the chat server's Services workspace for the detailed section evidence.

## Avoid manual cleanup

Do not delete the signing key, `homeserver.yaml`, database volume, or media folders to reclaim space. Manual cleanup can break identity, history, federation, or recovery fidelity.

Create a verified backup and use an explicit supported lifecycle workflow for removal or restore work.

---

# Zielreservierungen und Hostkonflikte lösen

Source: `docs/de/backups-und-wiederherstellen/zielreservierungen.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Zielreservierungen und Hostkonflikte lösen

MEM koordiniert Wiederherstellungsziele, damit zwei aktive Versuche nicht denselben Stack-Slug oder öffentlichen Host beanspruchen.

## Zielwerte

- **Matrix-Serveridentität / Matrix-Host** — aus der Sicherung bewahrt;
- **Ziel-Stack-Slug** — neue MEM-Laufzeitidentität, häufig als `<quelle>-restored` vorgeschlagen;
- **Element-Host** — öffentliche Element-Adresse, die bei Verfügbarkeit wiederverwendet oder neu gewählt werden kann.

Ein Matrix-Hostkonflikt wird nicht durch eine beliebig andere Matrix-Domain gelöst. Das würde eine andere Matrix-Identität erzeugen.

## Vorprüfungen

Die schreibgeschützte Prüfung bewertet:

- Gültigkeit und Verfügbarkeit der Katalogquelle;
- Bewahrung der Matrix-Identität;
- Verfügbarkeit von Stack-Manifest und aktivem Runtime;
- Stack-Slug-Reservierung;
- Matrix-Host-Runtime und -Reservierung;
- Element-Host-Runtime und -Reservierung;
- Routeneigentum und TURN-Anforderungen.

Ein bereites Ergebnis ist keine Reservierung. Die Ausführung prüft erneut und reserviert atomar.

## Häufige Konflikte

### Stack-Slug vorhanden

Anderen Ziel-Slug wählen. Löschen Sie keinen fremden Stack nur für eine erfolgreiche Vorprüfung.

### Matrix-Adresse noch aktiv

Alten Runtime über den unterstützten Lebenszyklus stoppen oder entfernen und bestätigen, dass die öffentliche Route den Matrix-Host nicht mehr besitzt. Alten Server für Backup- und Rückfallentscheidung erhalten, aber nicht parallel öffentlich betreiben.

### Host reserviert

Anderen aktiven Wiederherstellungsarbeitsbereich öffnen und Eigentum klären. Ein sicherer Abbruch gibt temporäre Reservierungen frei, sofern kein Vorgang läuft oder wartet.

### Element-Host belegt

Anderen genehmigten Element-Host wählen oder die konfliktbehaftete Route über ihren Stack-Lebenszyklus entfernen. NPM nicht ohne dokumentierten Notfallweg direkt bearbeiten.

## Reservierungsnachweise

Die Registerkarte **Konfiguration** zeigt Ressourcentyp, Wert, Status, Reservierungszeit, Freigabezeit und Freigabegrund.

Ein fertig erstellter Produktions-Stack wird durch Abbruch eines alten Arbeitsbereichs nicht gelöscht. Danach gelten normale Stack-Abläufe.

---

# Speicher und Medien verstehen

Source: `docs/de/chat-servers/speicher-und-medien.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Speicher und Medien verstehen

## Ergebnis

Nutzen Sie den obersten Arbeitsbereich **Speicher** als schreibgeschütztes Flotteninventar für Matrix-Medien auf verwalteten Chatservern.

Öffnen Sie einen Chatserver und wählen Sie **Dienste**, um detaillierte Speicher- und Mediennachweise dieses Stacks zu prüfen.

Die Speicheransichten dienen der Prüfung. Sie sind keine Dateimanager und bearbeiten, bereinigen, verschieben oder löschen keine Medien.

## Flottenarbeitsbereich Speicher

Öffnen Sie **Speicher** in der Hauptnavigation der Administration, um verwaltete Chatserver zu vergleichen, ohne alle technischen Dateisystemdetails einzublenden.

Der Flottenarbeitsbereich beginnt mit aggregierten Summen der von MEM verwalteten Matrix-Medien über alle erfolgreich geprüften Chatserver und der sicher ermittelten Kapazität des Dateisystems, das die MEM-Daten trägt. Der Kapazitätswert ist für das konfigurierte MEM-Datendateisystem maßgeblich; dieses kann ein eigenes Volume sein und ist nicht zwingend die gesamte Festplatte des Ubuntu-Hosts. Kann MEM das zugrunde liegende Dateisystem nicht sicher nachweisen, wird die Kapazität als nicht verfügbar angezeigt, statt die Container-Dateisystemschicht zu schätzen. Die Übersicht enthält:

- prozentual und in Bytes verbleibenden Platz im MEM-Datendateisystem;
- Größe der verwalteten Matrix-Medien;
- Gesamtanzahl der Mediendateien;
- Nutzung durch lokale Uploads;
- verfügbare Medienspeicher;
- Nutzung durch entfernten Mediencache, erzeugte Vorschaubilder und URL-Vorschau-Cache.

Das Inventar darunter zeigt pro Chatserver Matrix-Mediengröße, lokale Uploads, entfernten Cache, Medienspeicherverfügbarkeit und einen Link zum detaillierten Bereich **Dienste** des zugehörigen Chatservers.

Die Seite aktualisiert die schreibgeschützten Laufzeitnachweise automatisch und bietet zusätzlich eine zentrale Aktion Aktualisieren. Technische Dateisystempfade bleiben bewusst außerhalb der Flottentabelle.

## Detaillierte Speichernachweise

Öffnen Sie **Chatserver → <Server> → Dienste**, um die detaillierten Speichernachweise eines Chatservers zu prüfen.

Der Bereich Dienste kann zeigen:

- Matrix-Datenpfad;
- Medienspeicherpfad und Gesamtgröße;
- Anzahl der Mediendateien;
- Vorhandensein und Größe von `homeserver.yaml`;
- Vorhandensein und Größe des Matrix-Signaturschlüssels;
- Element-Daten- und Konfigurationspfade;
- Medienbereiche wie lokale Uploads, entfernter Cache, Vorschaubilder und URL-Vorschauen.

Technische Dateisystempfade sind Support- und Wiederherstellungsnachweise. Sie verbleiben im detaillierten Stack-Arbeitsbereich statt im Flotteninventar.

## Wiederherstellungsgrenze

Matrix-Medien funktionieren als Paar:

- PostgreSQL speichert Ereignisse, Metadaten und Medienreferenzen;
- das Synapse-Medienverzeichnis speichert die Dateiinhalte.

Eine wiederherstellbare Sicherung benötigt daher Datenbankdump und Medienverzeichnis. Synapse-Konfiguration, Signaturschlüssel und Element-Konfiguration sind ebenfalls relevant.

> [!WARNING]
> Nur das Medienverzeichnis ist keine vollständige Matrix-Sicherung. Nur PostgreSQL kann Medienreferenzen ohne Dateien hinterlassen.

## Kapazitätsplanung

Nutzen Sie **MEM-Datenspeicher verfügbar**, um das Dateisystem zu beobachten, das den konfigurierten MEM-Datenstamm tatsächlich trägt. Der Arbeitsbereich Speicher zeigt verfügbaren Platz, Gesamtkapazität und den verbleibenden Prozentsatz, wenn dieses Dateisystem im aktiven Laufzeitmodus sicher beobachtet werden kann. Die Startseite verwendet dieselbe maßgebliche MEM-Datenkapazitätsprojektion.

Beobachten Sie Medienwachstum zusammen mit Datenbank-, Sicherungs- und Migrationsarbeitsbereich. Der Installationsschwellwert ist keine dauerhafte Kapazitätsgarantie.

Entfernter Mediencache und erzeugte Ableitungen können Speicher belegen, obwohl lokale Benutzer wenig hochladen. Nutzen Sie das Flotteninventar, um Wachstum zu erkennen, und öffnen Sie anschließend den Bereich Dienste des Chatservers für detaillierte Bereichsnachweise.

## Manuelle Bereinigung vermeiden

Löschen Sie Signaturschlüssel, `homeserver.yaml`, Datenbank-Volume oder Medienordner nicht manuell. Dies kann Identität, Verlauf, Föderation oder Wiederherstellungstreue brechen.

Erstellen Sie eine geprüfte Sicherung und verwenden Sie einen ausdrücklich unterstützten Lebenszyklus-Workflow für Entfernung oder Wiederherstellung.

---

# Neuen Server live schalten und Produktion prüfen

Source: `docs/de/migrieren/go-live.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Neuen Server live schalten und Produktion prüfen

Die Liveschaltung überträgt die öffentlichen Proxy-Routen vom alten Dienstpfad auf die neue MEM-0.2.0-Laufzeit. Dies ist der folgenreichste Schritt des geführten Ablaufs.

## Ergebnis

Die neuen Matrix- und Element-Dienste besitzen die vorgesehenen öffentlichen Routen und bestehen die dauerhafte Produktionsprüfung. Die alte Quelle bleibt für Rollback und Annahme erhalten.

## Bevor Sie beginnen

Bestätigen Sie:

- neue normale Laufzeit privat gesund;
- Matrix-Hostname korrekt und unverändert;
- Element-Hostname korrekt;
- DNS zeigt bereits auf den Ziel-Ingress;
- aktives Zielzertifikat deckt die Hostnamen ab;
- aktueller Eigentümer der Nginx-Proxy-Manager-Routen ist bekannt;
- normale Nutzung des alten Servers ist gestoppt oder die Quelle final eingefroren;
- [Rollback-Grenzen](rollback.md) sind gelesen.

MEM ändert Nginx-Proxy-Manager-Routen. Es erstellt keine DNS-Einträge und verspricht während der Umschaltung kein fehlendes Zertifikat.

## Bereitschaft prüfen

Der Arbeitsbereich erstellt ein zeitlich begrenztes Bereitschafts- oder Vorschauergebnis. Prüfen Sie Blocker und Warnungen unmittelbar vor der Anwendung. Aktualisieren Sie eine abgelaufene Vorschau oder einen nach Zustandsänderung veralteten Nachweis.

Schließen Sie bei Bedarf Step-up ab.

## Routen veröffentlichen

MEM sichert den vorherigen Routenzustand, wählt das aktive Zertifikat und wendet die Matrix- und Element-Routen für die neue Laufzeit an.

Scheitert die Veröffentlichung, versucht MEM den vorherigen Zustand wiederherzustellen. Bei erfolgreicher Wiederherstellung bleibt der neue Server privat. Kann die Routenhoheit nicht bestätigt werden, stoppen Sie normale Wiederholungen und bestimmen Sie anhand erweiterter Wiederherstellungsnachweise, welche Laufzeit öffentlich ist.

## Produktion prüfen

Führen Sie die angezeigten Produktionsprüfungen aus. Prüfen Sie mindestens:

- Zielcontainer und Laufzeiteigentum;
- Nginx-Proxy-Manager-Ziele und Zertifikatsauswahl;
- öffentliche Matrix-Bereitschaft;
- öffentliche Element-Erreichbarkeit;
- Datenbank- und Migrationsnachweise;
- Warnungen zur Quellautorität oder unvollständigen Prüfungen.

## Grenze bei fehlgeschlagener Prüfung

Eine Routenänderung kann erfolgreich sein, obwohl eine spätere Prüfung scheitert. MEM führt nicht allein deshalb automatisch Rollback aus, weil dadurch ein erreichbarer neuer Server durch eine unsichere alte Route ersetzt werden könnte.

Bestimmen Sie zuerst die öffentliche Laufzeit. Wiederholen Sie sichere Prüfungen oder verwenden Sie erweiterte Wiederherstellungssteuerung. Drücken Sie Liveschaltung nicht wiederholt bei unbekannter Routenhoheit.

Weiter: [Migration annehmen und abschließen](finish.md).

---

# Make the new server live and verify production

Source: `docs/migrate/go-live.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Make the new server live and verify production

Go-live transfers public proxy-route ownership from the old service path to the new MEM 0.2.0 runtime. It is the highest-impact step in the guided workflow.

## Outcome

The new Matrix and Element services own the intended public routes and pass durable production verification. The old source remains retained for the rollback and acceptance boundary.

## Before you begin

Confirm:

- the new normal runtime is privately healthy;
- the Matrix hostname is correct and unchanged;
- the Element hostname is correct;
- DNS already resolves to the target ingress path;
- an active target certificate covers the public hostnames;
- you understand the current Nginx Proxy Manager route owner;
- users have stopped normal use of the old server, or the final-frozen source is still frozen;
- you have read [Rollback boundaries](rollback.md).

MEM changes Nginx Proxy Manager routes. It does not create DNS records and does not promise to issue a missing certificate during cutover.

## Review readiness

The workspace creates a time-bounded readiness or preview result. Review all blockers and warnings immediately before applying. If the preview expires or the underlying state changes, refresh it rather than reusing stale confirmation data.

Complete step-up authentication when required.

## Publish routes

MEM snapshots the previous route state, selects the active certificate, and applies the Matrix and Element route changes for the new runtime.

If route publication fails, MEM attempts to restore the previous route state. When restoration succeeds, the new server remains private. When route ownership cannot be confirmed, stop the normal retry path and use the advanced recovery evidence to determine which runtime is public.

## Verify production

Run the production checks shown by the workspace. They cover the durable runtime and route ownership rather than only a browser page. Review at least:

- target container and runtime ownership;
- Nginx Proxy Manager route targets and certificate selection;
- public Matrix readiness;
- public Element availability;
- database and migration verification evidence;
- any warnings about source authority or incomplete checks.

## Failed verification boundary

A route change may have succeeded even when a later production check fails. MEM does not automatically roll back solely because verification failed, because doing so could replace a reachable new server with an uncertain old route.

First determine which server is public. Rerun safe checks where appropriate or use the advanced recovery controls. Do not repeatedly press go-live while route ownership is unknown.

Next: [Accept and finish the migration](finish.md).

---

# Verify and complete the restored server

Source: `docs/backups-and-restores/verify-and-complete.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Verify and complete the restored server

Standard Recreate creates the stack. The Restore Workspace remains active until public checks pass and the operator explicitly completes handover.

## Run restored-server checks

In **Check the restored server**, run a fresh check. MEM invokes the registered stack's Doctor workflow and projects safe Matrix, Element, route, and connectivity results back into the Restore Workspace.

A URL recorded in configuration is not proof of reachability. Run the check after DNS, NPM, certificate, or network changes rather than relying on an earlier result.

## Review the result

Confirm that:

- the restored stack is registered in **Chat servers**;
- Matrix and Element containers are healthy;
- the Matrix and Element public routes resolve to the intended target;
- public readiness checks pass;
- the Matrix server identity matches the backup;
- the expected users are visible after Users synchronization;
- media needed for recent rooms is present;
- federation policy and TURN state match the recovery plan.

Also sign in to Element with a known Matrix account. Test room history, new messages, media, and—where relevant—federation and voice/video. MEM's automated checks do not prove user-held end-to-end encryption keys or a complete client experience.

## Complete handover

When the latest public checks pass:

1. Expand **Complete and hand over**.
2. Review final verification and any warnings.
3. Acknowledge completion.
4. Open the restored stack from the workspace.
5. Keep the restore-session ID in the change or incident record.

Handover marks the Restore Workspace completed. It does not delete the source backup, private-test history, logs, evidence, or support report.

## After handover

- Create a fresh backup of the recovered stack after confirming service.
- Verify the new backup appears in the Backup Catalog.
- Decide how long to retain the pre-restore source and any old stopped runtime.
- Review users, federation, TURN, storage, and diagnostics from the normal stack workspace.

A completed workspace cannot be cancelled. Normal stack lifecycle and backup retention rules apply from this point.

---

# Run daily operations safely

Source: `docs/chat-servers/daily-operations.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Run daily operations safely

## Outcome

Operate a healthy chat server without turning every observation into a container mutation.

## Routine checklist

For normal operation:

1. open **Chat servers** and check the latest status and verification time;
2. open the stack workspace and Refresh when current state matters;
3. use **Open Element** for a real user-facing check;
4. run **Doctor** after DNS, certificate, route, firewall, image, or host changes;
5. review recent operations for running, failed, or rolled-back work;
6. synchronize Matrix users before account administration;
7. inspect TURN and federation after related platform changes;
8. create a backup before destructive or identity-affecting work.

## Interpret status carefully

A green historical status proves what the last verification observed. It is not continuous monitoring. A browser can also retain the latest Doctor result only for the current session.

When a user reports an outage, create fresh evidence instead of relying on an old timestamp.

## Use dedicated workflows

The Stack Workspace intentionally avoids generic container start, stop, restart, and YAML-edit controls. Safe operations may need candidate validation, step-up, a durable operation journal, verification, and rollback.

Use:

- the Users workflow for Matrix accounts;
- Voice & video for TURN changes;
- Federation for federation policy;
- Backups and Recovery for data protection;
- Diagnostics for current evidence;
- the Delete workflow for runtime retirement.

Use Portainer or host Docker commands as emergency evidence or outage tooling, not as the normal source of truth for MEM-owned configuration.

## Protect the administration surface

Keep the Control Plane private. It has Docker socket and MEM-owned filesystem authority. Use a trusted LAN, VPN, management network, or SSH tunnel.

Do not include passwords, TOTP secrets, recovery codes, Matrix access tokens, TURN secrets, signing keys, or private configuration values in support notes.

## Before planned change

Record the stack slug, current public hosts, last verified time, recent operation state, and latest usable backup. After the change, run Doctor and retain the new operation or report reference.

---

# Wiederhergestellten Server prüfen und übergeben

Source: `docs/de/backups-und-wiederherstellen/pruefen-und-abschliessen.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Wiederhergestellten Server prüfen und übergeben

Die Standard-Neuerstellung erstellt den Stack. Der Arbeitsbereich bleibt aktiv, bis öffentliche Prüfungen bestehen und der Betreiber die Übergabe bestätigt.

## Prüfungen ausführen

Unter **Wiederhergestellten Server prüfen** eine aktuelle Prüfung starten. MEM verwendet den Doctor des registrierten Stacks und projiziert sichere Matrix-, Element-, Routen- und Konnektivitätsergebnisse in den Arbeitsbereich.

Eine gespeicherte URL ist kein Erreichbarkeitsnachweis. Nach DNS-, NPM-, Zertifikats- oder Netzwerkänderungen erneut prüfen.

## Ergebnis kontrollieren

Bestätigen Sie:

- Stack ist unter **Chatserver** registriert;
- Matrix- und Element-Container sind gesund;
- öffentliche Routen zeigen auf das beabsichtigte Ziel;
- öffentliche Bereitschaftsprüfungen bestehen;
- Matrix-Serveridentität entspricht der Sicherung;
- erwartete Benutzer sind nach Synchronisierung sichtbar;
- benötigte Medien sind vorhanden;
- Föderationsrichtlinie und TURN-Zustand entsprechen dem Plan.

Melden Sie sich zusätzlich mit einem bekannten Matrix-Konto bei Element an. Prüfen Sie Raumverlauf, neue Nachrichten, Medien sowie gegebenenfalls Föderation und Sprache/Video. Automatische Prüfungen beweisen keine benutzerseitigen Verschlüsselungsschlüssel.

## Übergabe abschließen

1. **Abschließen und übergeben** erweitern.
2. Letzte Prüfung und Warnungen lesen.
3. Abschluss bestätigen.
4. Wiederhergestellten Stack aus dem Arbeitsbereich öffnen.
5. Restore-ID im Change- oder Incident-Datensatz behalten.

Die Übergabe markiert den Arbeitsbereich als abgeschlossen. Quelle, privater Testverlauf, Logs, Nachweise und Supportbericht werden nicht gelöscht.

## Danach

- Neue Sicherung des wiederhergestellten Stacks erstellen.
- Katalogeintrag prüfen.
- Aufbewahrung von alter Quelle und gestopptem Runtime entscheiden.
- Benutzer, Föderation, TURN, Speicher und Diagnosen im normalen Stack-Arbeitsbereich prüfen.

Ein abgeschlossener Arbeitsbereich kann nicht abgebrochen werden.

---

# Täglichen Betrieb sicher durchführen

Source: `docs/de/chat-servers/taeglicher-betrieb.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Täglichen Betrieb sicher durchführen

## Ergebnis

Betreiben Sie einen gesunden Chatserver, ohne jede Beobachtung in eine Containeränderung umzuwandeln.

## Routinecheckliste

Für den normalen Betrieb:

1. öffnen Sie **Chatserver** und prüfen Sie Status und letzten Prüfzeitpunkt;
2. öffnen Sie den Arbeitsbereich und aktualisieren Sie bei Bedarf;
3. verwenden Sie **Element öffnen** für eine echte Benutzerprüfung;
4. führen Sie **Diagnose** nach DNS-, Zertifikats-, Routen-, Firewall-, Abbild- oder Hoständerungen aus;
5. prüfen Sie aktuelle Vorgänge auf laufende, fehlgeschlagene oder zurückgesetzte Arbeit;
6. synchronisieren Sie Matrix-Benutzer vor Kontoverwaltung;
7. prüfen Sie TURN und Föderation nach passenden Plattformänderungen;
8. erstellen Sie vor zerstörerischen oder identitätsrelevanten Arbeiten eine Sicherung.

## Status richtig interpretieren

Ein grüner historischer Status beweist nur die letzte Beobachtung. Er ist kein kontinuierliches Monitoring. Auch das letzte Diagnoseergebnis kann nur in der aktuellen Browsersitzung vorliegen.

Bei einer Störungsmeldung erstellen Sie aktuelle Nachweise statt sich auf einen alten Zeitstempel zu verlassen.

## Eigene Workflows verwenden

Der Stack-Arbeitsbereich vermeidet absichtlich allgemeine Container-Start-, Stopp-, Neustart- und YAML-Bearbeitungsaktionen. Sichere Änderungen können Kandidatenprüfung, Step-up, dauerhaftes Vorgangsjournal, Verifikation und Rollback benötigen.

Verwenden Sie:

- Benutzer-Workflow für Matrix-Konten;
- Sprache & Video für TURN;
- Föderation für Richtlinien;
- Sicherungen und Wiederherstellung für Datenschutz;
- Diagnose für aktuelle Nachweise;
- Löschen für Laufzeitstilllegung.

Portainer oder Host-Docker-Befehle sind Notfallnachweise oder Ausfallwerkzeuge, nicht normale Quelle der Wahrheit für MEM-eigene Konfiguration.

## Verwaltungsoberfläche schützen

Halten Sie die Control Plane privat. Sie besitzt Docker-Socket- und Dateisystemautorität. Verwenden Sie vertrauenswürdiges LAN, VPN, Managementnetz oder SSH-Tunnel.

Fügen Sie keine Passwörter, TOTP-Geheimnisse, Wiederherstellungscodes, Matrix-Zugriffstoken, TURN-Geheimnisse, Signaturschlüssel oder privaten Konfigurationswerte in Supportnotizen ein.

## Vor geplanter Änderung

Erfassen Sie Stack-Slug, öffentliche Hosts, letzten Prüfzeitpunkt, aktuellen Vorgangszustand und letzte nutzbare Sicherung. Führen Sie danach Diagnose aus und bewahren Sie neue Vorgangs- oder Berichtsreferenz auf.

---

# Fehlerbehebung bei der Installation

Source: `docs/de/installation/installation-fehlerbehebung.md`
Locale: de
Section: Installation
Status: supported
Applies to: 0.2.x

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

---

# Migration annehmen und abschließen

Source: `docs/de/migrieren/finish.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Migration annehmen und abschließen

Die Annahme ist die Grenze, an der der geprüfte MEM-0.2.0-Server zum maßgeblichen verwalteten Server wird und in den normalen Sicherungs- und Wiederherstellungslebenszyklus eintritt.

## Ergebnis

Die Migration ist dauerhaft angenommen, die alte Quelle besitzt einen Aufbewahrungszeitraum, die erste native MEM-Sicherung ist erstellt oder gezielt wiederholbar und die Staging-Bereinigung kann abgeschlossen werden.

## Voraussetzungen

Schließen Sie erst ab, wenn öffentliche Routenhoheit bekannt ist, die Produktionsprüfung bestanden und gültig ist, Matrix-Identität und Dienst korrekt sind, Betreiber fehlende Rücksynchronisation verstehen und die Quelle für den gewählten Zeitraum erhalten bleiben kann.

## Aufbewahrung der alten Quelle wählen

Wählen Sie die Dauer. Der Eintrag ist ein betrieblicher Sicherheitsnachweis und keine Anweisung an MEM, den Quellhost zu löschen. Halten Sie ihn gemäß Rollback-Plan ausgeschaltet, isoliert oder kontrolliert, bewahren Sie Daten und Nachweise jedoch auf.

## Migrierten Server annehmen

Schließen Sie Bestätigung und Step-up ab. Die Annahme zeichnet das Ziel als maßgeblich auf und schließt die normale Übernahmegrenze.

Danach ist die neue MEM-Laufzeit Produktion, neue Nachrichten und Kontoänderungen werden nicht zurückkopiert, die Quelle wird niemals automatisch gelöscht und Rohartefakte bleiben Migrationsnachweise.

## Erste native Sicherung

Der Abschluss startet die erste native MEM-Sicherung. Erst an dieser Grenze tritt der migrierte Server in Sicherungskatalog und Wiederherstellungsarbeitsbereich ein.

Scheitert die Basissicherung nach der Annahme, bleibt die Annahme dauerhaft. Wiederholen Sie nicht die Annahme, sondern nur die erste native Sicherung über die angebotene Aktion.

## Abschluss und Bereinigung

Laden oder bewahren Sie den Abschlussbericht auf. MEM versucht danach, erfolgreiche Staging-Ressourcen zu entfernen. Ein Bereinigungsfehler ist eine Folgeoperation und macht Annahme oder erfolgreiche Sicherung nicht rückgängig.

Prüfen Sie angenommenen/abgeschlossenen Status, normalen Chatserver-Arbeitsbereich, ersten Sicherungskatalog-Eintrag, sichtbaren Aufbewahrungsnachweis und abgeschlossene oder sicher wiederholbare Staging-Bereinigung.

Weiter: [Migrationsressourcen aufbewahren oder bereinigen](cleanup.md).

---

# Installation troubleshooting

Source: `docs/installation/troubleshooting.md`
Locale: en
Section: Installation
Status: supported
Applies to: 0.2.x

# Installation troubleshooting

Troubleshoot the stage that failed. Do not erase durable state before collecting evidence.

## Bootstrap does not start

Run:

```bash
sudo ./install.sh --dry-run
sudo docker info
sudo docker compose version
sudo docker ps -a --filter name=mem-control-plane
sudo docker ps -a --filter name=mem-installer
```

Common causes include:

- unsupported Ubuntu version;
- insufficient memory or disk;
- missing package repository access;
- Docker daemon unavailable;
- selected Control Plane port already in use;
- Control Plane image pull failure.

When the Control Plane container exists but is stopped, inspect its logs before restarting it:

```bash
sudo docker logs mem-control-plane
```

For a **real** bootstrap run, also inspect the root-only bootstrap evidence under:

```text
/var/log/mem/bootstrap/
```

`latest.log` points to the most recent transcript. A failed run also leaves `mem-bootstrap-<UTC-run-id>-failure.txt` with the failed phase, safe operation, mutation state, retained volume, bounded/redacted runtime evidence, and suggested recovery commands. Collect this report before deleting a failed Control Plane container.

A `--dry-run` intentionally creates no persistent transcript because dry-run must remain non-mutating.

## Setup code is missing

Retrieve it from the host:

```bash
sudo ./install.sh --show-setup-token
```

If the Control Plane data volume does not exist or contains no token, do not invent one in the browser. Re-run the official bootstrap and review why persistence was unavailable.

## Browser cannot reach the Control Plane

Check:

```bash
sudo docker ps --filter name=mem-control-plane
sudo ss -ltnp | grep 8443
sudo docker logs mem-control-plane
```

Confirm the selected port, firewall, VPN, or SSH tunnel. A self-signed certificate warning is expected; a connection refusal is not.

## Legacy Control Plane runtime name detected

A container named `mem-installer` with the reviewed volume `mem-installer-data` is an earlier MEM 0.2.0 Control Plane identity, not a MEM 0.1.0 source. Run the current bootstrap dry run and review the migration plan:

```bash
sudo ./install.sh --dry-run
```

The real run reuses the existing volume and certificates, verifies the canonical runtime, and rolls back on failure. Do not manually rename the volume. If both `mem-control-plane` and `mem-installer` exist, or only `mem-installer-data` remains, bootstrap stops without mutation and requires investigation.

## Setup reports a legacy installation

If `mem-api` or `mem-web` is detected, stop. Use MEM Migrate.

Do not rename or delete those containers to bypass detection. They identify the old application boundary and may point to data required for migration.

## Host checks show unexpected MEM resources

Record:

```bash
sudo docker ps -a
sudo docker network ls
sudo docker volume ls
```

Use the repair/investigation path and Diagnostics. Determine whether the resources belong to a failed current install, a test stack, a restore workspace, a migration staging runtime, or a legacy deployment.

## Certificate operation fails

Check:

- base domain and deSEC zone;
- token correctness and zone permission;
- outbound HTTPS and DNS;
- ACME staging versus production;
- DNS propagation;
- existing conflicting TXT records;
- NPM readiness and credentials.

Do not paste the deSEC token into a support report.

A browser timeout can occur while backend cleanup continues. Refresh the domain page and inspect the recorded last result before retrying.

## Installation waits for user

Open the linked domain or ingress action, correct the readiness issue, and resume the existing installation.

`WaitingForUser` is a durable pause, not a reason to create a second install plan.

## Installation fails

Use the persistent failure panel and **Run diagnostics**. When the Control Plane is available, use **Open diagnostics** to review the correlated incident and technical-event evidence before falling back to raw logs.

Record:

- installation ID;
- failed step;
- incident ID;
- trace or correlation ID;
- expected and observed state;
- bounded support report.

The active Diagnostics programme stores safe operator evidence separately from raw technical logs. When the API is unavailable, fall back to:

```bash
sudo docker logs mem-control-plane
```

and the configured persistent control-plane log directory.

## Verification warns or fails

Open each verification check. Confirm:

- install run and all steps;
- Docker;
- `mem-postgres`;
- `mem-npm`;
- NPM initialization and API access;
- active main certificate;
- private-key match;
- NPM certificate visibility.

Do not continue to public stack creation while a required platform check is failed.

## First-owner bootstrap fails

Confirm that:

- no completed Platform Owner already exists;
- the setup code is exact;
- the bootstrap grant has not expired;
- username and password validation passed;
- TOTP time is correct on both server and authenticator device;
- recovery codes were stored before finishing.

Never include passwords, TOTP secrets, recovery codes, cookies, setup codes, or private keys in support material.

## Installation support report

When Setup or installation fails after the Control Plane is running, prefer the bounded MEM installation support report over copying raw logs.

Open **Setup troubleshooting** from the Setup activity page when available. It keeps the exact Installation ID, shows the runtime-correct host command, provides the bounded report download, and keeps raw Docker logs as a last-resort fallback.

From the Setup activity, Setup troubleshooting, or Finish page, use **Download support report**. The report includes the reviewed plan fingerprint, safe server-check snapshot, persisted installation timeline, related diagnostic events, recovery guidance, runtime identity, and bounded Docker evidence when it is available.

The report deliberately omits credentials, passwords, DNS provider tokens, private keys, raw authorization data, recovery codes, connection strings, complete container environment arrays, unrestricted command output, and raw CLEF files.

If the browser route is unavailable but the Control Plane container can run commands, generate the same report directly from the host.

Latest installation in production:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report --latest \
  > "mem-install-report-$(date -u +%Y%m%dT%H%M%SZ).json"
```

Known installation:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report \
  --installation-id "<installation-id>" \
  --format json \
  > mem-install-report.json
```

Containerized development:

```bash
docker exec mem-control-plane-dev \
  dotnet Api.dll support install-report --latest
```

If a failure happened before an Installation ID was available but a technical trace reference exists:

```bash
sudo docker exec mem-control-plane \
  dotnet Api.dll support install-report \
  --trace-id "<trace-id>" \
  --format text
```

Docker evidence is included by default. Use `--no-docker-evidence` when only the durable Setup and diagnostic records are required. The command writes the report to stdout, writes operational errors to stderr, does not prompt for credentials, and will not create a missing Control Plane database.

As a last-resort raw fallback:

```bash
sudo docker logs --timestamps --tail 500 mem-control-plane
```

or during containerized development:

```bash
docker logs --timestamps --tail 500 mem-control-plane-dev
```

Raw logs can contain operational metadata and should be reviewed before external sharing. The generated support report is the preferred artefact.

---

# Accept and finish the migration

Source: `docs/migrate/finish.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Accept and finish the migration

Acceptance is the boundary where the verified MEM 0.2.0 server becomes the authoritative managed server and enters the normal backup and restore lifecycle.

## Outcome

The migration is durably accepted, the legacy source has a recorded retention period, the first native MEM backup is created or explicitly retryable, and migration-owned staging cleanup can complete.

## Acceptance prerequisites

Finish only after:

- public route ownership is known;
- production verification has passed and remains valid;
- the Matrix identity and expected service are correct;
- operators understand that new target changes will not synchronize back to the old source;
- the legacy source can remain retained for the chosen period.

## Choose legacy retention

Select the retention duration for the old server. Retention is an operational safety record; it is not an instruction for MEM to delete the source host. Keep the source powered down, isolated, or otherwise controlled according to your rollback plan, but preserve its data and evidence.

## Accept the migrated server

Complete the confirmation and step-up prompts. Acceptance records the target as authoritative and closes the normal source-to-target adoption boundary.

After acceptance:

- the new MEM runtime is the production system;
- new messages and account changes are not copied back to MEM 0.1.0;
- the legacy source is retained, never automatically deleted;
- raw migration artifacts remain migration evidence rather than normal backups.

## First native backup

Finish starts the first native MEM backup of the accepted stack. That backup is the boundary at which the migrated server enters the ordinary Backup Catalog and Restore Workspace lifecycle.

If the baseline backup fails after acceptance, acceptance remains durable. Do not repeat migration acceptance. Use the workspace action to retry only the first native backup and review its evidence.

## Completion and cleanup

Download or retain the completion report. MEM then attempts to remove successful migration staging resources. A cleanup failure is a follow-up operation and does not undo acceptance or the first successful backup.

Verify:

- the session shows accepted/completed state;
- the normal chat-server workspace opens;
- the first native Backup Catalog entry is present when backup succeeds;
- the old source retention record is visible;
- staging cleanup is complete or safely retryable.

Next: [Retain or clean up migration resources](cleanup.md).

---

# Cancel, retry, and handle restore failures

Source: `docs/backups-and-restores/cancel-retry-failure.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Cancel, retry, and handle restore failures

Restore failures are recoverable only when the operator preserves the source and understands which mutations completed.

## Safe cancellation

The Restore Workspace shows **Cancel** only when the backend reports that cancellation is safe.

Cancellation is unavailable when:

- the restore is already completed, cancelled, or otherwise terminal;
- a filesystem, database, Docker, or route operation is queued or running.

MEM will not cancel halfway through a live mutation. Wait for the operation to finish or fail, then refresh the workspace.

A successful cancellation:

- closes the non-terminal workspace;
- releases temporary restore target claims;
- preserves the backup, logs, evidence, support material, and audit record;
- does not delete a completed restored server.

## Retry boundary

Do not repeatedly click an action after a timeout. Refresh the workspace and inspect the operation status first. Durable operation history may show that the server completed work even when the browser lost the response.

Before retrying:

1. Read the stage summary and blocker list.
2. Inspect **Activity**, **Evidence**, and **Logs**.
3. Record the operation ID and event code.
4. Confirm whether database, containers, routes, or target claims were created.
5. Use a provided cleanup action for a failed Standard Recreate when available.
6. Rerun preflight because target ownership may have changed.

## Source failure versus target failure

Source problems include missing payload files, invalid checksums, absent signing identity, or failed database import. Target problems include occupied slugs or hosts, unavailable platform services, route ownership, and runtime startup failures.

Do not edit a catalog payload to make a source error disappear. Create a new verified backup or re-import the original portable archive.

## No automatic rollback

Standard Recreate explicitly acknowledges that no automatic rollback is promised. A failed operation can leave partial production resources that require guided cleanup or careful operator review.

Never delete `mem-postgres` databases, MEM-owned directories, Docker networks, or NPM routes solely because a screen reports failure. First establish ownership from Configuration, Activity, and Evidence.

## Escalation evidence

Generate a [restore support report](evidence-logs-support.md) before host-level cleanup. Keep the restore-session ID, catalog entry ID, target values, latest error code, operation IDs, timestamps, and any cleanup result.

---

# Remove a chat server

Source: `docs/chat-servers/remove.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Remove a chat server

## Outcome

Remove the active Matrix and Element runtime and public routes while understanding that the normal list-page action keeps the database and local files.

## Before you begin

Removal interrupts all users of the stack. Before proceeding:

- create and verify a recent backup;
- record the Matrix and Element public hosts;
- confirm there is no running migration, restore, backup, TURN, or federation operation;
- decide whether retained database and files are intentional;
- tell users that Element access and Matrix traffic will stop.

## Current Delete behaviour

From **Chat servers**, select **Delete** beside the stack and review the confirmation.

The current operator action requests removal of:

- the Matrix and Element containers;
- their public Nginx Proxy Manager routes.

It deliberately keeps:

- the stack PostgreSQL database;
- local Matrix and Element files.

Recent step-up verification is required before the server accepts the high-risk mutation.

## Durable removal operation

After validation and step-up, MEM accepts removal as a durable server operation and returns an operation reference. The browser tracks stages such as route removal, Matrix and Element container removal, retained-data handling, runtime-inventory finalisation, and manifest removal.

The removal continues under a bounded Control Plane lifetime if the page is refreshed, the tab is closed, or the initiating HTTP connection disappears. Returning to **Chat servers** restores progress tracking for the accepted operation without submitting a second destroy request.

MEM treats already-absent routes and containers as safe idempotent skips. This allows a new explicitly confirmed operation to resume cleanup after an earlier partial attempt, for example when one container has already been removed and another remains stopped. A real cleanup error ends the normal list-page operation as failed instead of silently discarding the remaining ownership record.

> [!IMPORTANT]
> Do not press Delete again merely because the browser lost progress temporarily. Reopen **Chat servers** and let MEM reconnect to the existing operation. If the operation reaches a terminal failure, open Diagnostics and review the failed stage before confirming a new attempt.

> [!IMPORTANT]
> A stack disappearing from the active runtime list does not prove that its data was erased. The success notice explicitly distinguishes runtime removal from database and file handling.

## Recover a stack that is missing from Chat servers

The normal **Chat servers** inventory is backed by active runtime manifests. A failed creation or interrupted removal can leave durable `RuntimeStack`, service-instance, or route records after the manifest has disappeared. Such a stack may remain visible in Docker, Nginx Proxy Manager, or **Diagnostics → Runtime reconciliation** while no Delete button is available on the normal list.

Runtime Reconciliation compares the manifest store, durable database rows, service identities, route targets, and effective NPM state. It classifies manifestless active records as one of the following:

- **Interrupted removal** — one or more recorded NPM routes are already absent, consistent with teardown that stopped before durable finalisation.
- **Incomplete creation** — durable service records exist, but no runtime manifest or public routes were recorded.
- **Unlisted runtime** — the recorded services and NPM routes still agree, but the manifest is missing.
- **Ownership ambiguous** or **Inspection unavailable** — evidence is conflicting or incomplete, so no destructive action is offered.

Where MEM can prove bounded ownership, the page offers a server-authored action such as **Finish removal**, **Clean incomplete creation**, or **Remove unlisted stack**. The operator must:

1. review the classification and service/route counts;
2. type `REMOVE <exact-slug>`;
3. complete recent step-up verification;
4. track the accepted durable destroy operation.

The recovery request is deliberately constrained to exact containers and NPM routes. It retains the Matrix database, database role, and local stack files, and it keeps `force=false`. Before mutation, MEM revalidates recorded container name and ID, MEM ownership labels, Control Plane instance identity, runtime mode, service identity, data-path mounts, public hostname, NPM upstream target, port, and certificate identity. Any mismatch fails closed.

Already-absent routes or containers are safe idempotent skips. A stopped ownership-verified container can be removed. MEM then removes stale service and route rows, marks the runtime stack destroyed, releases the original slug, and records the terminal operation result.

> [!WARNING]
> Do not use broad Docker cleanup, manual SQLite edits, or direct NPM deletion to make reconciliation counters look clean. If MEM labels ownership as ambiguous, preserve the evidence and investigate it rather than forcing removal.

## After removal

Confirm that:

- the stack no longer appears as an active runtime;
- the public routes no longer serve the stack;
- the durable destroy operation reached **Succeeded** rather than remaining running or failed;
- retained data paths and database ownership are documented.

Do not manually delete retained resources merely because the stack is absent from the list. Stronger data removal or recovery decisions must be deliberate and should follow the supported lifecycle tooling available for the installed release.

## When not to use Delete

Do not use runtime deletion as a substitute for:

- migration to a new server;
- restore rehearsal;
- hostname change;
- temporary outage troubleshooting;
- clearing a failed creation without first checking the operation and manifest state.

Use the bounded migration, recovery, or diagnostics workflow for those outcomes.

---

# Wiederherstellung abbrechen, wiederholen und Fehler behandeln

Source: `docs/de/backups-und-wiederherstellen/abbruch-und-fehler.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Wiederherstellung abbrechen, wiederholen und Fehler behandeln

Eine fehlgeschlagene Wiederherstellung bleibt nur dann kontrollierbar, wenn Quelle und Nachweise erhalten bleiben und klar ist, welche Änderungen bereits abgeschlossen wurden.

## Sicher abbrechen

**Abbrechen** erscheint nur, wenn der Server den Abbruch als sicher meldet.

Abbruch ist nicht verfügbar, wenn:

- der Versuch bereits abgeschlossen, abgebrochen oder anderweitig terminal ist;
- ein Datei-, Datenbank-, Docker- oder Routenvorgang wartet oder läuft.

MEM bricht keine laufende Mutation mittendrin ab. Warten Sie auf Abschluss oder Fehler und aktualisieren Sie den Arbeitsbereich.

Ein erfolgreicher Abbruch:

- schließt den nicht terminalen Arbeitsbereich;
- gibt temporäre Zielreservierungen frei;
- bewahrt Sicherung, Logs, Nachweise, Supportmaterial und Auditverlauf;
- löscht keinen fertig erstellten Stack.

## Wiederholen

Nach Timeout nicht mehrfach klicken. Zuerst aktualisieren und dauerhaften Vorgangsstatus prüfen. Der Server kann die Arbeit abgeschlossen haben, obwohl der Browser keine Antwort erhielt.

Vor einem erneuten Versuch:

1. Stufenzusammenfassung und Blocker lesen.
2. **Aktivität**, **Nachweise** und **Logs** prüfen.
3. Vorgangs-ID und Ereigniscode notieren.
4. Prüfen, ob Datenbank, Container, Routen oder Reservierungen erstellt wurden.
5. Verfügbare geführte Bereinigung für fehlgeschlagene Standard-Neuerstellung verwenden.
6. Vorprüfung erneut ausführen.

## Quellen- und Zielfehler

Quellenfehler sind etwa fehlende Dateien, ungültige Prüfsummen, fehlende Signaturidentität oder fehlerhafter Datenbankimport. Zielfehler sind belegte Slugs/Hosts, nicht verfügbare Plattformdienste, Routeneigentum oder Startfehler.

Bearbeiten Sie keinen Katalog-Payload direkt. Erstellen Sie eine neue geprüfte Sicherung oder importieren Sie das Originalarchiv erneut.

## Kein automatischer Rollback

Die Standard-Neuerstellung verspricht keinen automatischen Rollback. Ein Fehler kann teilweise Produktionsressourcen hinterlassen. Löschen Sie keine Datenbanken, MEM-Verzeichnisse, Netze oder NPM-Routen allein aufgrund einer Fehlermeldung. Stellen Sie zuerst Eigentum aus Konfiguration, Aktivität und Nachweisen fest.

Vor Host-Bereinigung einen [Supportbericht](nachweise-logs-support.md) erzeugen und Restore-ID, Katalog-ID, Zielwerte, Fehlercode, Vorgangs-IDs und Bereinigungsergebnis behalten.

---

# Chatserver entfernen

Source: `docs/de/chat-servers/entfernen.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Chatserver entfernen

## Ergebnis

Entfernen Sie aktive Matrix- und Element-Laufzeit sowie öffentliche Routen und verstehen Sie, dass die normale Listenaktion Datenbank und lokale Dateien behält.

## Voraussetzungen

Die Entfernung unterbricht alle Benutzer. Vorher:

- aktuelle Sicherung erstellen und prüfen;
- öffentliche Matrix- und Element-Hosts erfassen;
- sicherstellen, dass keine Migration, Wiederherstellung, Sicherung, TURN- oder Föderationsoperation läuft;
- entscheiden, ob behaltene Datenbank und Dateien beabsichtigt sind;
- Benutzer über den Ausfall informieren.

## Aktuelles Löschverhalten

Wählen Sie unter **Chatserver** beim Stack **Löschen** und prüfen Sie die Bestätigung.

Die aktuelle Operatoraktion entfernt:

- Matrix- und Element-Container;
- öffentliche Nginx-Proxy-Manager-Routen.

Sie behält absichtlich:

- PostgreSQL-Datenbank des Stacks;
- lokale Matrix- und Element-Dateien.

Vor der Hochrisikoänderung ist aktuelle Step-up-Prüfung erforderlich.

## Dauerhafter Entfernungsvorgang

Nach Validierung und Step-up nimmt MEM die Entfernung als dauerhaften Servervorgang an und gibt eine Vorgangsreferenz zurück. Der Browser verfolgt Phasen wie Routenentfernung, Entfernung der Matrix- und Element-Container, Behandlung beibehaltener Daten, Abschluss des Laufzeitinventars und Entfernung des Manifests.

Die Entfernung läuft unter einer begrenzten Lebensdauer der Steuerungsebene weiter, wenn die Seite aktualisiert, der Tab geschlossen oder die auslösende HTTP-Verbindung getrennt wird. Bei der Rückkehr zu **Chatserver** stellt MEM die Fortschrittsverfolgung des angenommenen Vorgangs wieder her, ohne einen zweiten Löschauftrag zu senden.

Bereits fehlende Routen und Container werden als sichere idempotente Überspringungen behandelt. Dadurch kann ein neu und ausdrücklich bestätigter Vorgang die Bereinigung nach einem früheren Teilversuch fortsetzen, zum Beispiel wenn ein Container bereits entfernt wurde und ein anderer gestoppt zurückblieb. Ein echter Bereinigungsfehler beendet die normale Listenaktion als fehlgeschlagen, statt den verbleibenden Besitznachweis stillschweigend zu verwerfen.

> [!IMPORTANT]
> Drücken Sie nicht erneut auf **Löschen**, nur weil der Browser den Fortschritt vorübergehend verloren hat. Öffnen Sie **Chatserver** erneut und lassen Sie MEM die Verbindung zum vorhandenen Vorgang wiederherstellen. Bei einem endgültigen Fehler öffnen Sie die Diagnose und prüfen die fehlgeschlagene Phase, bevor Sie einen neuen Versuch bestätigen.

> [!IMPORTANT]
> Das Verschwinden aus der aktiven Laufzeitliste beweist keine Datenlöschung. Die Erfolgsmeldung unterscheidet ausdrücklich Laufzeitentfernung von Datenbank- und Dateibehandlung.

## Einen Stack bereinigen, der unter Chatservern fehlt

Das normale Inventar **Chatserver** wird aus aktiven Laufzeitmanifesten aufgebaut. Eine fehlgeschlagene Erstellung oder unterbrochene Entfernung kann dauerhafte `RuntimeStack`-, Dienstinstanz- oder Routendatensätze zurücklassen, nachdem das Manifest verschwunden ist. Ein solcher Stack kann weiterhin in Docker, Nginx Proxy Manager oder unter **Diagnose → Laufzeit-Abgleich** erscheinen, obwohl in der normalen Liste keine Löschaktion verfügbar ist.

Der Laufzeit-Abgleich vergleicht Manifest-Speicher, dauerhafte Datenbankzeilen, Dienstidentitäten, Routenziele und den wirksamen NPM-Zustand. Aktive Datensätze ohne Manifest werden wie folgt klassifiziert:

- **Unterbrochene Entfernung** — mindestens eine gespeicherte NPM-Route fehlt bereits; dies entspricht einem Abbau, der vor dem dauerhaften Abschluss endete.
- **Unvollständige Erstellung** — dauerhafte Dienstdatensätze sind vorhanden, aber Laufzeitmanifest und öffentliche Routen fehlen.
- **Nicht gelistete Laufzeit** — gespeicherte Dienste und NPM-Routen stimmen überein, aber das Manifest fehlt.
- **Besitzzuordnung uneindeutig** oder **Prüfung nicht verfügbar** — Nachweise sind widersprüchlich oder unvollständig; daher wird keine destruktive Aktion angeboten.

Wenn MEM begrenzten Besitz eindeutig belegen kann, bietet die Seite eine serverseitig festgelegte Aktion wie **Entfernung abschließen**, **Unvollständige Erstellung bereinigen** oder **Nicht gelisteten Stack entfernen**. Der Operator muss:

1. Klassifizierung sowie Dienst- und Routenzahlen prüfen;
2. `REMOVE <exakter-slug>` eingeben;
3. eine aktuelle Step-up-Prüfung abschließen;
4. den angenommenen dauerhaften Zerstörungsvorgang verfolgen.

Die Bereinigungsanfrage ist absichtlich auf exakte Container und NPM-Routen begrenzt. Matrix-Datenbank, Datenbankrolle und lokale Stack-Dateien bleiben erhalten; `force=false` bleibt gesetzt. Vor jeder Änderung prüft MEM erneut gespeicherten Containernamen und -ID, MEM-Besitzlabels, Control-Plane-Instanz, Laufzeitmodus, Dienstidentität, Datenpfad-Mounts, öffentlichen Hostnamen, NPM-Ziel, Port und Zertifikatsidentität. Jede Abweichung führt zu einer sicheren Verweigerung.

Bereits fehlende Routen oder Container werden idempotent übersprungen. Ein gestoppter und eindeutig besitzgeprüfter Container kann entfernt werden. Anschließend entfernt MEM veraltete Dienst- und Routenzeilen, markiert den Laufzeit-Stack als zerstört, gibt den ursprünglichen Slug frei und speichert das endgültige Vorgangsergebnis.

> [!WARNING]
> Verwenden Sie keine breite Docker-Bereinigung, manuellen SQLite-Änderungen oder direkte NPM-Löschung, nur um Abgleichzähler zu bereinigen. Wenn MEM den Besitz als uneindeutig einstuft, bewahren Sie den Nachweis und untersuchen Sie ihn, statt die Entfernung zu erzwingen.

## Nach der Entfernung

Prüfen Sie:

- der Stack erscheint nicht mehr als aktive Laufzeit;
- öffentliche Routen liefern den Stack nicht mehr aus;
- der dauerhafte Zerstörungsvorgang hat **Erfolgreich** erreicht und ist nicht laufend oder fehlgeschlagen;
- behaltene Datenpfade und Datenbankbesitz sind dokumentiert.

Löschen Sie behaltene Ressourcen nicht manuell nur deshalb, weil der Stack nicht mehr in der Liste steht. Stärkere Datenlöschung oder Wiederherstellung muss bewusst über die unterstützten Werkzeuge der installierten Version erfolgen.

## Wann Löschen ungeeignet ist

Verwenden Sie Laufzeitlöschung nicht als Ersatz für:

- Migration auf einen neuen Server;
- Wiederherstellungsprobe;
- Hostnamenänderung;
- vorübergehende Störungsdiagnose;
- Bereinigung einer fehlgeschlagenen Erstellung ohne Prüfung von Vorgang und Manifest.

Verwenden Sie dafür Migration, Wiederherstellung oder Diagnose.

---

# Rollback-Grenzen verstehen

Source: `docs/de/migrieren/rollback.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Rollback-Grenzen verstehen

Rollback ist kein universeller Knopf. Die mögliche Aktion hängt von der Routenumschaltung, dem gewählten Quellautoritätspfad und bereits erzeugten neuen Zieldaten ab.

## Ergebnis

Sie unterscheiden sicheren Abbruch vor der Umschaltung, Ziel-Routenwiederherstellung und vollständige Quellwiederherstellung und verstehen den Datenverlust beim Rückwechsel.

## Vor öffentlicher Umschaltung

Vor der Routenveröffentlichung sind Kandidat und normale Ziel-Laufzeit privat. Abbruch oder Bereinigung auf dem Ziel verändert oder löscht die alte Quelle nicht. Dies ist der sicherste Stoppunkt.

## Nach Routenveröffentlichung

MEM kann bei fehlgeschlagener Route oder autorisiertem erweitertem Rollback den exakten vorherigen Nginx-Proxy-Manager-Zustand wiederherstellen. Dies verändert nur die Ziel-Routenhoheit. Die Ziel-Control-Plane verbindet sich nicht mit dem alten Host, um ihn zu starten, aufzutauen oder zu verändern.

Kann die Wiederherstellung nicht bewiesen werden, gilt die Routenhoheit als unbekannt. Prüfen Sie die Nachweise vor weiteren Aktionen.

## Normaler Snapshot-Pfad

Der normale geführte Pfad verwendet Paket und privaten Test als Snapshot. Er beweist nicht formal, dass die Quelle nach der Erfassung eingefroren blieb.

Ein Rückwechsel kann alle nach dem Snapshot auf dem Ziel entstandenen Nachrichten, Konto- und Mitgliedschaftsänderungen, Medien und Schlüsselereignisse verlieren. Das Ziel synchronisiert sie nicht zurück.

## Final eingefrorener Pfad

Der erweiterte Pfad verlangt ein finales Paket mit:

- finaler Paketart;
- `sourceFrozen=true`;
- `rehearsalOnly=false`;
- keiner unzulässigen Drift.

Ein Ziel-Rollback kann Routen wiederherstellen und das Ziel stoppen oder privatisieren, doch die Quelle muss eingefroren bleiben, bis der separate Quellwiederherstellungs-Handoff mit MEM Migrate auf dem alten Host angewendet wurde. Dies stärkt Nachweise, führt aber keine Zusammenführung neuer Zieldaten durch.

## Entscheidungsregel

- Vor Umschaltung stoppen und Nachweise erhalten.
- Nach Routenänderung ohne Zielnutzung den Routensnapshot und erweiterte Steuerung verwenden.
- Nach Zielnutzung ausdrücklich entscheiden, ob der Verlust neuer Änderungen akzeptabel ist.
- Bei unbekannter Routenhoheit zuerst diagnostizieren und nicht wiederholt hin- und herschalten.

> [!WARNING]
> Löschen Sie die alte Quelle niemals nur deshalb, weil der neue Server einmal geöffnet wurde. Bewahren Sie sie bis nach Produktionsprüfung, Annahme und Aufbewahrungszeitraum auf.

Verwandt: [Liveschaltung und Produktionsprüfung](go-live.md) sowie [Migrationsnachweise und Support](evidence-and-support.md).

---

# Understand rollback boundaries

Source: `docs/migrate/rollback.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Understand rollback boundaries

Rollback is not one universal button. The available action depends on whether public routes changed, which source-authority path was used, and whether users have already created new target data.

## Outcome

You can distinguish safe pre-cutover cancellation, target route recovery, and full source restoration, and you understand the data-loss boundary of returning to the old server.

## Before public cutover

Before route publication, the new candidate and normal target runtime are private. Cancelling or cleaning up target-side work does not change or delete the legacy source. This is the safest point to stop.

## After route publication

MEM may restore the exact pre-cutover Nginx Proxy Manager route snapshot when a route operation fails or when an authorized advanced rollback is applied. This changes target-host route ownership only. The target Control Plane does not connect to the legacy host to restart, unfreeze, or modify it.

When route restoration cannot be proven, treat public ownership as unknown. Inspect the recorded route evidence before taking another action.

## Normal snapshot path

The normal guided path uses the verified package and private test as the authoritative snapshot. It does not provide formal proof that the source remained frozen after capture.

Returning users to the old source can lose every message, account change, membership change, media upload, and key event created on the target after the snapshot. The target does not synchronize those changes back.

## Final-frozen path

The advanced final-frozen path requires a final package proving:

- final package kind;
- `sourceFrozen=true`;
- `rehearsalOnly=false`;
- no disallowed drift from the qualified source.

A target-host rollback can restore routes and stop or privatize the target, but the source must remain frozen until the separate source-restoration handoff is applied with MEM Migrate on the old host. This provides stronger evidence; it still does not merge post-cutover target changes into the source.

## Decision rule

- If cutover has not happened, stop and preserve evidence.
- If routes changed but no target user activity occurred, use the recorded route snapshot and advanced recovery controls.
- If users have used the target, explicitly decide whether losing those new changes is acceptable before returning to the source.
- If route ownership is uncertain, diagnose first; do not alternate routes repeatedly.

> [!WARNING]
> Never delete the legacy source merely because the new server opened once. Preserve it through production verification, acceptance, and the chosen retention period.

Related: [Make the new server live and verify production](go-live.md) and [Use migration evidence, logs, and support information](evidence-and-support.md).

---

# Use restore evidence, logs, and support reports

Source: `docs/backups-and-restores/evidence-logs-support.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Use restore evidence, logs, and support reports

The Restore Workspace keeps separate views for operator progress, safe evidence, structured logs, configuration, and support handoff.

## Activity

**Activity** is the durable timeline. Use it to identify stage transitions, operation IDs, event codes, timestamps, and whether work was requested, started, completed, failed, or cancelled.

## Evidence

**Evidence** groups curated results such as backup readiness, private-test outcome, Standard Recreate, public verification, and handover. It is the fastest place to find the latest recorded success or failure without reading every log line.

## Logs

Restore logs are append-only structured events. They can be filtered by severity, stage, and search text. Each safe event can include:

- timestamp;
- restore-session and operation identity;
- stage;
- severity;
- stable event code;
- redacted message and allowlisted details.

Logs must not contain credentials, private keys, signing keys, tokens, connection strings, or unrestricted command output. The redactor is a final safety net, not a reason to paste secrets into an operator note.

## Configuration

**Configuration** shows safe source snapshots, target values, target claims, and recorded operation facts. Use it to distinguish what the operator requested from what a later runtime inspection happens to show.

## Generate a support report

Use **Generate support report** from the workspace rail. MEM downloads a formatted JSON report containing:

- MEM version and restore-session ID;
- attempt state and latest safe error summary;
- source and target identity;
- log counts and recent redacted events;
- operation summaries;
- warnings.

The report deliberately excludes database dumps, configuration files, credentials, private keys, and unrestricted raw logs.

## Before sharing

Review even a redacted report before sharing it. Hostnames, stack names, timings, event codes, user counts, and topology can still be sensitive in some environments.

Never attach the portable backup ZIP, PostgreSQL dump, signing key, `homeserver.yaml`, raw container logs, access tokens, or TOTP recovery material to an ordinary support request.

When the Control Plane UI is unavailable, preserve local logs and Docker evidence through the documented diagnostics fallback, then correlate them with the restore-session ID rather than inventing a new restore attempt.

---

# Troubleshoot a chat server

Source: `docs/chat-servers/troubleshooting.md`
Locale: en
Section: Operate chat servers
Status: supported
Applies to: 0.2.x

# Troubleshoot a chat server

## Start with fresh evidence

Open the stack workspace, select **Refresh**, then run **Doctor**. Record the stack slug, checked time, operation ID, report ID, failed check codes, and redacted detail.

Do not diagnose solely from container presence or an old last-verified timestamp.

## Creation failed

The creation dialog explicitly reports failure and does not assume success. Before retrying:

1. return to Chat servers and refresh;
2. check whether the stack, containers, routes, database, or operation were recorded;
3. inspect the failed operation and current diagnostics;
4. correct the underlying domain, certificate, image, Docker, PostgreSQL, storage, or readiness problem;
5. retry the same intended request only when the resulting ownership state is clear.

Do not create a second slug to hide a partially created production identity.

## Synapse configuration or production storage failure

On an official production installation, new stack resources belong beneath:

```text
/var/lib/message-easy-mode/instances
```

The Control Plane and host Docker daemon must address the same physical files through that canonical host-data contract. Do not redirect production instance data into `/home/<user>/mem-data` to work around a failure.

If creation fails around Synapse configuration generation, preserve the failed operation and support report. Check whether the incident names `generate-synapse-config`, but use the supported MEM recovery/correction path rather than manually moving generated configuration files.

## NPM route publication fails with `npm:81` resolution

In containerized production, the Control Plane and Nginx Proxy Manager communicate through the managed `mem-gateway` Docker network. An error such as `Name or service not known (npm:81)` means the Control Plane cannot currently resolve the managed NPM authority.

Collect the incident/support report and verify the Control Plane was launched or recreated through the current supported bootstrap. Manual `docker network connect` can be useful as bounded engineering diagnosis, but it is not the normal operator repair procedure and should not replace the supported runtime/recreation path.

## Matrix or Element is not public

Use **Network & domains** to confirm recorded hosts, route IDs, certificate IDs, and internal delivery. Run Doctor to distinguish internal HTTP, NPM, route, and public HTTPS failures.

The page does not verify authoritative DNS or certificate expiry. Check those separately when public HTTPS fails but internal service checks pass.

## User creation is disabled

Synchronize the Synapse user inventory. Resolve inventory errors before treating the homeserver as empty. The first administrator workflow appears only when Synapse authoritatively reports no active local admin.

For password resets, validate Matrix administrator authority and complete Control Plane step-up. Replace rejected or expired authority rather than repeatedly submitting the reset.

## Voice or video is unreliable

Open **Voice & video** and check both stack connection and platform readiness. A connected Synapse configuration does not make relay traffic reliable when the platform coturn service or relay ports are not ready.

Do not overwrite External or drifted TURN settings manually. Use the reviewed operation or collect technical recovery evidence.

## Federation changes are disabled

MEM refuses to rewrite custom, ambiguous, incomplete, or unsupported federation state automatically. Review the reported Synapse runtime and canonical NPM ingress problem. Complete or restore a supported state before applying another policy.

Do not submit a second federation change while a durable operation is running after a browser disconnect.

## Storage details are unavailable

Confirm the stack manifest and Matrix runtime identity are readable. Do not infer that absent storage evidence means the files are absent. Inspect the host only through approved outage or support procedures.

## Escalation evidence

Retain redacted:

- stack slug and public hosts;
- last verified and Doctor checked times;
- operation and report identifiers;
- failed check or stable problem codes;
- image names and versions;
- route and certificate identifiers;
- TURN or federation state and configuration hashes;
- relevant container state and bounded logs.

Never include passwords, Matrix access tokens, TOTP secrets, recovery codes, TURN shared secrets, signing-key contents, or unredacted configuration files.

For platform installation failures, also see [Installation troubleshooting](../installation/troubleshooting.md). For capability boundaries, see [Known limitations](../start/known-limitations.md).

---

# Nachweise, Logs und Supportberichte verwenden

Source: `docs/de/backups-und-wiederherstellen/nachweise-logs-support.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Nachweise, Logs und Supportberichte verwenden

Der Wiederherstellungsarbeitsbereich trennt Fortschritt, sichere Nachweise, strukturierte Logs, Konfiguration und Supportübergabe.

## Aktivität

**Aktivität** zeigt Stufenwechsel, Vorgangs-IDs, Ereigniscodes, Zeitpunkte sowie angefordert, gestartet, abgeschlossen, fehlgeschlagen oder abgebrochen.

## Nachweise

**Nachweise** gruppiert kuratierte Ergebnisse für Sicherungsbereitschaft, privaten Test, Standard-Neuerstellung, öffentliche Prüfung und Übergabe. Hier finden Sie schnell den letzten Erfolg oder Fehler.

## Logs

Restore-Logs sind append-only strukturierte Ereignisse und können nach Schweregrad, Stufe und Text gefiltert werden. Ein sicheres Ereignis enthält Zeit, Restore- und Vorgangsidentität, Stufe, Schweregrad, stabilen Ereigniscode sowie redigierte Nachricht und freigegebene Details.

Logs dürfen keine Zugangsdaten, privaten Schlüssel, Signaturschlüssel, Tokens, Verbindungszeichenfolgen oder ungefilterte Befehlsausgabe enthalten. Die Redaktion ist eine letzte Schutzschicht.

## Konfiguration

**Konfiguration** zeigt sichere Quell-Snapshots, Zielwerte, Reservierungen und Vorgangsdaten. Damit lässt sich die Betreiberanforderung vom später beobachteten Laufzeitstatus trennen.

## Supportbericht erzeugen

Über **Supportbericht erzeugen** wird ein formatiertes JSON heruntergeladen mit:

- MEM-Version und Restore-ID;
- Versuchsstatus und letzter sicherer Fehlerzusammenfassung;
- Quellen- und Zielidentität;
- Log-Zählungen und aktuellen redigierten Ereignissen;
- Vorgangszusammenfassungen;
- Warnungen.

Datenbankdumps, Konfigurationsdateien, Zugangsdaten, private Schlüssel und ungefilterte Logs werden bewusst ausgeschlossen.

## Vor dem Teilen

Auch einen redigierten Bericht prüfen. Hostnamen, Stack-Namen, Zeitpunkte, Ereigniscodes, Benutzerzahlen und Topologie können sensibel sein.

Portable ZIPs, PostgreSQL-Dumps, Signaturschlüssel, `homeserver.yaml`, rohe Containerlogs, Zugriffstokens und TOTP-Wiederherstellungsmaterial gehören nicht in normale Supportanfragen.

Ist die Control Plane nicht verfügbar, lokale Logs und Docker-Nachweise über den dokumentierten Diagnose-Notfallweg sichern und mit der Restore-ID korrelieren, statt einen neuen Versuch zu erzeugen.

---

# Chatserver-Fehler beheben

Source: `docs/de/chat-servers/fehlerbehebung.md`
Locale: de
Section: Chatserver betreiben
Status: supported
Applies to: 0.2.x

# Chatserver-Fehler beheben

## Mit aktuellen Nachweisen beginnen

Öffnen Sie den Stack-Arbeitsbereich, wählen Sie **Aktualisieren** und führen Sie **Diagnose** aus. Erfassen Sie Slug, Prüfzeit, Vorgangs-ID, Berichts-ID, fehlgeschlagene Prüfcodes und redigierte Details.

Diagnostizieren Sie nicht nur anhand vorhandener Container oder eines alten Prüfzeitpunkts.

## Erstellung fehlgeschlagen

Der Dialog meldet Fehler ausdrücklich und nimmt keinen Erfolg an. Vor erneutem Versuch:

1. zur Chatserver-Liste zurückkehren und aktualisieren;
2. prüfen, ob Stack, Container, Routen, Datenbank oder Vorgang erfasst wurden;
3. fehlgeschlagenen Vorgang und Diagnose prüfen;
4. Domain-, Zertifikats-, Abbild-, Docker-, PostgreSQL-, Speicher- oder Bereitschaftsproblem beheben;
5. dieselbe beabsichtigte Anfrage nur bei klarem Besitzstand erneut ausführen.

Erstellen Sie keinen zweiten Slug, um eine teilweise erstellte produktive Identität zu verstecken.

## Synapse-Konfiguration oder Produktionsspeicher schlägt fehl

Bei einer offiziellen Produktionsinstallation liegen neue Stack-Ressourcen unter:

```text
/var/lib/message-easy-mode/instances
```

Control Plane und Host-Docker-Daemon müssen über diesen kanonischen Host-Datenvertrag dieselben physischen Dateien adressieren. Leiten Sie Produktions-Instanzdaten nicht auf `/home/<user>/mem-data` um, um einen Fehler zu umgehen.

Schlägt die Erstellung bei der Synapse-Konfigurationsgenerierung fehl, bewahren Sie Vorgang und Supportbericht auf. Prüfen Sie, ob der Vorfall `generate-synapse-config` nennt, verschieben Sie generierte Konfigurationsdateien aber nicht manuell. Verwenden Sie den unterstützten MEM-Recovery-/Korrekturpfad.

## NPM-Routenveröffentlichung scheitert bei `npm:81`

In containerisierter Produktion kommunizieren Control Plane und Nginx Proxy Manager über das verwaltete Docker-Netzwerk `mem-gateway`. Ein Fehler wie `Name or service not known (npm:81)` bedeutet, dass die Control Plane die verwaltete NPM-Adresse aktuell nicht auflösen kann.

Sammeln Sie Vorfall/Supportbericht und prüfen Sie, ob die Control Plane über das aktuelle unterstützte Bootstrap gestartet oder neu erstellt wurde. Ein manuelles `docker network connect` kann für begrenzte technische Diagnose nützlich sein, ist aber kein normaler Operator-Reparaturweg und ersetzt nicht den unterstützten Runtime-/Recreate-Pfad.

## Matrix oder Element nicht öffentlich erreichbar

Prüfen Sie unter **Netzwerk & Domains** Hosts, Routen-IDs, Zertifikats-IDs und interne Zustellung. Diagnose unterscheidet internes HTTP, NPM, Route und öffentliches HTTPS.

Die Seite prüft weder autoritatives DNS noch Zertifikatsablauf. Prüfen Sie dies separat, wenn öffentliches HTTPS scheitert, intern aber alles funktioniert.

## Benutzererstellung deaktiviert

Synchronisieren Sie das Synapse-Benutzerinventar. Beheben Sie Inventarfehler, bevor Sie den Homeserver als leer behandeln. Der erste Administrator erscheint nur, wenn Synapse verbindlich keinen aktiven lokalen Admin meldet.

Für Passwortzurücksetzung prüfen Sie Matrix-Administratorautorität und Control-Plane-Step-up. Ersetzen Sie abgelehnte oder abgelaufene Autorität.

## Sprache oder Video unzuverlässig

Prüfen Sie unter **Sprache & Video** Stack-Verbindung und Plattformbereitschaft. Eine verbundene Synapse-Konfiguration garantiert keinen zuverlässigen Relay-Verkehr, wenn coturn oder Relay-Ports nicht bereit sind.

Überschreiben Sie externe oder driftende TURN-Einstellungen nicht manuell. Verwenden Sie den geprüften Vorgang oder sammeln Sie technische Wiederherstellungsnachweise.

## Föderationsänderungen deaktiviert

MEM schreibt benutzerdefinierte, mehrdeutige, unvollständige oder nicht unterstützte Zustände nicht automatisch um. Prüfen Sie Synapse-Laufzeit und kanonisches NPM-Ingress-Problem. Stellen Sie einen unterstützten Zustand her.

Senden Sie nach Browsertrennung keine zweite Änderung, solange ein dauerhafter Vorgang läuft.

## Speicherdetails nicht verfügbar

Bestätigen Sie, dass Stack-Manifest und Matrix-Laufzeitidentität lesbar sind. Fehlende Speichernachweise bedeuten nicht, dass Dateien fehlen. Prüfen Sie den Host nur über genehmigte Ausfall- oder Supportverfahren.

## Eskalationsnachweise

Bewahren Sie redigiert auf:

- Stack-Slug und öffentliche Hosts;
- letzten Prüf- und Diagnosezeitpunkt;
- Vorgangs- und Berichtskennungen;
- fehlgeschlagene Prüf- oder stabile Problemcodes;
- Abbildnamen und Versionen;
- Routen- und Zertifikatskennungen;
- TURN- oder Föderationszustand und Konfigurationshashes;
- relevanten Containerzustand und begrenzte Logs.

Nie enthalten: Passwörter, Matrix-Zugriffstoken, TOTP-Geheimnisse, Wiederherstellungscodes, TURN-Geheimnisse, Signaturschlüsselinhalte oder unredigierte Konfigurationsdateien.

Für Plattform-Installationsfehler siehe [Fehlerbehebung bei der Installation](../installation/installation-fehlerbehebung.md). Für Capability-Grenzen siehe [Bekannte Einschränkungen](../start/known-limitations.md).

---

# Migrationsressourcen aufbewahren oder bereinigen

Source: `docs/de/migrieren/cleanup.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Migrationsressourcen aufbewahren oder bereinigen

Eine Migration erzeugt Erfassungen, verschlüsselte Pakete, Zielarbeitsmaterial, Konvertierungsausgaben, privates Staging, eine normale Ziel-Laufzeit und dauerhafte Nachweise. Nicht alles hat dieselbe Löschgrenze.

## Ergebnis

Der angenommene Server und erforderliche Nachweise bleiben erhalten, temporäre Ressourcen werden sicher entfernt und die alte Quelle bleibt für den festgelegten Zeitraum verfügbar.

## Alte Quelle

MEM 0.2.0 löscht niemals automatisch den alten Host oder dessen Matrix-Daten. Nach der Annahme:

- für den aufgezeichneten Zeitraum aufbewahren;
- versehentliche normale Nutzung und Routenhoheit verhindern;
- Bewertung, Erfassung, Paketbericht und gegebenenfalls final eingefrorene Nachweise erhalten;
- verantwortliche Person für endgültige Entsorgung dokumentieren;
- erst entsorgen, wenn Rollback nicht mehr erforderlich ist.

## Material im Source Assistant

Der Source Assistant kann ein fertiges verschlüsseltes Paket und Paketberichte löschen. Quellerfassung, Bewertungshistorie, Journal, aktive Altdaten und Host bleiben bestehen.

Entfernen Sie eine Erfassung nur, wenn sie für Paketerstellung, Nachweise und Rollback-Planung nicht mehr benötigt wird und die angebotene Aktion ihren Umfang eindeutig beschreibt.

## Paketaufbewahrung auf dem Ziel

Bei frühem Abbruch wählen Sie Aufbewahrung oder Entfernung des verschlüsselten Pakets. Entschlüsseltes Zielarbeitsmaterial wird entfernt. Nach Löschen der Entschlüsselungsidentität kann ein behaltenes Paket die abgebrochene Sitzung nicht einfach fortsetzen.

Bei abgeschlossenen Migrationen gilt die Sitzungsrichtlinie; Hashes und sichere Herkunft bleiben auch nach späterer Payload-Entfernung erhalten.

## Staging-Bereinigung

Privater Test und Konvertierungsressourcen bleiben bis Annahme und erster nativer Sicherung migrationsgebunden. Danach versucht MEM automatische Bereinigung.

Bei Fehlern löschen Sie keine Container oder Verzeichnisse, bevor Migrations-ID und Eigentum bestätigt sind. Verwenden Sie die explizite Wiederholungs- oder Bereinigungsaktion, prüfen Sie die gesunde Produktionslaufzeit und bewahren Sie das Operationslog auf.

Ein Staging-Bereinigungsfehler macht den angenommenen Server nicht ungültig.

## Erfolg prüfen

Produktions-Stack gesund, erste native Sicherung im Katalog, keine öffentliche Route für Staging, Aufbewahrungsnachweis intakt und Entscheidungen zu Paket, Erfassung und Nachweisen dokumentiert.

Verwandt: [Sitzungen fortsetzen, abbrechen, archivieren und wiederherstellen](session-lifecycle.md).

---

# Retain or clean up migration resources

Source: `docs/migrate/cleanup.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Retain or clean up migration resources

Migration creates source captures, encrypted packages, target working material, conversion output, private staging, a normal target runtime, and durable evidence. They do not all share the same deletion boundary.

## Outcome

The accepted server and required evidence are retained, temporary resources are removed safely, and the legacy source remains available for the recorded retention period.

## Legacy source

MEM 0.2.0 never automatically deletes the old host or its Matrix data. After acceptance:

- keep it for the recorded retention duration;
- prevent accidental normal use or route ownership;
- retain the source assessment, capture, package report, and any final-frozen handoff evidence;
- document who may authorize final disposal;
- dispose of it only after your organization accepts that rollback is no longer required.

## Source Assistant material

The Source Assistant can delete a completed encrypted package and package reports. That action does not remove the source capture, assessment history, journal, live legacy data, or host.

Remove a source capture only when you no longer require it for package recreation, evidence, or rollback planning and the available Source Assistant action explicitly describes that scope.

## Target package retention

During an early cancellation, the target asks whether to retain or remove the encrypted package. Any decrypted target working package is removed. Clearing the target decryption identity means a retained encrypted package cannot simply resume the cancelled session.

For completed migrations, follow the session retention policy and preserve hashes and safe provenance even when payloads are later removed.

## Staging cleanup

Private-test and conversion resources remain migration-owned through acceptance and the first native backup. After that boundary, MEM attempts automatic cleanup.

If cleanup fails:

- do not delete containers or directories until you confirm their migration ID and ownership;
- use the explicit retry or cleanup operation shown by the workspace;
- verify that the accepted normal runtime remains healthy;
- retain the cleanup operation log.

A staging cleanup failure does not invalidate the accepted server.

## Verify success

- The production stack remains managed and healthy.
- The first native backup is retained in the Backup Catalog.
- No staging container owns public routes.
- The legacy source retention record is intact.
- Package, capture, and evidence deletion decisions are documented.

Related: [Resume, cancel, archive, and restore sessions](session-lifecycle.md).

---

# Domains, Zertifikate und Verlängerung betreiben

Source: `docs/de/operations/domains-und-zertifikate.md`
Locale: de
Section: Operations (Deutsch)
Status: supported
Applies to: 0.2.x

# Domains, Zertifikate und Verlängerung betreiben

Verwenden Sie **Domains** als Operator-Arbeitsbereich für die Registrierung öffentlicher Domains, Domain-eigene Zertifikate, die Auswahl des aktiven Zertifikats, die Hauptdomain und die Verlängerungsbereitschaft.

Die wichtige Eigentumsregel lautet:

```text
Domain
  → besitzt Zertifikate
  → hat höchstens ein aktives Produktionszertifikat
  → hat eine eigene Verlängerungsrichtlinie und eigene Verlängerungszugangsdaten
```

Die Hauptdomain der Plattform ist eine separate Rolle. Ein Zertifikat, das für seine Domain aktiv ist, macht diese Domain nicht automatisch zur Hauptdomain.

## Zuerst eine Domain registrieren

Öffnen Sie **Domains → Domain hinzufügen**, um den Registry-Eintrag anzulegen.

Der geführte Anbieter in MEM 0.2.x ist **deSEC**. Domain hinzufügen speichert Domain, Anbieter und DNS-Zone für spätere Schritte. Dieser Vorgang ist bewusst **nur Registrierung**:

- deSEC wird nicht kontaktiert;
- es werden keine `_acme-challenge`-Einträge erstellt;
- es wird kein Zertifikat angefordert;
- es wird kein Token für die Zertifikatsausstellung gespeichert;
- die automatische Verlängerung wird nicht aktiviert.

Öffnen Sie danach den Arbeitsbereich der Domain, um Zertifikate auszustellen oder zu verwalten.

> [!NOTE]
> Das erstmalige Setup besitzt einen eigenen geprüften Zertifikatsplan für die Plattforminstallation. Der nachträgliche Ablauf **Domain hinzufügen** ist absichtlich kleiner und ist keine Wiederholung des alten Setup-Assistenten innerhalb von Domains.

## Die globale Zertifikatsseite als Inventar verwenden

**Domains → Zertifikate** ist das übergreifende Zertifikatsinventar. Dort können Sie Zertifikate aller registrierten Domains finden und anschließend die besitzende Domain oder die Zertifikatsdetails öffnen.

Eigentum und normale Lebenszyklusänderungen gehören zur jeweiligen Domain. Verwenden Sie das globale Inventar nicht als Domain-übergreifendes Zuweisungswerkzeug.

Der eingeklappte Bereich **Erweiterte Ingress-Diagnose** ist davon getrennt. Er enthält begrenzte NPM-/Ingress-Diagnoseoperationen für die Fehlersuche und ersetzt das Domain-eigene Zertifikatsmodell nicht.

## Ein Zertifikat aus der besitzenden Domain ausstellen

Öffnen Sie die Domain und anschließend **Zertifikate → Zertifikat ausstellen**.

MEM bindet die Anfrage an die besitzende Domain. Der geführte Ablauf leitet den Wildcard-Namen aus der Domain ab und verwendet deSEC DNS-01. Geben Sie die einmaligen Zugangsdaten ein, nach denen das Formular fragt. DNS-Tokens gehören nicht in Supportunterlagen, Screenshots oder normale Logs.

Die Zertifikatsausstellung ist ein **serverseitiger dauerhafter Vorgang**. Der Browser zeigt eine verständliche Phasenansicht aus dem serverseitigen Fortschritt, zum Beispiel:

```text
Zertifikatsanforderung vorbereiten
DNS-01-Challenge veröffentlichen
Auf autoritative DNS-Bereitschaft warten
DNS-Challenge mit Let's Encrypt validieren
Zertifikat finalisieren und herunterladen
Zertifikat speichern und validieren
Produktionszugangsdaten für Verlängerung sichern   (Produktion, wenn zutreffend)
```

Die aktuelle Phase ist maßgeblich. Sie können die Seite verlassen und zurückkehren; MEM findet den dauerhaften Vorgang wieder, statt die Arbeit an die Browsersitzung zu binden.

Sobald der Server eine Ausstellungsanforderung angenommen hat, wird das vollständige Anforderungsformular durch eine sichere Zusammenfassung sowie die dauerhafte Fortschritts-/Ergebnisansicht ersetzt. MEM zeigt das deSEC-Token **nicht erneut** an. Nach einem terminalen Ergebnis führen **Weiteres Zertifikat ausstellen** oder **Neue Ausstellung versuchen** zurück zu einem frischen Formular mit leerem Tokenfeld.

Während eine Ausstellung läuft, können das globale **Zertifikate**-Inventar, die **Zertifikate**-Seite der besitzenden Domain und die Domain-Detailseite eine kompakte laufende Aktivität mit **Fortschritt anzeigen** darstellen. Diese Oberflächen führen zum selben serverseitigen Vorgang zurück und starten keine zweite Ausstellung.

### Technische Nachweise und aktueller Zustand

Die Phasenansicht ist die normale Operator-Sicht. Öffnen Sie **Technische Nachweise** zur Fehlersuche.

Nachweiszeilen sind historische Beobachtungen aus dem Vorgang. Eine Zeile wie **Aufgezeichnet: Läuft** bedeutet, dass dieser Zustand beim Erstellen des Nachweises beobachtet wurde. Sie bedeutet nicht, dass ein inzwischen mit **Erfolgreich** abgeschlossener Vorgang noch läuft.

Wenn die Ausstellung fehlschlägt und MEM einen Vorfall erstellt, verwenden Sie den Diagnose-Link im Vorgangsergebnis. Bewahren Sie Vorgangs- und Vorfallidentität für Supportfälle auf.

Eine erfolgreich abgeschlossene historische Ausstellung bleibt auch dann wertvoller Nachweis, wenn das daraus entstandene Zertifikat später gelöscht wurde. In diesem Fall hält der Ausstellungsarbeitsbereich fest, dass das Zertifikat nicht mehr vorhanden ist, statt einen ungültigen Link **Ausgestelltes Zertifikat öffnen** anzubieten.

## Staging und Produktion unterscheiden

Verwenden Sie Let's-Encrypt-**Staging**, um DNS-01 zu testen, ohne normale Produktionslimits unnötig zu belasten.

Ein Staging-Zertifikat:

- wird von normalen Browsern nicht als vertrauenswürdig akzeptiert;
- bleibt Eigentum seiner Domain;
- kann nicht zum aktiven Produktionszertifikat der Domain werden;
- kann die Domain nicht für die Rolle als Hauptdomain qualifizieren;
- erstellt oder ersetzt weder die Produktions-Zugangsdaten noch die Produktionsrichtlinie für Verlängerung;
- darf nicht als normales Matrix-/Element-Ingress-Zertifikat verwendet werden.

Verwenden Sie **Produktion** für echte öffentliche Dienste. MEM muss die Produktionsvalidierung und den Aktivierungsvertrag erfolgreich abschließen, bevor der Zeiger auf das aktive Domain-Zertifikat geändert wird. Eine erfolgreiche Produktionsausstellung speichert normalerweise die verifizierten deSEC-Zugangsdaten geschützt für die Domain, erfasst den ACME-Kontakt und aktiviert die standardmäßige automatische Verlängerungsrichtlinie. Wenn dieser letzte Schritt für Verlängerungszugangsdaten eine Warnung liefert, kann das Zertifikat selbst weiterhin gültig sein; der Verlängerungsarbeitsbereich muss jedoch repariert werden, bevor unbeaufsichtigte Verlängerung als bereit gilt.

## Aktives Zertifikat und Hauptdomain getrennt behandeln

Eine Domain kann Zertifikatsverlauf besitzen, während genau ein geeignetes Produktionszertifikat aktiv ist.

Beim Wechsel des aktiven Zertifikats verwendet MEM die Domain-bezogene Operation und prüft, ob das Zertifikat zu dieser Domain gehört und für Produktion geeignet ist.

Eine Nicht-Hauptdomain kann nur dann zur **Hauptdomain** gemacht werden, wenn der aktuelle Bereitschaftsvertrag dies erlaubt. Ein Staging-Zertifikat umgeht diese Voraussetzung nicht.

## Verlängerung zuerst als Flottenübersicht verwenden

Öffnen Sie **Domains → Verlängerung** für eine übergreifende Statusansicht. Sie zeigt unter anderem, ob automatische Verlängerung aktiviert ist, ob geschützte DNS-Verlängerungszugangsdaten vorhanden sind, wann das Zertifikat abläuft und wann der nächste automatische Versuch geplant ist.

Die Flottenseite ist für sensible Verlängerungskonfiguration bewusst schreibgeschützt. Wählen Sie **Verlängerung öffnen**, um den Detailarbeitsbereich einer Domain zu verwenden.

Der Domain-Arbeitsbereich für Verlängerung enthält die geschützten Aktionen, darunter:

- deSEC-Verlängerungszugangsdaten für historische Domains, Wiederherstellung oder geplante Rotation hinterlegen oder rotieren;
- automatische Verlängerung aktivieren oder deaktivieren, wenn zulässig;
- **Jetzt verlängern** oder einen fehlgeschlagenen Vorgang wiederholen, wenn zulässig;
- aktuellen/letzten Vorgangszustand;
- dauerhaften Verlängerungsverlauf und Diagnose-Links.

Das deSEC-Verlängerungstoken wird geprüft und geschützt serverseitig gespeichert. MEM gibt das gespeicherte Token nicht an den Browser zurück. Eine erfolgreiche Produktionsausstellung führt diese Hinterlegung normalerweise automatisch durch; die manuelle Hinterlegung ist der Wiederherstellungs-/Rotationspfad, wenn der Arbeitsbereich fehlende Zugangsdaten meldet.

## Verhalten der automatischen Verlängerung

Die automatische Verlängerung ist serverseitig. Wenn ein geeignetes Produktionszertifikat sein Verlängerungsfenster erreicht, kann MEM einen Ersatz ausstellen und validieren, ihn bei Bedarf in NPM aktivieren, den Ingress prüfen und erst danach die Domain auf das erneuerte Zertifikat umstellen.

Das vorherige Zertifikat bleibt als Verlauf erhalten. Ein Fehler vor sicherer Aktivierung darf den aktiven Zeiger nicht stillschweigend auf einen ungeprüften Ersatz verschieben.

Wenn automatische Verlängerung deaktiviert ist oder Verlängerungszugangsdaten fehlen, müssen Flotten- und Detailansicht dies ausdrücklich anzeigen.

## Zertifikate und Domains ausdrücklich löschen

Das Löschen eines Zertifikats gehört zur besitzenden Domain. Öffnen Sie die Zertifikatsdetails und verwenden Sie **Zertifikat löschen**, wenn es nicht mehr benötigt wird. MEM blockiert das Löschen bewusst, wenn es sich um das Hauptplattform-Zertifikat handelt, wenn ein aktiver Chat Server das Zertifikat noch benötigt oder wenn NPM-Ingress es weiterhin verwendet. Staging-Zertifikate und anderweitig ungenutzte Produktionszertifikate können sicher entfernt werden.

Wird das aktive Produktionszertifikat einer ungenutzten Nicht-Hauptdomain gelöscht, entfernt MEM bewusst den aktiven Zertifikatszeiger dieser Domain; die Domain selbst bleibt bestehen. Ein Fehler bei der Bereinigung muss Registry- und Speicherzustand intakt lassen, statt fälschlich Erfolg zu melden.

Das Löschen einer Domain ist kein verstecktes Zertifikats-Cascade. Löschen Sie zuerst ausdrücklich die Domain-eigenen Zertifikate und verwenden Sie anschließend **Domain löschen** in den Domain-Details. Die Hauptdomain sowie Domains, die noch von aktiven Chat Servern referenziert werden, bleiben geschützt.

Das Löschen eines aktiven Produktionszertifikats entfernt verifizierte Verlängerungszugangsdaten oder die Richtlinie nicht automatisch. Bis wieder ein Produktionszertifikat vorhanden ist, kann Verlängerung daher korrekt melden, dass Richtlinie und Zugangsdaten konfiguriert sind, aber noch ein Produktionszertifikat benötigt wird.

## Sicherer Operator-Ablauf

Für eine neue Domain nach der Installation:

1. **Domains → Domain hinzufügen** öffnen und die Domain registrieren;
2. die Domain öffnen;
3. zuerst ein Staging-Zertifikat ausstellen, wenn DNS-Delegation oder Anbieterzugriff unsicher sind, und prüfen, dass es nicht aktiv wird;
4. das Produktionszertifikat ausstellen;
5. prüfen, dass das geeignete Produktionszertifikat für die Domain aktiv ist und Verlängerung den erwarteten geschützten Zugangsdaten-/Richtlinienzustand aus der erfolgreichen Produktionsausstellung meldet;
6. manuelle Hinterlegung von Verlängerungszugangsdaten oder Richtlinienänderungen nur verwenden, wenn der Verlängerungsarbeitsbereich Wiederherstellung/Rotation verlangt oder Sie die Richtlinie bewusst ändern;
7. die Domain nur dann als **Hauptdomain** setzen, wenn der Bereitschaftsvertrag dies erlaubt;
8. bei Fehlern dauerhafter Vorgänge Diagnose und Nachweise verwenden.

## Nicht mehr aus dem Zustand ableiten, als er beweist

Beachten Sie diese Unterschiede:

- **deSEC als Anbieter registriert** bedeutet nicht, dass wiederverwendbare Verlängerungszugangsdaten hinterlegt sind;
- **Zertifikat vorhanden** bedeutet nicht, dass es aktiv ist;
- **für Domain aktiv** bedeutet nicht automatisch **Hauptplattform-Zertifikat**;
- erfolgreiches **Staging** macht ein Staging-Zertifikat nicht produktionstauglich;
- eine alte Nachweiszeile überschreibt nicht den aktuellen terminalen Vorgangszustand.

---

# Delete and retain recovery material safely

Source: `docs/backups-and-restores/deletion-retention.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Delete and retain recovery material safely

MEM intentionally separates recovery objects so that deleting one does not imply every related record or runtime was erased.

## Objects with separate lifecycles

- **Catalog entry and managed payload** — the recovery source used by Restore Workspace actions.
- **Original uploaded ZIP** — retained ingestion and provenance material for an imported archive.
- **Portable exports** — generated downloads associated with a catalog entry.
- **Private staging runtime** — disposable containers, network, and workspace created by a private test.
- **Restore Workspace** — durable attempt, claims, operations, logs, evidence, and support history.
- **Restored production stack** — a normal managed stack after Standard Recreate.

## Delete the original uploaded ZIP

Deleting a retained uploaded ZIP does not delete the materialised catalog payload, Restore Workspaces, logs, evidence, support reports, or restored stack. Use this when the original transport archive is no longer required but the catalog copy must remain recoverable.

## Retire private staging

Use **Retire private test** to remove disposable private containers, the internal network, and private workspace. The safe historical private-test result remains visible.

## Permanently delete a catalog entry

The catalog detail page allows permanent deletion only when no active restore blocks it. The operation can remove:

- the managed payload;
- the retained original archive when linked;
- server-side portable exports;
- catalog linkage from historical restore attempts.

The deletion is irreversible. A previously downloaded off-host ZIP is outside MEM and remains wherever you stored it.

Historical Restore Workspaces can remain readable after source deletion, but guided actions are blocked because executable source material no longer exists.

## Completed restored servers

Cancelling or deleting a Restore Workspace does not roll back or delete a completed restored stack. Manage that stack through normal stack operations and take a fresh backup before removal.

## Suggested retention policy

Keep at least:

- more than one recent local capture;
- at least one tested off-host portable export;
- the pre-change backup until the change is proven stable;
- restore evidence and support reports for the period required by your operational policy.

Test restoration periodically. Retention without a restore proof is only an assumption.

---

# Wiederherstellungsmaterial sicher löschen und aufbewahren

Source: `docs/de/backups-und-wiederherstellen/loeschen-und-aufbewahren.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Wiederherstellungsmaterial sicher löschen und aufbewahren

MEM trennt Wiederherstellungsobjekte, damit die Löschung eines Objekts nicht fälschlich als vollständige Bereinigung verstanden wird.

## Getrennte Lebenszyklen

- **Katalogeintrag und Payload** — Quelle für Restore-Aktionen.
- **Original-Upload-ZIP** — Aufnahme- und Herkunftsmaterial.
- **Portable Exporte** — erzeugte Downloads eines Katalogeintrags.
- **Privates Staging** — wegwerfbare Container, Netz und Arbeitsbereich.
- **Wiederherstellungsarbeitsbereich** — Versuch, Reservierungen, Vorgänge, Logs, Nachweise und Supportverlauf.
- **Wiederhergestellter Produktions-Stack** — normal verwalteter Stack nach Standard-Neuerstellung.

## Original-ZIP löschen

Die Löschung des Original-Uploads löscht nicht Katalog-Payload, Arbeitsbereiche, Logs, Nachweise, Supportberichte oder Produktions-Stack. Verwenden Sie dies, wenn der Transport-Upload nicht mehr benötigt wird, die Wiederherstellungsquelle aber erhalten bleiben soll.

## Privates Staging entfernen

**Privaten Test entfernen** löscht Wegwerf-Container, internes Netz und privaten Arbeitsbereich. Das sichere historische Testergebnis bleibt sichtbar.

## Katalogeintrag permanent löschen

Die permanente Löschung ist nur ohne aktiven Restore-Blocker möglich und kann entfernen:

- verwalteten Payload;
- verknüpften Original-Upload;
- serverseitige portable Exporte;
- Katalogverknüpfungen historischer Restore-Versuche.

Die Aktion ist irreversibel. Eine heruntergeladene externe Kopie liegt außerhalb von MEM.

Historische Arbeitsbereiche können nach Quellenlöschung lesbar bleiben, aber geführte Aktionen werden blockiert.

## Fertige Produktions-Stacks

Abbruch oder Löschung eines Arbeitsbereichs entfernt keinen fertig erstellten Stack. Verwalten Sie ihn über normale Stack-Abläufe und erstellen Sie vor Entfernung eine neue Sicherung.

## Empfohlene Aufbewahrung

Mindestens behalten:

- mehrere aktuelle lokale Sicherungen;
- mindestens einen getesteten externen Export;
- die Sicherung vor einer Änderung bis zur stabilen Bestätigung;
- Restore-Nachweise und Supportberichte entsprechend Ihrer Richtlinie.

Testen Sie Wiederherstellungen regelmäßig. Aufbewahrung ohne Wiederherstellungsnachweis bleibt eine Annahme.

---

# Sitzungen fortsetzen, abbrechen, archivieren und wiederherstellen

Source: `docs/de/migrieren/session-lifecycle.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Sitzungen fortsetzen, abbrechen, archivieren und wiederherstellen

Migrationssitzungen sind dauerhaft. Das Verlassen der Seite oder ein Browser-Neustart verwandelt eine laufende Operation nicht in eine neue Migration.

## Ergebnis

Sie kehren zu bestehenden Sitzungen zurück, brechen nur sicher ab, archivieren terminale Historie, stellen sie in der Liste wieder her und verstehen die eingeschränkte dauerhafte Löschung.

## Nach Refresh oder Unterbrechung fortsetzen

Öffnen Sie **Migrationen**, suchen Sie die bestehende Sitzung und fahren Sie an ihrem aktuellen Schritt fort. Erfassung, Konvertierung, Staging, Zielerstellung, Umschaltung, Sicherung und Bereinigung sind serverseitige Operationen. Prüfen Sie den Zustand, bevor Sie etwas erneut starten.

Erstellen Sie keine zweite Aufnahme nur wegen geschlossener Seite oder abgelaufener Vorschau. Aktualisieren Sie Nachweis oder Bereitschaft in derselben Sitzung.

## Frühe Sitzung abbrechen

Abbruch ist nur verfügbar, solange das Ziel serverseitig als wirklich entbehrlich gilt. Die Prüfung verlangt eine ausdrückliche Wahl zur Aufbewahrung des verschlüsselten Pakets und die Bestätigung, dass die Quelle außerhalb der Zielkontrolle liegt.

Der Zielabbruch verändert oder löscht die Quelle nicht, entfernt entschlüsseltes Arbeitsmaterial, löscht die Ziel-Entschlüsselungsidentität, erhält redigiertes Audit und sichere Hashes und behält oder entfernt das verschlüsselte Paket gemäß Auswahl.

Ein behaltenes Paket kann nach Löschen der Identität die abgebrochene Sitzung nicht fortsetzen.

## Archivieren und in aktive Liste zurückstellen

Terminale Sitzungen können archiviert werden. Archivierung ändert die Sichtbarkeit, nicht Ressourcen oder Nachweise. Verwenden Sie den Archivfilter und **In aktive Liste zurückstellen**, wenn normale Sichtbarkeit wieder benötigt wird.

Archivierung ist weder Abbruch noch dauerhafte Löschung.

## Dauerhaft löschen

Dauerhafte Löschung ist auf serverseitig nachgewiesene entbehrliche frühe Sitzungen beschränkt und verlangt die genaue Migrations-ID. Sitzungsdatensatz, Paketrevisionen, Validierungshistorie und verifizierte Zielpaketreste können entfernt werden; ein redigierter Auditdatensatz bleibt.

Nicht entbehrlich sind Sitzungen mit Konvertierung, Staging, Ziel-Laufzeit, Produktionsrouten, Annahme, erster nativer Sicherung, Sicherungskatalog-/Restore-Herkunft, Quellqualifizierung oder Altserver-Aufbewahrung.

Angenommene oder abgeschlossene Sitzungen und ihre Grenze der ersten nativen Sicherung müssen erhalten bleiben.

## Aktion prüfen

Bestätigen Sie nach jeder Lebenszyklusaktion Filter, Status, Archivzustand und angezeigte Folgen. Schließen Sie aus einer fehlenden Zeile in der Standardliste niemals auf Löschung.

Verwandt: [Migrationsnachweise, Logs und Supportinformationen verwenden](evidence-and-support.md).

---

# Portainer für erweiterte Containerdiagnose verwenden

Source: `docs/de/tools/portainer.md`
Locale: de
Section: Tools
Status: supported
Applies to: 0.2.x

# Portainer für erweiterte Containerdiagnose verwenden

MEM erklärt einen Fehler; Portainer bietet erweiterte Containerprüfung auf niedriger Ebene. Portainer ersetzt keine MEM-Vorfälle, Supportberichte oder Workflow-Anleitungen, und MEM wird dadurch nicht zu einer allgemeinen Docker-Verwaltungskonsole.

## Portainer aus MEM öffnen

Ein Platform Owner kann `/diagnostics/portainer` öffnen, wenn der Server eine autoritative private Portainer-URL und Umgebungs-ID besitzt. MEM kann anbieten:

- **Portainer öffnen**;
- **Lokale Umgebung öffnen**;
- **Container öffnen**.

Der Browser konstruiert diese Links nicht aus `localhost`, veröffentlichten Ports, Containernamen oder Browser-Origin.

Portainer verwaltet seine eigene Anmeldung. MEM speichert kein Portainer-Passwort, Sitzungstoken oder API-Key im Browser.

## Ersteinrichtung und das Fünf-Minuten-Fenster

Eine frische Portainer-2.39.5-Installation benötigt ein einmal verwendbares Setup-Token, bevor der erste Administrator angelegt werden kann. Portainer schreibt dieses Token in die Serverprotokolle. Lesen Sie auf dem Docker-Host die neueste Token-Zeile mit folgendem Befehl aus:

```bash
docker logs portainer 2>&1 | grep 'setup_token=' | tail -n 1
```

Kopieren Sie den Wert nach `setup_token=` in das Feld **Setup-Token** von Portainer. Das Token kann nur einmal verwendet werden. Der Benutzername ist standardmäßig `admin`, kann aber geändert werden; Portainer verlangt ein Passwort mit mindestens 12 Zeichen.

Der erste Administrator muss weiterhin innerhalb von fünf Minuten angelegt werden. Läuft dieses Fenster ab, starten Sie nur den Portainer-Container neu, öffnen Sie die Oberfläche sofort erneut und führen Sie danach den Token-Befehl erneut aus, damit das neueste Setup-Token verwendet wird:

```bash
docker restart portainer
```

MEM liest oder speichert weder das Portainer-Setup-Token noch das Administratorkennwort. Nach dem Anlegen des ersten Administrators erkennt der Portainer-Einrichtungsassistent die lokale Docker-Umgebung. In hostnativer Entwicklung kann MEM die laufende lokale Portainer-Startseite öffnen, bevor eine exakte Umgebungs-ID konfiguriert wurde; eine exakte Container-Übergabe bleibt bis zur vorhandenen serverseitigen Umgebungsinformation deaktiviert.

## Aktuellen Vorfallscontainer öffnen

Wenn ein Vorfall aktuelle MEM-eigene Docker-Nachweise besitzt, ruft **Diesen Container in Portainer öffnen** einen MEM-Redirect-Endpunkt auf. Der Server löst logische Ressource und aktuelle Containeridentität erst beim Aufruf auf.

Dadurch bleiben Browserlinks nach Container-Neuerstellung nicht veraltet. Nicht unterstützte, entfernte oder nicht auflösbare Ressourcen fallen auf die konfigurierte Containerliste zurück statt auf eine bekannte fehlerhafte Detailseite.

Der Seq-Arbeitsbereich verwendet dasselbe Modell für **Seq-Container in Portainer öffnen**.

## Eigentumsgrenze

Nur MEM-eigene logische Ressourcen sind für kontextbezogene Übergabe zugelassen. Der Browser kann keine beliebige Docker-Container-ID senden. Nicht verwaltete oder identitätsabweichende Ressourcen bleiben schreibgeschützt und werden nicht als MEM-eigen dargestellt.

MEM bietet über Diagnostics keine Container-Aktionen zum Starten, Stoppen, Neustarten, Entfernen, Ausführen, Bereinigen sowie keine Netzwerk- oder Volume-Löschung.

## Freigegebene Laufzeit

Neue MEM-verwaltete Installationen verwenden das exakt freigegebene Portainer-CE-Image:

```text
portainer/portainer-ce:2.39.5
```

Der Installationsworkflow löst die lokale unveränderliche `sha256:`-Imageidentität auf und erstellt den Container daraus. Normale Status-, Start- und Stop-Operationen laden kein Image herunter.

Die Laufzeit verwendet:

- das persistente Volume `portainer_data` unter `/data`;
- den Docker-Socket unter `/var/run/docker.sock`;
- privaten HTTPS-Port `9443`;
- keinen Edge-Agent-Port `8000`, solange kein künftiger expliziter Workflow ihn hinzufügt;
- keine automatische öffentliche NPM-Route.

Eine vorhandene nicht von MEM besessene Portainer-Installation – einschließlich einer funktionierenden 2.39.1-Instanz – wird beobachtet, aber nicht automatisch übernommen, ersetzt, neu markiert, neu gestartet oder aktualisiert.

## Kompatibilität exakter Links

Exakte Ressourcenlinks sind eine Komfortfunktion für den freigegebenen Portainer-2.39-Routenvertrag. Die Containerliste ist der stabile Fallback. Nach einer Versionsänderung muss die Direktlink-Kompatibilität erneut geprüft werden.

Offizielle Referenzen:

- [Portainer-Dokumentation](https://docs.portainer.io/)
- [Portainer CE mit Docker unter Linux installieren](https://docs.portainer.io/start/install-ce/server/docker/linux)
- [Portainer-Ersteinrichtung](https://docs.portainer.io/start/install-ce/server/setup)

## Rollback

Für ein Rollback der Übergabe können MEM-Portainer-Links ausgeblendet werden; Portainer bleibt separat erreichbar. Für ein Runtime-Rollback nur das dokumentierte vorherige unveränderliche Image verwenden und zuvor Datenkompatibilität prüfen. `portainer_data` erhalten und niemals blind downgraden.

Die Fresh-Server-Releaseprüfung soll unveränderliche Imageidentität, Datenpersistenz, Port `9443`, fehlenden Port `8000`, Eigentumslabels, keine öffentliche NPM-Route sowie Direktlink-Fallback prüfen.

## Verwandte Dokumentation

- [Das Diagnose-Kommandozentrum verwenden](../operations/diagnose-kommandocenter.md)
- [Seq mit MEM verwenden](../operations/seq-mit-mem.md)

---

# Resume, cancel, archive, and restore sessions

Source: `docs/migrate/session-lifecycle.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Resume, cancel, archive, and restore sessions

Migration Sessions are durable. Leaving the page or restarting a browser does not turn an in-progress operation into a new migration.

## Outcome

You can return to an existing session, cancel only where safe, archive terminal history, restore it to the list, and understand why most progressed sessions cannot be permanently deleted.

## Resume after refresh or interruption

Open **Migrations**, find the existing session, and continue from its current stage. Running conversion, capture, staging, target creation, cutover, backup, or cleanup operations are server-owned. Review the recorded status before starting another operation.

Do not create a second intake merely because a page was closed or a preview expired. Refresh the stage evidence or readiness snapshot in the existing session.

## Cancel an early session

Cancellation is available only while the target session is still genuinely disposable. The cancellation review requires explicit choices about encrypted target-package retention and acknowledgement that the source host is outside target control.

Target cancellation:

- does not stop, alter, or delete the source server;
- removes decrypted target working material;
- clears the target decryption identity;
- retains redacted audit and safe hashes;
- may retain or remove the encrypted package according to the selected option.

A retained encrypted package cannot resume the cancelled session after the identity is cleared.

## Archive and restore to active list

Terminal sessions can be archived to reduce normal-list noise. Archiving changes visibility, not migration resources or evidence. Use the archived filter to find them and **Restore to active list** when they need normal visibility again.

Archiving is not cancellation and is not permanent deletion.

## Permanent deletion

Permanent deletion is restricted to server-proven disposable early sessions and requires the exact Migration ID. It can remove the session record, package-revision metadata, validation history, and verified target-package remnants while retaining a redacted audit record.

A session is not disposable after it owns or references consequential state such as conversion, staging, a target runtime, production routes, acceptance, first native backup, Backup Catalog or restore provenance, source qualification, or legacy retention.

Accepted or completed sessions and their first-native-backup boundary must be retained.

## Verify the action

After any lifecycle action, confirm the list filter, session status, archived state, and the stated resource/evidence consequences. Never infer deletion from a missing row in the default list.

Related: [Use migration evidence, logs, and support information](evidence-and-support.md).

---

# Choose restore, migration, or ordinary repair

Source: `docs/backups-and-restores/restore-or-migrate.md`
Locale: en
Section: Back up and restore
Status: supported
Applies to: 0.2.x

# Choose restore, migration, or ordinary repair

Backup/Restore and Migration are separate bounded workflows in MEM.

## Use restore when

Use the Backup Catalog and Restore Workspace when you have a supported native MEM recovery payload and need to recreate a Matrix + Element stack while preserving the backed-up Matrix identity.

Typical cases include:

- recovering after host or disk loss;
- recreating a removed or damaged MEM 0.2.x stack;
- moving a native portable backup to another compatible MEM installation;
- proving recoverability through a private test.

## Use MEM Migrate when

Use MEM Migrate when the source is a legacy or different installation that must be assessed, captured, converted, staged, adopted, or cut over.

For MEM 0.2.0, the primary supported migration source is the defined MEM 0.1.0 profile. A legacy `mem-api` / `mem-web` server is not converted merely by importing an arbitrary directory as a backup.

Migration owns source assessment, package capture, compatibility, transformation, private staging, cutover, acceptance, rollback planning, and legacy retention. Only after acceptance does the migrated stack enter the normal Backup Catalog lifecycle through a native baseline backup.

## Use ordinary repair when

Use the owning stack, platform, or diagnostics workflow when the runtime still exists and the problem is a repairable configuration, route, certificate, TURN, federation, or container issue.

Do not restore merely to fix one NPM route or a temporary Docker outage. A restore creates a new production stack and can introduce more risk than a scoped repair.

## Decision questions

1. Is the source already a supported Backup Catalog entry?
2. Must the Matrix server identity remain exactly the same?
3. Does the source require conversion or compatibility assessment?
4. Is the current runtime still intact and repairable?
5. Is an old public homeserver still serving the same Matrix identity?
6. Do you have a tested off-host backup and user encryption-key recovery plan?

When the answers are unclear, stop before production mutation. Preserve the current source, collect diagnostics, and choose the workflow that owns the actual problem.

---

# Wiederherstellen, migrieren oder reparieren?

Source: `docs/de/backups-und-wiederherstellen/wiederherstellen-oder-migrieren.md`
Locale: de
Section: Sichern und wiederherstellen
Status: supported
Applies to: 0.2.x

# Wiederherstellen, migrieren oder reparieren?

Backup/Restore und Migration sind getrennte Bereiche in MEM.

## Wiederherstellung verwenden

Verwenden Sie Sicherungskatalog und Wiederherstellungsarbeitsbereich bei einem unterstützten nativen MEM-Payload, wenn ein Matrix- und Element-Stack mit derselben gesicherten Matrix-Identität neu erstellt werden soll.

Typische Fälle:

- Host- oder Datenträgerverlust;
- beschädigter oder entfernter MEM-0.2.x-Stack;
- Übertragung eines nativen portablen Backups auf eine kompatible MEM-Installation;
- Recovery-Nachweis durch privaten Test.

## MEM Migrate verwenden

MEM Migrate ist für ältere oder unterschiedliche Quellen gedacht, die bewertet, erfasst, konvertiert, privat bereitgestellt, übernommen oder umgeschaltet werden müssen.

Für MEM 0.2.0 ist das primäre unterstützte Quellprofil die definierte MEM-0.1.0-Installation. Ein alter Server mit `mem-api` und `mem-web` wird nicht durch den Import eines beliebigen Verzeichnisses als Backup konvertiert.

Migration besitzt Quellbewertung, Paketerfassung, Kompatibilität, Transformation, privates Staging, Cutover, Abnahme, Rückfallplanung und Legacy-Aufbewahrung. Erst nach Abnahme gelangt der migrierte Stack durch eine native Basissicherung in den normalen Sicherungskatalog.

## Normale Reparatur verwenden

Wenn der Runtime noch existiert und das Problem eine begrenzte Konfigurations-, Routen-, Zertifikats-, TURN-, Föderations- oder Containerstörung ist, verwenden Sie den zuständigen Stack-, Plattform- oder Diagnoseablauf.

Stellen Sie nicht nur wegen einer einzelnen NPM-Route oder eines temporären Docker-Ausfalls wieder her. Eine Wiederherstellung erstellt einen neuen Produktions-Stack und kann mehr Risiko verursachen als eine gezielte Reparatur.

## Entscheidungsfragen

1. Ist die Quelle bereits ein unterstützter Katalogeintrag?
2. Muss die Matrix-Identität exakt gleich bleiben?
3. Benötigt die Quelle Konvertierung oder Kompatibilitätsbewertung?
4. Ist der aktuelle Runtime noch intakt und reparierbar?
5. Bedient ein alter öffentlicher Homeserver noch dieselbe Matrix-Identität?
6. Gibt es ein getestetes externes Backup und einen Plan für Benutzerschlüssel?

Bei Unklarheit vor Produktionsmutation stoppen, Quelle erhalten, Diagnosen sammeln und den Ablauf wählen, der das tatsächliche Problem besitzt.

---

# Migrationsnachweise, Logs und Supportinformationen verwenden

Source: `docs/de/migrieren/evidence-and-support.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Migrationsnachweise, Logs und Supportinformationen verwenden

Eine Migration umfasst zwei Hosts und mehrere dauerhafte Operationen. Gute Nachweise identifizieren Quelle, Paket, Zielversuch, Routenzustand und Prüfung, ohne Geheimnisse offenzulegen.

## Ergebnis

Sie sammeln die minimal erforderlichen sicheren Informationen zur Diagnose und bewahren Audit- und Supportnachweise auf.

## Nachweise auf der Quelle

Bewahren Sie auf:

- Source-Assistant-Version und Listener-Information;
- Bewertungs-ID, Klassifizierung, Empfehlung und Fingerabdruck;
- ausgewählte Stack-Identität;
- Ziel-Aufnahme-ID und öffentlichen Empfängerfingerabdruck;
- Erfassungs-ID und Abschlusszustand;
- Paketname, Art, Größe, SHA-256 und Paketbericht;
- Abbruch- oder lokale Löschaufzeichnungen.

Teilen Sie keinen Start-Zugriffscode, Roh-Signaturschlüssel, Passwörter, Raumschlüssel-Exporte, Tokens oder private Quelldateien.

## Nachweise auf dem Ziel

Bewahren oder laden Sie sichere Informationen zu Aufnahme und Paketvalidierung, Quellidentität, Konvertierungsversuchen, Kandidaten-Hashes, privatem Staging, Ziel-Vorprüfung, Bereitschaftsrevision, Nginx-Proxy-Manager-Routensnapshot, Produktionsprüfung, Annahme, Aufbewahrung, erster nativer Sicherung, Bereinigung, Abschlussbericht und Lebenszyklusaktionen auf.

Nennen Sie bei Supportanfragen Migrations-ID, Operations-ID, Zeitstempel und stabile Fehler- oder Problemcodes.

## Sichere Supportzusammenfassung

```text
Quellbewertungs-ID: <id>
Ausgewählter Quell-Stack: <slug>
Quellfingerabdruck: <sicherer Fingerabdruck>
Ziel-Migrations-ID: <id>
Aktueller geführter Schritt: <schritt>
Letzte Operation: <art / status / zeitstempel>
Paket-SHA-256: <hash>
Privater Test: <nicht gestartet / bestanden / fehlgeschlagen>
Routenhoheit: <alt / neu / wiederhergestellt / unbekannt>
Produktionsprüfung: <status>
Erste native Sicherung: <status>
Beobachteter Fehlercode: <code>
```

Redigieren Sie Benutzeridentitäten, sofern nicht erforderlich. Fügen Sie niemals Datenbank-Verbindungszeichenfolgen, Autorisierungsheader, Zugriffscodes, Wiederherstellungsgeheimnisse, Signaturschlüsselinhalt oder entschlüsseltes Paketmaterial ein.

## Bei unbekannter Routenhoheit

Priorisieren Sie vorherigen Routensnapshot, Anwendungsergebnis, Rollback-Ergebnis, aktuelle Nginx-Proxy-Manager-Ziele und öffentliche Bereitschaftsprüfungen. Raten Sie nicht anhand eines geladenen Browser-Tabs.

## Nachweise aufbewahren

Bewahren Sie Abschlussbericht und Nachweis der ersten nativen Sicherung mit Release- und Änderungsunterlagen auf. Eine archivierte Sitzung bleibt der dauerhafte Zielnachweis.

Verwandt: [Rollback-Grenzen](rollback.md) und [Bereinigung](cleanup.md).

---

# Das Diagnose-Kommandozentrum verwenden

Source: `docs/de/operations/diagnose-kommandocenter.md`
Locale: de
Section: Operations (Deutsch)
Status: supported
Applies to: 0.2.x

# Das Diagnose-Kommandozentrum verwenden

MEM Diagnostics ist die interpretierte Operator-Ebene für Fehler der Control Plane und betriebliche Nachweise. Öffnen Sie **Diagnose** über die Operator-Navigation oder verwenden Sie das Warnsymbol im Header, wenn ein Vorfall Aufmerksamkeit benötigt.

## Die Zustandsleiste richtig lesen

Die Zustandsleiste meldet unabhängig:

- die allgemeine Diagnosebereitschaft;
- Vorfälle mit aktuellem Handlungsbedarf;
- den persistenten lokalen CLEF-Recorder;
- den browser-sicheren Ereignisspeicher;
- optionale Seq-Zustellung und Seq-Laufzeit.

Ein neutraler Seq-Zustand macht MEM nicht ungesund. Ein grüner Gesamtzustand darf einen deaktivierten oder nicht verfügbaren Recorder beziehungsweise Ereignisspeicher nicht verdecken.

## Die drei Nachweisebenen verstehen

### Vorfälle

Vorfälle gruppieren Warnungen, Fehler oder kritische Ereignisse, die Operator-Aufmerksamkeit benötigen. Sie enthalten eine sichere Zusammenfassung, Feature, Zeitpunkte, Korrelationsreferenzen und – wenn eindeutig – einen Link zum zuständigen Arbeitsbereich.

### Sichere Ereignisse

Der sichere Ereignisspeicher enthält browser-lesbare, redigierte Betriebsereignisse. Information-Ereignisse können normale Aktivität belegen, ohne einen Vorfall zu erzeugen. Unbeschränkte Ausnahmen, Geheimnisse und Hostpfade werden nicht veröffentlicht.

### Technischer CLEF-Recorder

Der lokale CLEF-Recorder ist das breitere strukturierte Black-Box-Protokoll. Er bleibt nützlich, wenn kein passendes sicheres Ereignis vorhanden ist oder die API nicht verfügbar ist. Prüfen Sie eine vollständige Recorder-Datei auf sensible Betriebsmetadaten, bevor Sie sie weitergeben.

## Technische Ereignisse finden und filtern

Die Übersicht trennt **Aufmerksamkeit** von **technischer Aktivität**. Ein technisches Ereignis mit Warnungsstufe kann ein nützlicher Nachweis sein, ohne einen Vorfall zu erzeugen, der eine Betreiberaktion erfordert.

Wählen Sie den Zähler für Vorfälle, Warnungen, Fehler, kritische Ereignisse, Informationen oder alle Ereignisse, um die entsprechende Ansicht zu öffnen. Schweregrad-, Funktions- und Suchfilter werden in der URL gespeichert, sodass die Ansicht aktualisiert oder als Lesezeichen gespeichert werden kann.

Die Ansicht **Technische Ereignisse** zeigt standardmäßig die neuesten sicheren Ereignisse ohne Schweregradfilter. Wählen Sie **50 weitere Ereignisse laden**, um die nächste begrenzte Cursor-Seite abzurufen. MEM verwendet bewusstes Cursor-Laden statt automatischem Infinite Scroll, damit Tastaturfokus, Browserspeicher und Nachweisgrenzen vorhersehbar bleiben.

Wenn kein Vorfall Aufmerksamkeit erfordert, aber sichere Ereignisse vorhanden sind, führt der leere Vorfallszustand direkt zu den technischen Ereignissen.

## Die Diagnose-Pipeline prüfen

Ein Platform Owner kann **Diagnose-Pipeline prüfen** unter `/diagnostics` oder im Protokollzustand ausführen.

Die Prüfung:

1. prüft die Schreibbarkeit des lokalen Recorders;
2. schreibt ein harmloses Information-Ereignis;
3. liest genau dieses sichere Ereignis zurück;
4. prüft die Korrelation;
5. meldet Seq getrennt als erfolgreich, fehlgeschlagen, deaktiviert oder nicht konfiguriert.

Sie erzeugt keinen Warnungs- oder Fehler-Vorfall und verändert Docker nicht.

## Das Warnsymbol verwenden

Das Warnsymbol verwendet serverseitigen Vorfallszustand. Es zeigt höchstens fünf aktuelle Warnungen, Fehler oder kritische Vorfälle und verlinkt den exakten Vorfall. Es ist kein Benachrichtigungspostfach: Es gibt keinen browserlokalen Gelesen-Status, keine dauerhafte Ausblendung, Zuweisung oder Bestätigung.

Wenn der Attention-Endpunkt nicht verfügbar ist, darf das Symbol nicht fälschlich Entwarnung anzeigen.

## Supportübergabe erstellen

Öffnen Sie einen Vorfall und verwenden Sie **Support-JSON kopieren** oder **Supportbericht herunterladen**. Docker-Nachweise sind begrenzt und optional. Prüfen Sie den Bericht vor dem Teilen, da Hostnamen, Stacknamen, Zeitpunkte, Ereigniscodes und Topologie sensibel sein können.

Teilen Sie niemals Zugangsdaten, Tokens, Recovery-Codes, Signaturschlüssel, private Schlüssel, Datenbank-Dumps, unbeschränkte Konfigurationsdateien oder vollständige rohe Logs in einer normalen Supportanfrage.

## Wenn die API nicht verfügbar ist

Wenn eine bereits geladene Diagnose-Seite die API nicht erreicht, verwenden Sie die externen Fallback-Pfade:

```bash
sudo docker logs --tail 500 mem-control-plane
```

Sie können außerdem den kanonischen Container `mem-control-plane` in Portainer und den konfigurierten persistenten CLEF-Speicher auf dem Host prüfen. Die installierte Single-Page-Anwendung kann bei einem vollständigen Kestrel-Ausfall nicht verfügbar bleiben; externe Prüfung ist daher beabsichtigt.

## Rollen

- **Auditor:** sichere Übersicht, Attention-Zusammenfassungen und Vorfallszusammenfassungen.
- **Operator:** technische Ereignisse, Supportberichte, Protokollzustand und vorfallsgebundene Docker-Nachweise.
- **Platform Owner:** Operator-Funktionen sowie Pipeline-Prüfung, Seq-Verwaltung und Portainer-Übergabe.

Ausgeblendete Bedienelemente sind keine Autorisierung. Jede Operation wird serverseitig erneut autorisiert.

## Rollback und Wiederherstellung

Wenn eine neue Diagnose-Oberfläche Probleme verursacht, kann diese Web-Oberfläche ausgeblendet oder zurückgerollt werden, während Vorfälle, Ereignisse, Recorder und Supportberichte erhalten bleiben. Das Deaktivieren von Seq oder Portainer darf MEM-native Diagnostics nicht deaktivieren.

## Verwandte Dokumentation

- [Seq mit MEM verwenden](seq-mit-mem.md)
- [Portainer für erweiterte Containerdiagnose verwenden](../tools/portainer.md)
- [Nachweise, Logs und Supportberichte verwenden](../backups-und-wiederherstellen/nachweise-logs-support.md)

---

# Use migration evidence, logs, and support information

Source: `docs/migrate/evidence-and-support.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Use migration evidence, logs, and support information

A migration crosses two hosts and several durable operations. Good evidence identifies the exact source, package, target attempt, route state, and verification result without exposing secrets.

## Outcome

You can collect the minimum safe evidence needed to diagnose a failed or uncertain migration and retain the records needed for later audit or support.

## Source-side evidence

Retain:

- Source Assistant version and listener information;
- source assessment ID, classification, recommendation, and fingerprint;
- selected stack identity;
- target request intake ID and public recipient fingerprint;
- capture ID and completion state;
- package filename, kind, size, SHA-256, and package report;
- cancellation or local-deletion records where applicable.

Do not share the startup access code, raw signing key, passwords, room-key exports, tokens, or private source files.

## Target-side evidence

Retain or download the safe information available for:

- intake and package validation;
- source identity and compatibility review;
- conversion attempts and verified candidate hashes;
- private staging creation and health checks;
- target preflight and ownership claims;
- readiness or preview revision;
- Nginx Proxy Manager route snapshot and apply outcome;
- production verification;
- acceptance and legacy retention;
- first native backup;
- staging cleanup;
- completion report and session lifecycle actions.

Use the Migration ID, operation ID, timestamps, and stable error or problem codes when asking for help.

## Safe support summary

A useful report states:

```text
Source assessment ID: <id>
Selected source stack: <slug>
Source fingerprint: <safe fingerprint>
Target Migration ID: <id>
Current guided stage: <stage>
Last operation: <kind / status / timestamp>
Package SHA-256: <hash>
Private test: <not started / passed / failed>
Route ownership: <old / new / restored / unknown>
Production verification: <status>
First native backup: <status>
Observed error code: <code>
```

Redact public user identities where they are not required. Never paste database connection strings, authorization headers, access codes, recovery secrets, signing-key content, or decrypted package content into a support request.

## When route ownership is uncertain

Prioritize the recorded pre-cutover snapshot, apply result, rollback result, current Nginx Proxy Manager targets, and public readiness checks. Do not guess based only on which browser tab loaded.

## Evidence retention

Keep the completion report and first native backup evidence with your release and change records. An archived session remains the durable target record; archiving is not evidence deletion.

Related: [Rollback boundaries](rollback.md) and [Retain or clean up migration resources](cleanup.md).

---

# Seq mit MEM verwenden

Source: `docs/de/operations/seq-mit-mem.md`
Locale: de
Section: Operations (Deutsch)
Status: supported
Applies to: 0.2.x

# Seq mit MEM verwenden

Seq ist eine optionale erweiterte Suchschicht für strukturierte MEM-Ereignisse. MEM-native Vorfälle, sicherer Ereignisspeicher, persistenter CLEF-Recorder und Supportberichte bleiben der primäre Produktpfad, wenn Seq fehlt, gestoppt oder deaktiviert ist.

## Zustellung und Laufzeit trennen

MEM zeigt zwei getrennte Steuerungen:

```text
MEM-Ereigniszustellung an Seq
Seq-Containerlaufzeit
```

Das Stoppen von Seq deaktiviert MEM-native Diagnostics nicht. Das Deaktivieren der Zustellung stoppt neue MEM-Ereignisse nach dem erforderlichen API-Neustart; Container und vorhandene Seq-Daten bleiben erhalten.

## Seq mit MEM einrichten

Wenn Seq fehlt, kann ein Platform Owner **Seq einrichten** wählen. Der geführte Ablauf:

1. erklärt freigegebenes Image, private Laufzeit, dauerhafte Daten und den Verzicht auf öffentlichen Ingress;
2. zeichnet die ausdrückliche Zustimmung zur Seq-EULA auf;
3. nimmt ein einmaliges anfängliches Administratorkennwort mit Bestätigung entgegen oder fragt einmal nach dem aktuellen Administratorkennwort, wenn bestehende Autorität erhalten bleiben muss;
4. erstellt eine eingefrorene Prüfung ohne Kennwort oder Geheimnis und enthält die Zustellungswahl des Betreibers, die standardmäßig nach dem nächsten API-Neustart aktiviert wird;
5. verlangt eine frische Identitätsprüfung, bevor ein einmaliges Administratorkennwort an den Ausführungsendpunkt gesendet wird;
6. bereitet nur das freigegebene exakte Image vor, wenn es noch nicht lokal vorhanden ist;
7. prüft und bereitet das serververwaltete Datenverzeichnis vor, bevor die Erststartautorität verändert wird;
8. hasht ein neues Administratorkennwort über isolierte Standardeingabe nur dann, wenn Erststartautorität erforderlich ist, und erhält bei einem erneuten Versuch einen vorhandenen konfigurierten Administrator-Hash;
9. erstellt oder startet den von MEM verwalteten Container und meldet Laufzeitbereitschaft erst nach erfolgreicher Docker- und Seq-`/health`-Prüfung;
10. stellt einen dedizierten reinen MEM-Ingest-Schlüssel bereit oder verwendet ihn erneut und sendet ein harmloses Prüfereignis;
11. fragt Seq nach genau diesem Prüfereignis ab, bevor die MEM-Verbindung als geprüft gilt;
12. wenn Ereigniszustellung ausgewählt wurde, merkt MEM den gewünschten Zustellungszustand für den nächsten Start der MEM-API vor; andernfalls bleibt die laufende Zustellung deaktiviert.

Der Browser sendet keine Image-Referenz, Hostpfade, Container-IDs, Docker-Netzwerke oder Ports. Diese Bereitstellungsziele bleiben serververwaltet. Klartext-Administratorkennwörter werden nur im geschützten Ausführungsschritt verwendet und weder im Browserspeicher noch in Vorgangsnachweisen oder Serverdateien gespeichert. Der dedizierte Ingest-Schlüssel bleibt serverseitig und wird nicht an den Browser zurückgegeben.

Findet MEM einen vorhandenen konfigurierten Administratorkennwort-Hash, erhält der Assistent diese Autorität, statt sie zu ersetzen. Das aktuelle Seq-Administratorkennwort wird einmal für den geschützten Verbindungs- und Bereitstellungsschritt verwendet und danach verworfen. Initialisierte Seq-Daten ohne das zugehörige Administratorgeheimnis blockieren die geführte Einrichtung und benötigen eine ausdrückliche Wiederherstellung.

Die private Seq-UI-URL ist optional. Ohne URL kann die Bereitstellung erfolgreich sein. Nach der Bereitstellung kann ein Platform Owner über **Seq-Zugriff konfigurieren** eine serverseitig genehmigte private LAN-, VPN- oder SSH-Tunnel-Adresse speichern oder ändern. MEM zeigt anschließend **Seq öffnen** in einem neuen Tab an. Speichern Sie nicht `0.0.0.0`; dies ist eine Bind-Adresse und kein Browserziel.

Wird die Laufzeit fehlerfrei, aber Bereitstellung, Prüfung der MEM-Verbindung oder Vorbereitung der Zustellung scheitert, erhält MEM die fehlerfreie Seq-Laufzeit und ihre Daten und meldet, dass die Einrichtung Aufmerksamkeit benötigt. Verwenden Sie die normalen Steuerelemente **MEM mit Seq verbinden** oder die Zustellungssteuerung, um nur den verbleibenden Schritt zu wiederholen; eine fehlerfreie Laufzeit soll dafür nicht erneut bereitgestellt werden.

Wenn die Einrichtung mit ausgewählter Ereigniszustellung abgeschlossen wird, wird der aktuelle API-Prozess nicht stillschweigend neu konfiguriert. Der Seq-Arbeitsbereich meldet den erforderlichen API-Neustart und verwendet den serverseitigen Neustartvertrag des aktiven Laufzeitkontexts. Stellt der Server einen exakten Neustartbefehl bereit, zeigt MEM ihn zum Kopieren an. Öffnen Sie nach dem Neustart den Arbeitsbereich erneut und prüfen Sie, dass aktueller und gewünschter Zustellungszustand übereinstimmen und normale strukturierte Ereignisse in Seq eintreffen.

## Laufzeitsteuerung

Abhängig von serverseitigen Capabilities kann ein Platform Owner:

- das freigegebene lokale Image bereitstellen;
- die MEM-verwaltete Laufzeit starten, stoppen oder neu starten;
- eine begrenzte Zustandsprüfung ausführen;
- künftige Ereigniszustellung aktivieren oder deaktivieren;
- den verwalteten Container entfernen und das Datenverzeichnis erhalten.

Bereitstellung, Zustellungsänderungen und Entfernen verlangen frische Identitätsprüfung. Stoppen und Neustarten verlangen Bestätigung. Start und Neustart gelten erst nach geprüftem Docker- und Seq-Zustand als erfolgreich.

Ein nicht verwalteter gleichnamiger Container oder eine abweichende unveränderliche Identität blockiert Änderungen.

## Zustellungsänderungen anwenden

Zustellungspräferenzen werden vorgemerkt. Der Arbeitsbereich zeigt aktuellen und gewünschten Zustand und nennt einen erforderlichen API-Neustart.

Nach einer Änderung:

1. gewünschten Zustand unter `/diagnostics/seq` prüfen;
2. MEM API bewusst neu starten;
3. Arbeitsbereich erneut öffnen;
4. prüfen, dass aktueller und gewünschter Zustand übereinstimmen;
5. Zustandsprüfung oder Pipeline-Prüfung ausführen.

Eine gespeicherte Präferenz bedeutet nicht, dass der aktuelle Prozess bereits anders zustellt.

## Seq sicher entfernen

Das Entfernen ist blockiert, solange aktuelle oder gewünschte Zustellung aktiviert ist. Der konfigurierte Seq-Datenpfad bleibt nach erfolgreichem Entfernen erhalten. Dauerhafte Datenlöschung gehört nicht zu diesem Workflow.

## Erste Suchabfragen

Nützliche Felder sind Vorfalls-ID, Vorgangs-ID, Feature, Ereigniscode, Ressourcenart sowie Warnungs- oder Fehlerstufe. Der Arbeitsbereich bietet kopierbare Beispiele für die freigegebene Seq-Version.

Offizielle Referenzen:

- [Seq-Dokumentation](https://datalust.co/docs)
- [Seq mit Docker ausführen](https://datalust.co/docs/getting-started?platform=docker)
- [Seq-Abfragesprache](https://datalust.co/docs/the-seq-query-language)
- [Abfragesyntax](https://datalust.co/docs/query-syntax)

## Sicherheitsgrenze

Seq-Zugangsdaten bleiben serverseitig. Speichern Sie Ingestion-Key, Administrator-Passwort-Hash oder Geheimnisdateien niemals im Browser, Supportbericht, Screenshot oder normalen Logeintrag.

Seq ist privates Operator-Werkzeug. MEM erzeugt dafür nicht automatisch öffentlichen Ingress.

## Rollback

Für ein Web-Rollback kann die Seq-Verwaltungsroute ausgeblendet werden, ohne Laufzeit oder MEM-native Diagnostics zu entfernen. Zum Stoppen der Zustellung gewünschten Zustand deaktivieren und API neu starten. Das Entfernen der Laufzeit erhält Daten; Datenlöschung benötigt einen getrennten zukünftigen Workflow.

## Verwandte Dokumentation

- [Das Diagnose-Kommandozentrum verwenden](diagnose-kommandocenter.md)
- [Portainer für erweiterte Containerdiagnose verwenden](../tools/portainer.md)

---

# Migration, Sicherung und Wiederherstellung oder Reparatur wählen

Source: `docs/de/migrieren/migration-or-restore.md`
Locale: de
Section: Von MEM 0.1.0 migrieren
Status: supported
Applies to: 0.2.x

# Migration, Sicherung und Wiederherstellung oder Reparatur wählen

Migration und Sicherung/Wiederherstellung teilen Validierungs- und Laufzeitfähigkeiten, sind aber getrennte Betreiberabläufe mit unterschiedlichen Quellen und Nachweisen.

## Ergebnis

Sie wählen den richtigen Ablauf und stellen nicht angenommenes Migrationsmaterial nicht in den Sicherungskatalog.

## Migration verwenden

- Quelle ist eine unterstützte ältere MEM-0.1.0-Installation.
- Quelle muss bewertet und konvertiert werden, bevor sie ein normaler MEM-0.2.0-Stack wird.
- Source-Assistant-Erfassung, verschlüsselte Aufnahme, privates Staging, geführte Routenumstellung und Annahme sind erforderlich.

Migration besitzt Rohpakete, Konvertierungsversuche, Kandidaten, Staging, Produktionsübernahme, Rollback-Nachweise und Quellaufbewahrung.

## Sicherung und Wiederherstellung verwenden

- Quelle ist ein nativer MEM-Sicherungskatalog-Eintrag oder unterstützter portabler MEM-Export.
- Ein verwalteter MEM-Stack wird aus einem nativen Recovery-Payload wiederhergestellt oder neu erstellt.
- Keine Quellbewertung oder Konvertierung ist nötig.

Ein Wiederherstellungsarbeitsbereich akzeptiert kein Roh-Migrationspaket.

## Normale Reparatur verwenden

- Der bestehende verwaltete Stack bleibt der beabsichtigte Server.
- Das Problem betrifft Container, Zertifikat, DNS, TURN, Föderation, Speicher, Konten oder Laufzeitgesundheit.
- Weder Neuerstellung noch Übernahme einer anderen Quelle ist nötig.

Verwenden Sie Stack-Diagnose und normale Operationen.

## Annahmegrenze

Vor der Annahme bleiben verschlüsseltes Paket, validiertes Quellarchiv, Konvertierungsausgabe, fehlgeschlagener oder temporärer Kandidat, private Staging-Laufzeit und nicht angenommene normale Ziel-Laufzeit ausschließlich Migrationsmaterial. Sie dürfen nicht als normale Katalogeinträge erscheinen.

Nach Produktionsprüfung und Annahme erstellt MEM die erste native Sicherung. Erst dann treten Stack und Sicherung in den normalen Sicherungskatalog- und Wiederherstellungslebenszyklus ein.

## Entscheidungstabelle

| Situation | Richtiger Ablauf |
|---|---|
| Unterstütztes MEM 0.1.0 nach MEM 0.2.0 verschieben | Migration |
| MEM 0.2.0 aus nativer portabler Sicherung wiederherstellen | Sicherung und Wiederherstellung |
| Sicherungskatalog-Quelle privat testen | Privater Test im Wiederherstellungsarbeitsbereich |
| TURN, Föderation, DNS oder gestoppten Container reparieren | Normale Operation oder Diagnose |
| Beliebigen Synapse-Server importieren | Von diesem Migrationsleitfaden nicht unterstützt |

Verwandt: [Chatserver sichern und wiederherstellen](../backups-und-wiederherstellen/index.md).

---

# Choose migration, backup and restore, or ordinary repair

Source: `docs/migrate/migration-or-restore.md`
Locale: en
Section: Migrate from MEM 0.1.0
Status: supported
Applies to: 0.2.x

# Choose migration, backup and restore, or ordinary repair

Migration and Backup/Restore share validation and runtime capabilities, but they are separate operator workflows with different sources and evidence.

## Outcome

You choose the correct workflow and do not place unaccepted migration material into the Backup Catalog.

## Use migration when

- the source is a supported legacy MEM 0.1.0 installation;
- the source must be assessed and converted before it can become a normal MEM 0.2.0 stack;
- you need Source Assistant capture, encrypted target intake, private staging, guided route cutover, and acceptance.

Migration owns raw packages, conversion attempts, candidates, staging, production adoption, rollback evidence, and legacy-source retention.

## Use Backup and Restore when

- the source is an existing native MEM Backup Catalog entry or a supported portable MEM backup export;
- you are recovering or recreating a MEM-managed stack from a native recovery payload;
- no legacy-source assessment or conversion is required.

A Restore Workspace does not accept a raw migration package as its source.

## Use ordinary repair when

- the existing managed stack remains the intended server;
- the problem is a stopped container, certificate, DNS, TURN, federation, storage, account, or runtime-health issue;
- recreating or adopting a different source is unnecessary.

Use stack diagnostics and normal operations rather than creating a migration or restore attempt for a routine fault.

## The acceptance boundary

Before acceptance, these remain migration-only material:

- encrypted source package;
- validated source archive;
- conversion output;
- failed or temporary candidate;
- private staging runtime;
- unaccepted normal target runtime.

They must not appear as ordinary Backup Catalog entries.

After production verification and acceptance, MEM creates the first native backup. That accepted stack and backup then enter the normal Backup Catalog and Restore Workspace lifecycle.

## Decision table

| Situation | Correct workflow |
|---|---|
| Move supported MEM 0.1.0 into MEM 0.2.0 | Migration |
| Recover MEM 0.2.0 from a native portable backup | Backup and Restore |
| Test a Backup Catalog source privately | Restore Workspace private test |
| Fix TURN, federation, DNS, or a stopped managed container | Ordinary operation or diagnostics |
| Import an arbitrary Synapse server | Not supported by this migration guide |

Related: [Back up and restore chat servers](../backups-and-restores/index.md).
