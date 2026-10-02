---
title: Matrix-Benutzer verwalten
description: Synchronisieren Sie das verbindliche Synapse-Inventar und erstellen Sie den ersten Administrator oder weitere Matrix-Benutzer.
section: Chatserver betreiben
order: 30
---

# Matrix-Benutzer verwalten

## Ergebnis

Synchronisieren Sie das lokale Synapse-Kontoinventar, erstellen Sie den ersten Matrix-Administrator und fügen Sie weitere Matrix-Benutzer hinzu.

Öffnen Sie im Stack-Arbeitsbereich **Benutzer**.

## Matrix-Benutzer und Operatoren sind getrennt

MEM-Control-Plane-Benutzer verwalten die Plattform. Matrix-Benutzer gehören zu einem Homeserver und melden sich über Element oder einen anderen Matrix-Client an.

Ein Platform-Owner-Konto erstellt nicht automatisch ein Matrix-Konto. Ein Matrix-Administrator ist nicht automatisch MEM-Operator.

## Vor Änderungen synchronisieren

MEM liest das lokale Kontoinventar aus der Synapse-Datenbank des Stacks. Dieses Inventar entscheidet verbindlich, ob ein aktiver Administrator vorhanden und Benutzererstellung sicher ist.

Benutzererstellung bleibt deaktiviert, solange das Inventar nicht verfügbar, nicht synchronisiert, in Bearbeitung oder fehlgeschlagen ist. Wählen Sie **Benutzer synchronisieren** und warten Sie auf Erfolg, bevor Sie eine leere Tabelle als leeren Homeserver interpretieren.

Die Tabelle kann enthalten:

- über MEM erstellte Konten;
- direkt aus Synapse gefundene Konten;
- aktive, deaktivierte, ausstehende, fehlgeschlagene oder fehlende Projektionen;
- Administrator- und Erster-Administrator-Markierungen.

## Ersten Matrix-Administrator erstellen

Meldet Synapse keinen aktiven lokalen Administrator, zeigt MEM **Ersten Matrix-Administrator erstellen**. Das erste Konto wird zwingend Administrator und über den lokalen Shared-Secret-Registrierungsweg des Stacks erstellt.

Wählen Sie einen dauerhaften Benutzernamen und ein starkes Passwort. Speichern Sie das Passwort sicher, melden Sie sich anschließend in Element an und richten Sie das Matrix-Wiederherstellungsmaterial für Verschlüsselung ein.

## Weitere Benutzer erstellen

Nachdem das Inventar mindestens einen aktiven Administrator bestätigt, verwenden Sie **Matrix-Benutzer erstellen**.

Das Formular akzeptiert:

- Benutzername;
- Passwort mit mindestens acht Zeichen;
- optionaler Anzeigename;
- optionale E-Mail-Adresse;
- optionale Matrix-Administratorrolle.

Benutzernamen werden in Kleinbuchstaben umgewandelt und behalten Buchstaben, Zahlen, `.`, `_`, `-` und `=`. Prüfen Sie den normalisierten Namen. Die Matrix-ID verwendet den Servernamen des Stacks, zum Beispiel `@alice:matrix-familie.example.org`.

## Erfolg prüfen

Nach der Erstellung:

1. prüfen Sie den Status Aktiv oder Synchronisiert;
2. prüfen Sie Matrix-Benutzer-ID und Rolle;
3. melden Sie sich über die Element-URL des Stacks an;
4. richten Sie für verschlüsselte Nutzung Wiederherstellung und ein verifiziertes weiteres Gerät ein.

Ein erfolgreich erstelltes Konto kann trotz anschließender Inventarwarnung bereits in Matrix existieren. Synchronisieren Sie erneut, bevor Sie ein Duplikat anlegen.
