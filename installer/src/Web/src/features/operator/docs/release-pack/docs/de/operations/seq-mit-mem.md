---
title: Seq mit MEM verwenden
description: Optionale Seq-Zustellung und Laufzeit sicher einrichten und steuern, ohne MEM-native Diagnostics zu ersetzen oder Geheimnisse offenzulegen.
section: Operations (Deutsch)
order: 135
---

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
