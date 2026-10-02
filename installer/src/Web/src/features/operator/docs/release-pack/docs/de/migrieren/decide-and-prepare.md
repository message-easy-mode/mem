---
title: Migration entscheiden und sicher vorbereiten
description: Prüfen Sie die unterstützte Quelle, schützen Sie die Verschlüsselungswiederherstellung der Benutzer und bereiten Sie Quelle und Ziel vor.
section: Von MEM 0.1.0 migrieren
order: 10
---

# Migration entscheiden und sicher vorbereiten

Verwenden Sie die Migration, wenn die Quelle eine unterstützte MEM-0.1.0-Installation und das Ziel eine separate MEM-0.2.0-Control-Plane ist. Die Migration ist kein universeller Synapse-Importer und nicht der Wiederherstellungspfad für eine native MEM-Sicherung.

## Ergebnis

Quelle und Ziel sind bestätigt, die Benutzer haben ihre Verschlüsselungswiederherstellung geschützt und der Betreiber verfügt über die erforderlichen Zugriffs-, Speicher-, DNS-, Zertifikats- und Wartungsinformationen.

## Bevor Sie beginnen

Bestätigen Sie:

- Die Quelle entspricht dem von MEM Migrate erkannten älteren MEM-0.1.0-Profil.
- Der Docker-Daemon der Quelle ist erreichbar und die alten Container und Daten sind vorhanden.
- Eine separate MEM-0.2.0-Control-Plane ist installiert und betriebsbereit.
- Sie können sich als berechtigter Zielbetreiber anmelden und Step-up-Authentifizierung abschließen.
- Das Ziel hat genug freien Speicher für verschlüsseltes Paket, entschlüsseltes Arbeitsmaterial, Konvertierung, Staging und endgültige Laufzeit.
- Sie kontrollieren die bestehenden öffentlichen Matrix- und Element-Hostnamen, DNS-Einträge und Nginx-Proxy-Manager-Routen.
- Auf dem Ziel ist bereits ein geeignetes aktives Zertifikat für die öffentlichen Hostnamen vorhanden.
- Ein Wartungsfenster für Umschaltung und Produktionsprüfungen ist festgelegt.

Wenn der Source Assistant die Quelle als nicht unterstützt oder blockiert einstuft, stoppen Sie. Erzwingen Sie den Paketablauf nicht und kopieren Sie Quelldateien nicht manuell in die Ziel-Laufzeit.

## Wiederherstellung verschlüsselter Nachrichten schützen

Bitten Sie betroffene Benutzer vor der Erfassung, sich nicht aus funktionierenden Element-Sitzungen abzumelden. Jeder Benutzer sollte mindestens einen verlässlichen Wiederherstellungsweg prüfen:

- ein anderes vertrauenswürdiges und verifiziertes Gerät;
- funktionierendes Secure Backup samt Wiederherstellungsgeheimnis;
- sicher gespeicherte exportierte Raumschlüssel.

Eine Servermigration kann serverseitige Räume, Ereignisse, Konten, Medien, Konfiguration und Matrix-Identität bewahren. Nicht gesicherte Ende-zu-Ende-Verschlüsselungsschlüssel kann sie nicht neu erzeugen.

## Einen Stack planen

Der Source Assistant kann mehrere Stacks erkennen, doch jedes Paket enthält genau einen ausgewählten Stack. Notieren Sie:

- beabsichtigten Quell-Stack-Slug;
- unverändert zu erhaltenden Matrix-Hostnamen;
- bestehenden Element-Hostnamen;
- aktuellen Eigentümer der öffentlichen Routen;
- Quell- und Zielhost;
- verantwortlichen Entscheider für die Umschaltung.

Starten Sie keine parallelen Migrationen für dieselbe öffentliche Identität.

## Quelle bewahren

Erstellen Sie bei Bedarf eine Host-Sicherheitskopie nach Ihrem bestehenden Verfahren, stellen Sie diese MEM 0.2.0 jedoch nicht als native Sicherungskatalog-Quelle dar. Halten Sie den alten Host intakt und erreichbar. Der Zielablauf löscht ihn nicht.

## Bereitschaft prüfen

Sie können fortfahren, wenn Quelle und Ziel getrennt sind, der Stack eindeutig ist, Benutzer informiert wurden, DNS- und Zertifikatshoheit klar sind, die Nutzung des alten Servers zur Liveschaltung gestoppt werden kann und Sie [Rollback-Grenzen](rollback.md) gelesen haben.

Weiter: [Source Assistant installieren und privat öffnen](install-source-assistant.md).
