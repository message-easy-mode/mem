---
title: Sicherheits- und Zugriffseinstellungen
description: Betreiben Sie die private MEM-0.2.x-Control-Plane mit benannten Konten, Passwörtern, MFA, Wiederherstellung, Rollen und Step-up-Richtlinie für Hochrisikoaktionen.
section: Operations (Deutsch)
order: 40
---

# Sicherheits- und Zugriffseinstellungen

Die MEM Control Plane ist eine privilegierte Verwaltungsoberfläche mit Docker-Autorität. Netzwerkprivatheit, benannte Anmeldung, MFA, Rollen und serverseitige Autorisierung wirken zusammen; keine dieser Grenzen ersetzt die anderen.

## Verwaltung privat halten

Verwenden Sie eine ausdrücklich ausgewählte Trusted-LAN-Adresse, VPN oder SSH/local-only-Weiterleitung. Veröffentlichen Sie die Control Plane nicht über NPM, öffentliches DNS oder Internet-NAT.

## Benannte Operatoridentität

Platform Owner und andere MEM-Operatoren verwenden Control-Plane-Konten. Diese sind getrennt von Matrix-Benutzern, die sich in Element anmelden.

## MFA und Recovery

Der erste Platform Owner richtet TOTP-MFA ein und erhält Recovery-Codes. Bewahren Sie Recovery-Codes offline auf und behandeln Sie sie wie Zugangsdaten.

## Operator-Passwörter und Wiederherstellung bei vergessenem Passwort

Verwenden Sie **Passwort ändern** im Kontomenü oder in der aktuellen Zeile unter **Operator-Zugriff**, solange Sie Ihr Passwort kennen. MEM verlangt eine frische Bestätigung mit aktuellem Passwort und Authenticator und widerruft nach der Änderung bestehende Browser- und CLI-/Gerätesitzungen.

Wenn ein Platform Owner sein Passwort vergessen hat, verwenden Sie den hostautoritativen Konsolenbefehl anstelle eines Browser-Reset-Ablaufs. Siehe [Operator-Passwort ändern oder wiederherstellen](operator-passwort-aendern-und-wiederherstellen.md) für den genauen Befehl, die erhaltenen Kontodaten und die aktuelle Wiederherstellungsgrenze.

## Step-up für Hochrisikoaktionen

MEM kann für destruktive oder hochriskante Browseraktionen eine frische Identitätsprüfung verlangen. Das konfigurierte Wiederverwendungsfenster bestimmt, wie lange eine erfolgreiche Prüfung in derselben serververwalteten Sitzung gültig bleibt.

Das Abschalten der zusätzlichen Hochrisiko-Step-up-Prüfung deaktiviert weder Anmeldung noch TOTP-MFA, Rollen, serverseitige Autorisierung oder Audit-Nachweise. Die Änderung dieser Policy bleibt selbst eine sensible Aktion.

## Sitzungs- und Rollenänderungen

Kennwort-, MFA-, Rollen-, Kontostatus-, Sitzungs- und Sicherheitsrichtlinienänderungen sollen Autorität gemäß Serververtrag invalidieren. Verlassen Sie sich nicht auf browserlokale Flags, um privilegierte Autorität zu verlängern.

## CLI

Die CLI verwendet benannte Geräteautorität und, wo unterstützt, OS Secret Service. Sie darf kein Kommandozeilen-Bypass für Browser-Step-up werden und keine wiederverwendbaren Geheimnisse akzeptieren, nur um Hochrisikoaktionen zu vereinfachen.
