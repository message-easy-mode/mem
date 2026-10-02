---
title: Chatserver-Fehler beheben
description: Diagnostizieren Sie Erstellungs-, Bereitschafts-, Benutzer-, Routen-, TURN-, Föderations- und Speicherprobleme, ohne Erfolg anzunehmen.
section: Chatserver betreiben
order: 110
---

# Chatserver-Fehler beheben

## Mit aktuellen Nachweisen beginnen

Öffnen Sie den Stack-Arbeitsbereich, wählen Sie **Aktualisieren** und führen Sie **Diagnose** aus. Erfassen Sie Slug, Prüfzeit, Vorgangs-ID, Berichts-ID, fehlgeschlagene Prüfcodes und redigierte Details.

Diagnostizieren Sie nicht nur anhand vorhandener Container oder eines alten Prüfzeitpunkts.

## Erstellung fehlgeschlagen

Der Dialog meldet Fehler ausdrücklich und nimmt keinen Erfolg an. Vor erneutem Versuch:

1. zur Chatserver-Liste zurückkehren und aktualisieren;
2. prüfen, ob Stack, Container, Routen, Datenbank oder Vorgang erfasst wurden;
3. fehlgeschlagenen Vorgang und Diagnose prüfen;
4. Domain-, Zertifikats-, Abbild-, Docker-, PostgreSQL-, Speicher- oder Bereitschaftsproblem beheben;
5. dieselbe beabsichtigte Anfrage nur bei klarem Besitzstand erneut ausführen.

Erstellen Sie keinen zweiten Slug, um eine teilweise erstellte produktive Identität zu verstecken.

## Synapse-Konfiguration oder Produktionsspeicher schlägt fehl

Bei einer offiziellen Produktionsinstallation liegen neue Stack-Ressourcen unter:

```text
/var/lib/message-easy-mode/instances
```

Control Plane und Host-Docker-Daemon müssen über diesen kanonischen Host-Datenvertrag dieselben physischen Dateien adressieren. Leiten Sie Produktions-Instanzdaten nicht auf `/home/<user>/mem-data` um, um einen Fehler zu umgehen.

Schlägt die Erstellung bei der Synapse-Konfigurationsgenerierung fehl, bewahren Sie Vorgang und Supportbericht auf. Prüfen Sie, ob der Vorfall `generate-synapse-config` nennt, verschieben Sie generierte Konfigurationsdateien aber nicht manuell. Verwenden Sie den unterstützten MEM-Recovery-/Korrekturpfad.

## NPM-Routenveröffentlichung scheitert bei `npm:81`

In containerisierter Produktion kommunizieren Control Plane und Nginx Proxy Manager über das verwaltete Docker-Netzwerk `mem-gateway`. Ein Fehler wie `Name or service not known (npm:81)` bedeutet, dass die Control Plane die verwaltete NPM-Adresse aktuell nicht auflösen kann.

Sammeln Sie Vorfall/Supportbericht und prüfen Sie, ob die Control Plane über das aktuelle unterstützte Bootstrap gestartet oder neu erstellt wurde. Ein manuelles `docker network connect` kann für begrenzte technische Diagnose nützlich sein, ist aber kein normaler Operator-Reparaturweg und ersetzt nicht den unterstützten Runtime-/Recreate-Pfad.

## Matrix oder Element nicht öffentlich erreichbar

Prüfen Sie unter **Netzwerk & Domains** Hosts, Routen-IDs, Zertifikats-IDs und interne Zustellung. Diagnose unterscheidet internes HTTP, NPM, Route und öffentliches HTTPS.

Die Seite prüft weder autoritatives DNS noch Zertifikatsablauf. Prüfen Sie dies separat, wenn öffentliches HTTPS scheitert, intern aber alles funktioniert.

## Benutzererstellung deaktiviert

Synchronisieren Sie das Synapse-Benutzerinventar. Beheben Sie Inventarfehler, bevor Sie den Homeserver als leer behandeln. Der erste Administrator erscheint nur, wenn Synapse verbindlich keinen aktiven lokalen Admin meldet.

Für Passwortzurücksetzung prüfen Sie Matrix-Administratorautorität und Control-Plane-Step-up. Ersetzen Sie abgelehnte oder abgelaufene Autorität.

## Sprache oder Video unzuverlässig

Prüfen Sie unter **Sprache & Video** Stack-Verbindung und Plattformbereitschaft. Eine verbundene Synapse-Konfiguration garantiert keinen zuverlässigen Relay-Verkehr, wenn coturn oder Relay-Ports nicht bereit sind.

Überschreiben Sie externe oder driftende TURN-Einstellungen nicht manuell. Verwenden Sie den geprüften Vorgang oder sammeln Sie technische Wiederherstellungsnachweise.

## Föderationsänderungen deaktiviert

MEM schreibt benutzerdefinierte, mehrdeutige, unvollständige oder nicht unterstützte Zustände nicht automatisch um. Prüfen Sie Synapse-Laufzeit und kanonisches NPM-Ingress-Problem. Stellen Sie einen unterstützten Zustand her.

Senden Sie nach Browsertrennung keine zweite Änderung, solange ein dauerhafter Vorgang läuft.

## Speicherdetails nicht verfügbar

Bestätigen Sie, dass Stack-Manifest und Matrix-Laufzeitidentität lesbar sind. Fehlende Speichernachweise bedeuten nicht, dass Dateien fehlen. Prüfen Sie den Host nur über genehmigte Ausfall- oder Supportverfahren.

## Eskalationsnachweise

Bewahren Sie redigiert auf:

- Stack-Slug und öffentliche Hosts;
- letzten Prüf- und Diagnosezeitpunkt;
- Vorgangs- und Berichtskennungen;
- fehlgeschlagene Prüf- oder stabile Problemcodes;
- Abbildnamen und Versionen;
- Routen- und Zertifikatskennungen;
- TURN- oder Föderationszustand und Konfigurationshashes;
- relevanten Containerzustand und begrenzte Logs.

Nie enthalten: Passwörter, Matrix-Zugriffstoken, TOTP-Geheimnisse, Wiederherstellungscodes, TURN-Geheimnisse, Signaturschlüsselinhalte oder unredigierte Konfigurationsdateien.

Für Plattform-Installationsfehler siehe [Fehlerbehebung bei der Installation](../installation/installation-fehlerbehebung.md). Für Capability-Grenzen siehe [Bekannte Einschränkungen](../start/known-limitations.md).
