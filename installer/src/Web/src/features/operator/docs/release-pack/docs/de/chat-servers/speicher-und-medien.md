---
title: Speicher und Medien verstehen
description: Nutzen Sie das Flotteninventar Speicher und den Bereich Dienste eines Chatservers, um Matrix-Medien und wiederherstellungsrelevante Speichernachweise zu prüfen.
section: Chatserver betreiben
order: 80
---

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
