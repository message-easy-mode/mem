---
id: "de/chat-servers/passwords-and-accounts"
translationKey: "chat-servers/passwords-and-accounts"
locale: "de"
groupId: "chat-servers-de"
groupKey: "chat-servers"
groupLabel: "Chatserver betreiben"
groupOrder: 15
title: "Passwörter und Kontolebenszyklus verwalten"
description: "Richten Sie Matrix-Admin-Autorität ein, setzen Sie Passwörter zurück und deaktivieren oder reaktivieren Sie Konten sicher."
order: 40
status: "supported"
appliesTo: ["0.2.x"]
tags: ["Passwort zurücksetzen", "deaktivieren", "reaktivieren", "Step-up", "E2EE"]
route: "/docs/de/chat-servers/passwords-and-accounts"
aliases: []
outputPath: "docs/de/chat-servers/passwoerter-und-konten.md"
preserveLegacyBranding: false
---
# Passwörter und Kontolebenszyklus verwalten

## Ergebnis

Verwenden Sie geschützte Server-Admin-Workflows, um ein Matrix-Passwort zurückzusetzen, ein Konto zu deaktivieren oder es mit neuem Passwort zu reaktivieren.

## Autorität für Passwortzurücksetzung einrichten

MEM benötigt geprüfte Autorität eines bestehenden aktiven Matrix-Serveradministrators.

Wählen Sie **Reset-Autorität setzen** oder **Autorität ersetzen**. Der normale Modus verwendet Matrix-Administrator-ID und Passwort. MEM verwendet das Passwort einmalig zur Beschaffung und Prüfung der Autorität und speichert es nicht. Ein Zugriffstoken steht als erweiterte Wiederherstellungsoption bereit und wird mit dem MEM-Data-Protection-Schlüsselring verschlüsselt.

Das Setzen oder Ersetzen der Autorität erfordert eine aktuelle Control-Plane-Step-up-Prüfung.

## Passwort zurücksetzen

Wählen Sie bei einem aktiven Matrix-Benutzer **Passwort zurücksetzen**, geben Sie das neue Passwort zweimal ein und führen Sie bei Aufforderung Step-up aus.

Synapse meldet den Benutzer beim Passwortwechsel von vorhandenen Matrix-Geräten ab. MEM kann das alte Passwort weder anzeigen noch wiederherstellen.

> [!WARNING]
> Eine Passwortzurücksetzung stellt keine Ende-zu-Ende-Verschlüsselungsschlüssel wieder her. Ohne verifiziertes Gerät und entsperrbare serverseitige Schlüsselsicherung kann der Zugriff auf alten verschlüsselten Verlauf verloren bleiben.

Gehört das geänderte Passwort zum Administrator der Reset-Autorität, verwirft MEM diese Autorität. Richten Sie sie vor dem nächsten Reset neu ein.

## Konto deaktivieren

Wählen Sie **Konto deaktivieren** und prüfen Sie die Folgen. Der aktuelle MEM-Workflow deaktiviert ohne Datenlöschanforderung.

Synapse entfernt Geräte, Verschlüsselungsschlüssel, Zugriffstoken, Raummitgliedschaften, Drittanbieter-IDs und Passwort. Bestehende Raumnachrichten bleiben im Verlauf. Der letzte aktive Matrix-Administrator kann nicht deaktiviert werden.

Deaktivierung ist eine Hochrisikoaktion und erfordert aktuelle Step-up-Prüfung.

## Konto reaktivieren

Ein deaktiviertes Konto kann mit neuem Passwort reaktiviert werden. Wählen Sie **Konto reaktivieren**, geben Sie das Passwort zweimal ein und führen Sie Step-up aus.

Die Reaktivierung stellt Kontozugriff her. Entfernte Geräte, Verschlüsselungsschlüssel, Raummitgliedschaften oder fehlendes Wiederherstellungsmaterial werden nicht rekonstruiert.

## Nachweise

Erfassen Sie betroffene Matrix-Benutzer-ID, Zeitpunkt, Operator und Vorgangs- oder Fehlerreferenz. Kopieren Sie keine Passwörter, Zugriffstoken, Wiederherstellungsschlüssel oder TOTP-Geheimnisse in Supportberichte.
