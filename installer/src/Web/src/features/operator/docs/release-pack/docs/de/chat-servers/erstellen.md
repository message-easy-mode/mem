---
title: Chatserver erstellen
description: Erstellen Sie einen neuen Matrix- und Element-Runtime-Stack und prüfen Sie die von MEM verwalteten Ressourcen.
section: Chatserver betreiben
order: 10
---

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
