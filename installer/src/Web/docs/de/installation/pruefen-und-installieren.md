---
id: "de/installation/review-and-install"
translationKey: "installation/review-and-install"
locale: "de"
groupId: "installation-de"
groupKey: "installation"
groupLabel: "Installation"
groupOrder: 10
title: "Installationsplan prüfen und ausführen"
description: "Frieren Sie den exakten Review-Plan ein und starten Sie danach bewusst die dauerhafte Plattforminstallation."
order: 60
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Installationsplan", "Review", "Postgres", "NPM", "Diagnosen"]
route: "/docs/de/installation/pruefen-und-installieren"
aliases: []
outputPath: "docs/de/installation/pruefen-und-installieren.md"
preserveLegacyBranding: false
---
# Installationsplan prüfen und ausführen

Review und Installation sind bewusst getrennte Stufen.

## Review vor externer Mutation

Review zeigt den exakten kanonischen Plattformplan mit Domain, Wildcard-Zertifikatsabsicht, Docker-Netzwerk, PostgreSQL, Nginx Proxy Manager, Ports, Support-Werkzeugen und den Aktionen nach Installationsstart.

Geschützte DNS-, PostgreSQL- und NPM-Zugangsdaten gehören nicht in das eingefrorene Review-JSON. Das Speichern des NPM-Administratorpassworts verändert daher den Plan-Fingerprint nicht.

Das Akzeptieren von Review:

- friert Plan und Fingerprint ein;
- dokumentiert die geprüfte Operatorabsicht;
- verändert **noch nicht** Docker, DNS, ACME, Zertifikatsspeicher oder NPM.

## Plattform installieren

Auf der nächsten Seite überschreitet **Plattform installieren** ausdrücklich die externe Mutationsgrenze.

Danach kann der dauerhafte serverseitige Worker unter anderem:

- `mem-gateway` und dauerhafte Volumes erstellen oder prüfen;
- `mem-postgres` starten und Bereitschaft prüfen;
- `mem-npm` starten;
- bei frischem NPM den ersten Administrator sicher initialisieren;
- die DNS-01-Challenge erstellen und das geprüfte Wildcard-Zertifikat anfordern;
- Zertifikat speichern, prüfen und in NPM importieren;
- ausgewählte Support-Werkzeuge starten;
- die Plattform verifizieren;
- jeden Schritt und Versuch dauerhaft protokollieren.

## Warten, Fehler und Retry

Ein Schritt kann `WaitingForUser` melden. Folgen Sie der angezeigten Aktion und setzen Sie dieselbe Installation fort.

Bei einem Fehler lesen Sie die sichere Fehlerursache, verwenden Sie bei Bedarf Diagnostics oder den begrenzten Supportbericht, beheben Sie die konkrete Ursache und verwenden Sie Retry nur, wenn MEM dies erlaubt.

Bereits abgeschlossene Schritte bleiben autoritativ. Löschen Sie nicht wiederholt Container, Volumes, Installationsdatensätze oder Zertifikatszustand.

Seq bleibt optional; Diagnostics muss auch ohne Seq funktionieren.
