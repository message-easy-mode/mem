---
title: Migrationsnachweise, Logs und Supportinformationen verwenden
description: Sammeln Sie sichere Bewertungs-, Paket-, Konvertierungs-, Staging-, Routen-, Prüf- und Abschlussnachweise zur Diagnose einer Migration.
section: Von MEM 0.1.0 migrieren
order: 130
---

# Migrationsnachweise, Logs und Supportinformationen verwenden

Eine Migration umfasst zwei Hosts und mehrere dauerhafte Operationen. Gute Nachweise identifizieren Quelle, Paket, Zielversuch, Routenzustand und Prüfung, ohne Geheimnisse offenzulegen.

## Ergebnis

Sie sammeln die minimal erforderlichen sicheren Informationen zur Diagnose und bewahren Audit- und Supportnachweise auf.

## Nachweise auf der Quelle

Bewahren Sie auf:

- Source-Assistant-Version und Listener-Information;
- Bewertungs-ID, Klassifizierung, Empfehlung und Fingerabdruck;
- ausgewählte Stack-Identität;
- Ziel-Aufnahme-ID und öffentlichen Empfängerfingerabdruck;
- Erfassungs-ID und Abschlusszustand;
- Paketname, Art, Größe, SHA-256 und Paketbericht;
- Abbruch- oder lokale Löschaufzeichnungen.

Teilen Sie keinen Start-Zugriffscode, Roh-Signaturschlüssel, Passwörter, Raumschlüssel-Exporte, Tokens oder private Quelldateien.

## Nachweise auf dem Ziel

Bewahren oder laden Sie sichere Informationen zu Aufnahme und Paketvalidierung, Quellidentität, Konvertierungsversuchen, Kandidaten-Hashes, privatem Staging, Ziel-Vorprüfung, Bereitschaftsrevision, Nginx-Proxy-Manager-Routensnapshot, Produktionsprüfung, Annahme, Aufbewahrung, erster nativer Sicherung, Bereinigung, Abschlussbericht und Lebenszyklusaktionen auf.

Nennen Sie bei Supportanfragen Migrations-ID, Operations-ID, Zeitstempel und stabile Fehler- oder Problemcodes.

## Sichere Supportzusammenfassung

```text
Quellbewertungs-ID: <id>
Ausgewählter Quell-Stack: <slug>
Quellfingerabdruck: <sicherer Fingerabdruck>
Ziel-Migrations-ID: <id>
Aktueller geführter Schritt: <schritt>
Letzte Operation: <art / status / zeitstempel>
Paket-SHA-256: <hash>
Privater Test: <nicht gestartet / bestanden / fehlgeschlagen>
Routenhoheit: <alt / neu / wiederhergestellt / unbekannt>
Produktionsprüfung: <status>
Erste native Sicherung: <status>
Beobachteter Fehlercode: <code>
```

Redigieren Sie Benutzeridentitäten, sofern nicht erforderlich. Fügen Sie niemals Datenbank-Verbindungszeichenfolgen, Autorisierungsheader, Zugriffscodes, Wiederherstellungsgeheimnisse, Signaturschlüsselinhalt oder entschlüsseltes Paketmaterial ein.

## Bei unbekannter Routenhoheit

Priorisieren Sie vorherigen Routensnapshot, Anwendungsergebnis, Rollback-Ergebnis, aktuelle Nginx-Proxy-Manager-Ziele und öffentliche Bereitschaftsprüfungen. Raten Sie nicht anhand eines geladenen Browser-Tabs.

## Nachweise aufbewahren

Bewahren Sie Abschlussbericht und Nachweis der ersten nativen Sicherung mit Release- und Änderungsunterlagen auf. Eine archivierte Sitzung bleibt der dauerhafte Zielnachweis.

Verwandt: [Rollback-Grenzen](rollback.md) und [Bereinigung](cleanup.md).
