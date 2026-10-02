---
title: Öffentliche Domain und Zertifikatsplan wählen
description: Validieren Sie Domain und DNS-Zugriff und bereiten Sie den Wildcard-Zertifikatsplan vor, ohne öffentliches DNS zu verändern.
section: Installation
order: 50
---

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
