---
id: "de/chat-servers/voice-and-video"
translationKey: "chat-servers/voice-and-video"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "TURN für Sprache und Video konfigurieren"
description: "Prüfen Sie den verbindlichen Synapse-TURN-Zustand und verbinden oder trennen Sie den gemeinsamen Plattform-coturn-Dienst sicher."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["TURN", "coturn", "Sprache", "Video", "Synapse"]
route: "/docs/de/chat-servers/voice-and-video"
aliases: []
outputPath: "docs/de/chat-servers/sprache-und-video.md"
preserveLegacyBranding: false
---
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
