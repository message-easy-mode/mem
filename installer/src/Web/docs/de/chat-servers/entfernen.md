---
id: "de/chat-servers/remove"
translationKey: "chat-servers/remove"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "Chatserver entfernen"
description: "Entfernen Sie gelistete Stacks dauerhaft und bereinigen Sie unterbrochene, unvollständige oder nicht gelistete Stack-Datensätze über den geschützten Laufzeit-Abgleich."
order: 100
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Stack entfernen", "Laufzeit zerstören", "Laufzeit-Abgleich", "unterbrochene Entfernung", "Datenaufbewahrung", "Step-up", "Routen"]
route: "/docs/de/chat-servers/remove"
aliases: []
outputPath: "docs/de/chat-servers/entfernen.md"
preserveLegacyBranding: false
---
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
