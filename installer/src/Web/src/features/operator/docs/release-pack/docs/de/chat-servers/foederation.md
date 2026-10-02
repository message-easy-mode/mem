---
title: Föderation verwalten
description: Prüfen und übernehmen Sie öffentliche, eingeschränkte oder lokale Matrix-Föderation mit geschütztem Neustart und Rollback.
section: Chatserver betreiben
order: 70
---

# Föderation verwalten

## Ergebnis

Wählen Sie, wie der Stack Matrix-Föderationsverkehr mit anderen Homeservern austauscht, und übernehmen Sie die Änderung über einen geprüften, journalisierten Vorgang.

Öffnen Sie im Stack-Arbeitsbereich **Föderation**.

## Unterstützte Modi

**Öffentliche Föderation** erlaubt normale Kommunikation mit gültigen Matrix-Homeservern über die öffentliche Matrix-Route.

**Eingeschränkte Föderation** verwendet die exakte Domain-Allowlist von Synapse. Geben Sie pro Zeile eine Homeserver-Domain ein. Wildcards, URLs, IP-Adressen, Pfade, Ports und Duplikate werden abgelehnt. Dies ist eine Allowlist und kein eigener Föderations-Gateway.

**Nur lokale Föderation** verwendet eine leere Synapse-Allowlist und blockiert Discovery-, Föderations- und Signing-Key-Pfade an der kanonischen NPM-Route. Matrix-Client-Zugriff bleibt öffentlich, sofern Sie ihn nicht zusätzlich per Netzwerk oder VPN einschränken.

Der lokale Modus löscht keine vorhandenen Benutzer, Räume, Nachrichten, Medien, Signaturschlüssel oder historischen entfernten Raumzustände.

## Vor Übernahme prüfen

Der Editor beobachtet aktive Synapse-Konfiguration, Matrix-Laufzeit und kanonische NPM-Route. Benutzerdefinierte, mehrdeutige, unvollständige oder nicht unterstützte Zustände können Verwaltung deaktivieren.

Wählen Sie **Änderung prüfen**. Die Prüfung zeigt:

- aktuellen und vorgeschlagenen Modus;
- hinzugefügte und entfernte Domains;
- erforderlichen Matrix-Neustart;
- NPM-Ingress-Änderung;
- Auswirkungen auf föderierte Räume;
- automatisches Rollback.

Eine relevante Stack-Änderung macht die Prüfung ungültig. Verwenden Sie keine alte Prüfung erneut.

## Richtlinie übernehmen

Bestätigen Sie die exakt geprüfte Änderung und führen Sie bei Aufforderung aktuelle Step-up-Prüfung aus.

MEM journalisiert den Vorgang, prüft den Kandidaten mit dem aktiven Synapse-Abbild, ersetzt Konfiguration atomar, führt den kontrollierten Matrix-Neustart aus, ändert bei Bedarf NPM-Ingress und prüft Client- und Föderationsverhalten.

Clients können während des Neustarts kurz neu verbinden. Entfernte Homeserver können keine neuen Ereignisse mehr austauschen; vorhandener Verlauf bleibt erhalten.

## Fehler und Rollback

Scheitert Neustart oder Prüfung nach einer Änderung, versucht MEM den exakten vorherigen Synapse- und Ingress-Zustand wiederherzustellen. Bei Browsertrennung senden Sie keine zweite Änderung, solange der dauerhafte Vorgang läuft.

Verwenden Sie Vorgangs-ID und beobachteten Endzustand für die Entscheidung über einen neuen Versuch. Ein ungeklärter Zustand benötigt technische Wiederherstellung.
