---
title: Domains, Zertifikate und Verlängerung betreiben
description: Domains registrieren, Domain-eigene Zertifikate ausstellen, aktives Zertifikat und Hauptdomain verwalten und automatische Verlängerung sicher betreiben.
section: Operations (Deutsch)
order: 115
---

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
