---
title: Chatserver sichern und wiederherstellen
description: Schützen Sie Matrix-Daten mit dem Sicherungskatalog und stellen Sie sie über einen dauerhaften Wiederherstellungsarbeitsbereich wieder her.
section: Sichern und wiederherstellen
order: 0
---

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
