---
title: Mit Standard-Neuerstellung wiederherstellen
description: Erstellen Sie einen echten Matrix- und Element-Stack aus einem Katalog-Payload über den unterstützten Produktionsweg.
section: Sichern und wiederherstellen
order: 70
---

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
