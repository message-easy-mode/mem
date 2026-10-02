---
id: "de/operations/diagnostics-command-centre"
translationKey: "operations/diagnostics-command-centre"
locale: "de"
groupId: "operations-de"
groupKey: "operations"
groupLabel: "Operations (Deutsch)"
groupOrder: 40
title: "Das Diagnose-Kommandozentrum verwenden"
description: "MEM-Vorfälle, sichere Ereignisse, CLEF-Aufzeichnung, Pipeline-Prüfung, Aufmerksamkeit, Supportberichte und Ausfall-Fallback verstehen."
order: 130
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Diagnose", "Vorfälle", "Protokollzustand", "Supportbericht", "Warnsymbol"]
route: "/docs/de/operations/diagnose-kommandocenter"
aliases: []
outputPath: "docs/de/operations/diagnose-kommandocenter.md"
preserveLegacyBranding: false
---
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
