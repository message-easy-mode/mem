---
title: Nachweise, Logs und Supportberichte verwenden
description: Sammeln Sie redigierte dauerhafte Recovery-Nachweise ohne Dumps, Schlüssel, Zugangsdaten oder rohe Befehlsausgabe.
section: Sichern und wiederherstellen
order: 110
---

# Nachweise, Logs und Supportberichte verwenden

Der Wiederherstellungsarbeitsbereich trennt Fortschritt, sichere Nachweise, strukturierte Logs, Konfiguration und Supportübergabe.

## Aktivität

**Aktivität** zeigt Stufenwechsel, Vorgangs-IDs, Ereigniscodes, Zeitpunkte sowie angefordert, gestartet, abgeschlossen, fehlgeschlagen oder abgebrochen.

## Nachweise

**Nachweise** gruppiert kuratierte Ergebnisse für Sicherungsbereitschaft, privaten Test, Standard-Neuerstellung, öffentliche Prüfung und Übergabe. Hier finden Sie schnell den letzten Erfolg oder Fehler.

## Logs

Restore-Logs sind append-only strukturierte Ereignisse und können nach Schweregrad, Stufe und Text gefiltert werden. Ein sicheres Ereignis enthält Zeit, Restore- und Vorgangsidentität, Stufe, Schweregrad, stabilen Ereigniscode sowie redigierte Nachricht und freigegebene Details.

Logs dürfen keine Zugangsdaten, privaten Schlüssel, Signaturschlüssel, Tokens, Verbindungszeichenfolgen oder ungefilterte Befehlsausgabe enthalten. Die Redaktion ist eine letzte Schutzschicht.

## Konfiguration

**Konfiguration** zeigt sichere Quell-Snapshots, Zielwerte, Reservierungen und Vorgangsdaten. Damit lässt sich die Betreiberanforderung vom später beobachteten Laufzeitstatus trennen.

## Supportbericht erzeugen

Über **Supportbericht erzeugen** wird ein formatiertes JSON heruntergeladen mit:

- MEM-Version und Restore-ID;
- Versuchsstatus und letzter sicherer Fehlerzusammenfassung;
- Quellen- und Zielidentität;
- Log-Zählungen und aktuellen redigierten Ereignissen;
- Vorgangszusammenfassungen;
- Warnungen.

Datenbankdumps, Konfigurationsdateien, Zugangsdaten, private Schlüssel und ungefilterte Logs werden bewusst ausgeschlossen.

## Vor dem Teilen

Auch einen redigierten Bericht prüfen. Hostnamen, Stack-Namen, Zeitpunkte, Ereigniscodes, Benutzerzahlen und Topologie können sensibel sein.

Portable ZIPs, PostgreSQL-Dumps, Signaturschlüssel, `homeserver.yaml`, rohe Containerlogs, Zugriffstokens und TOTP-Wiederherstellungsmaterial gehören nicht in normale Supportanfragen.

Ist die Control Plane nicht verfügbar, lokale Logs und Docker-Nachweise über den dokumentierten Diagnose-Notfallweg sichern und mit der Restore-ID korrelieren, statt einen neuen Versuch zu erzeugen.
