---
title: Operator-Passwort ändern oder wiederherstellen
description: Ändern Sie ein bekanntes MEM-Operator-Passwort oder stellen Sie ein vergessenes Platform-Owner-Passwort über die Host-Konsole wieder her.
section: Operations (Deutsch)
order: 45
---

# Operator-Passwort ändern oder wiederherstellen

MEM trennt die normale selbstständige Passwortänderung von der hostautoritativen Wiederherstellung. Verwenden Sie den Browser, solange Sie Ihr aktuelles Passwort kennen. Verwenden Sie die Host-Konsole nur, wenn ein Platform Owner sein Passwort vergessen hat und den normalen Änderungsablauf nicht abschließen kann.

Wenn Sie aus der Control Plane ausgesperrt sind und die integrierte Seite **Dokumentation** nicht öffnen können, rufen Sie die öffentliche Dokumentation auf **messageeasymode.com** auf und suchen Sie nach **Operator-Passwort ändern oder wiederherstellen**. Die öffentliche Dokumentation wird aus derselben MEM-Dokumentationsquelle synchronisiert.

## Passwort im angemeldeten Zustand ändern

Für Ihr eigenes benanntes Operatorkonto:

1. Öffnen Sie das Kontomenü oder **Operator-Zugriff**.
2. Wählen Sie für den aktuellen Operator **Passwort ändern**.
3. Bestätigen Sie Ihre Identität mit Ihrem **aktuellen Passwort** und einem aktuellen **Authenticator-App-Code**.
4. Geben Sie das neue Passwort zweimal ein. Verwenden Sie bei Bedarf **Passwort anzeigen**, um komplexe Zeichen vor dem Absenden zu prüfen.
5. Wählen Sie **Passwort ändern**.

Das neue Passwort muss die von MEM angezeigte Passwortrichtlinie erfüllen. In MEM 0.2.x verlangt das Formular mindestens 14 Zeichen mit Groß- und Kleinbuchstaben, einer Zahl, einem Sonderzeichen und mindestens vier unterschiedlichen Zeichen.

Nach einer erfolgreichen Änderung:

- macht MEM bestehende Browser- und CLI-/Gerätesitzungen dieses Operators ungültig;
- kehrt der aktuelle Browser zur Anmeldung zurück;
- wird das alte Passwort abgelehnt;
- bleibt die bestehende TOTP-Authenticator-Konfiguration erhalten;
- bleiben ungenutzte Wiederherstellungscodes erhalten;
- bleiben Rollen und normaler Kontostatus erhalten.

Melden Sie sich anschließend mit dem neuen Passwort und dem bestehenden Authenticator erneut an.

Ein Wiederherstellungscode kann für diesen Vorgang das aktuelle Passwort nicht ersetzen und kann kein Step-up für Hochrisikoaktionen erfüllen.

## Vergessenes Platform-Owner-Passwort wiederherstellen

MEM 0.2.x stellt für ein vergessenes Operator-Passwort keinen E-Mail-Reset-Link, keine Sicherheitsfragen und keine Web-Schaltfläche bereit, mit der ein Administrator das Passwort eines anderen Operators zurücksetzt.

Wenn ein **Platform Owner** sein Passwort vergessen hat, verwenden Sie die hostautoritative Wiederherstellung über die Konsole des MEM-Servers.

Führen Sie auf dem MEM-Host aus:

```bash
sudo docker exec -it mem-control-plane \
  dotnet Api.dll operator reset-password PLATFORM_OWNER_USERNAME
```

Der Befehl ist interaktiv. Er fordert Sie auf, den ausgewählten Platform-Owner-Benutzernamen zur Bestätigung des Ziels einzugeben, und fragt das Ersatzpasswort anschließend zweimal ohne Terminal-Echo ab.

Geben Sie das neue Passwort **nicht** in der Befehlszeile, im Shell-Verlauf, als Skriptargument oder in einem Supportbericht an.

Bei Erfolg meldet der Befehl die Passwortzurücksetzung. Bestehende TOTP-Konfiguration, ungenutzte Wiederherstellungscodes, Rollen und Plattformdaten bleiben erhalten; vorhandene MEM-Sitzungen dieses Kontos werden widerrufen.

Danach:

1. Kehren Sie zur MEM-Anmeldeseite zurück.
2. Melden Sie sich mit dem Ersatzpasswort an.
3. Schließen Sie MFA mit dem bestehenden Authenticator-Code ab oder verwenden Sie, falls passend, in der MFA-Stufe einen vorhandenen Wiederherstellungscode.

## Aktuelle Wiederherstellungsgrenze

Der obige Host-Befehl ist in MEM 0.2.x bewusst ein **Platform-Owner-Wiederherstellungsbefehl**. Er ist kein allgemeiner Web- oder Konsolenmechanismus, mit dem ein Operator das Passwort eines anderen benannten Operators zurücksetzen kann.

Umgehen Sie diese Grenze nicht, indem Sie die MEM-SQLite-Datenbank bearbeiten, Passwort-Hashes manuell ersetzen oder Identity-Felder zwischen Konten kopieren.

## Wenn auch der Authenticator nicht verfügbar ist

Die Host-Passwortwiederherstellung behält die aktuelle MFA-Konfiguration bei. TOTP wird dadurch weder entfernt noch ersetzt.

Wenn das Passwort bekannt, der Authenticator aber nicht verfügbar ist, verwenden Sie in der MFA-Stufe einen ungenutzten MEM-Wiederherstellungscode. Wenn weder Passwortautorität noch MFA-Wiederherstellungsautorität verfügbar sind, stoppen Sie und verwenden Sie den unterstützten Wiederherstellungs-/Supportweg, statt Authentifizierungsdaten direkt zu verändern.

## Verwandte Hinweise

Siehe [Sicherheits- und Zugriffseinstellungen](sicherheits-und-zugriffseinstellungen.md) für das umfassendere Modell zu Authentifizierung, Rollen, Sitzungen und Step-up für Hochrisikoaktionen.
