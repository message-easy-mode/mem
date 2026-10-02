---
title: Installieren oder migrieren?
description: Wählen Sie den sicheren Weg für einen neuen Host, ein teilweise vorbereitetes Ziel oder eine unterstützte MEM-0.1.0-Quelle.
section: Erste Schritte
order: 40
---

# Installieren oder migrieren?

Wählen Sie den Installationsweg, bevor MEM Änderungen am Host vornimmt. Eine Neuinstallation und eine Migration sind unterschiedliche Workflows mit unterschiedlichen Sicherheitsgrenzen.

## Neuinstallation

Verwenden Sie den normalen Einrichtungsablauf auf einem sauberen oder bewusst vorbereiteten Ziel ohne ältere MEM-0.1.0-Anwendung.

Der aktuelle Ablauf bestimmt Installations- oder Reparaturmodus, prüft Host und Docker, bewertet Speicher und Ports, konfiguriert Domain und Zertifikat, zeigt geplante Ressourcen, erstellt oder prüft MEM-Netzwerk und dauerhaften Speicher, startet PostgreSQL und Nginx Proxy Manager, verifiziert das Ergebnis und übergibt an das Operator-Dashboard.

Die Control Plane ist bereits die private Anwendung, in der die Einrichtung läuft. Neue Pläne stellen keine separaten Anwendungscontainer `mem-api` und `mem-web` bereit.

## Vorhandenes MEM 0.1.0

Wenn MEM alte Container `mem-api` oder `mem-web` ohne aktuellen Installationsdatensatz erkennt, wird der Operator zu MEM Migrate geführt. Führen Sie keine Neuinstallation über den alten Server aus.

Der sichere Weg ist: sauberes Ziel vorbereiten, Zielanfrage erstellen, einen Quell-Stack bewerten und erfassen, verschlüsseltes Paket erstellen, importieren und validieren, Kandidaten konvertieren und privat bereitstellen, dann prüfen, übernehmen, verifizieren und abschließen.

Der aktuelle Adapter unterstützt definierte MEM-0.1.0-Quellen. Er ist kein universeller Importer für jeden manuell aufgebauten Synapse-Server.

## Unvollständige MEM-Ressourcen

Vorhandene MEM-eigene Dienste oder Stacks ohne abgeschlossenen Installationsdatensatz gelten als Reparatur- oder Prüfzustand und nicht automatisch als saubere Neuinstallation.

Prüfen Sie Ressourcen und Diagnosen. Löschen Sie keine Datenbank, kein Volume, Netzwerk oder keinen Container nur deshalb, weil die Ressource noch nicht korrekt in der Oberfläche erscheint.

## Recovery für verschlüsselte Nachrichten

Servermigration und Restore erhalten serverseitig gespeicherte verschlüsselte Ereignisse. Ende-zu-Ende-Schlüssel, die ausschließlich auf Benutzergeräten vorhanden waren, können sie nicht neu erzeugen.

Prüfen Sie vor Passwort-Resets, Gerätewechsel oder Migration, ob wichtige Benutzer einen Recovery Key oder ein Key-Backup und möglichst ein weiteres verifiziertes Gerät besitzen.

| Aktuelle Situation | Richtiger Start |
|---|---|
| Sauberer Zielhost | Normale MEM-Einrichtung |
| Aktuelle Installation mit ausgefallenem Dienst | Reparatur und Diagnose |
| Alte Quelle mit `mem-api` / `mem-web` | MEM Migrate |
| Beliebiger Nicht-MEM-Synapse-Server | Vom aktuellen Migrationsadapter nicht unterstützt |
