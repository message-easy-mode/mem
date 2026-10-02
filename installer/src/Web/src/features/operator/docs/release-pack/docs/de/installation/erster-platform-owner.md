---
title: Ersten Platform Owner erstellen
description: Tauschen Sie die Setup-Code-Berechtigung vor der authentifizierten Ersteinrichtung gegen ein benanntes Owner-Konto mit TOTP und Recovery-Codes.
section: Installation
order: 35
---

# Ersten Platform Owner erstellen

Der erste Platform Owner ersetzt die vorübergehende Setup-Code-Berechtigung durch ein benanntes Operator-Konto **bevor die verwaltete Plattform eingerichtet wird**.

Führen Sie diesen Ablauf nur über die private Control-Plane-Verbindung aus.

## First-Owner-Setup öffnen

Wenn noch kein abgeschlossener, aktiver Platform Owner existiert, führt MEM zur Route `/bootstrap`.

Geben Sie dort den vom Host-Bootstrap erzeugten `mem_...`-Code ein. Der Server tauscht ihn gegen einen kurzlebigen, begrenzten Bootstrap-Grant. Dieser Grant ist keine normale Operator-Sitzung.

## Benannten Owner erstellen

Vergeben Sie einen eindeutigen Benutzernamen, optional eine gültige E-Mail-Adresse und ein starkes Passwort. Das Konto bleibt im Bootstrap-Zustand, bis TOTP erfolgreich geprüft wurde.

## TOTP einrichten und Recovery-Codes speichern

Scannen Sie den QR-Code oder tragen Sie das Secret in eine vertrauenswürdige Authenticator-App ein und bestätigen Sie den aktuellen sechsstelligen Code.

Speichern Sie die danach angezeigten Recovery-Codes offline oder in einem vertrauenswürdigen Passwortmanager. Sie gehören nicht in Tickets, Screenshots, Dokumentation oder Chat.

## Nach dem Bootstrap

- Der Platform Owner ist angemeldet und die authentifizierte Ersteinrichtung kann beginnen.
- `/bootstrap` eröffnet keinen weiteren First-Owner-Ablauf.
- Der Setup-Code verliert seine First-Owner-Funktion.
- Normale Rollen-, MFA- und Step-up-Regeln gelten.
- Weitere benannte Operatoren können später eingerichtet werden.
