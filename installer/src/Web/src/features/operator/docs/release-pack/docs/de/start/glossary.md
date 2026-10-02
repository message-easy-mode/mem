---
title: MEM-Glossar
description: Lernen Sie die in MEM verwendeten Begriffe zu Produkt, Matrix, Wiederherstellung, Migration, Sicherheit und Diagnose.
section: Erste Schritte
order: 70
---

# MEM-Glossar

## Produkt- und Operatorbegriffe

**MEM** — Message Easy Mode, das Projekt und die Produktfamilie.

**MEM Control Plane** — die private Web- und API-Anwendung für Operatoridentität, Workflow-Zustand, Orchestrierung, Diagnose und Zugriff auf MEM-verwaltete Docker- und Host-Ressourcen.

**Operator** — eine benannte Person mit Berechtigung zur Nutzung der Control Plane. Fähigkeiten hängen von Rollen und Authentifizierungszustand ab.

**Step-up-Verifizierung** — erneute Identitätsprüfung vor ausgewählten risikoreichen Aktionen.

**Stack / Chatserver** — eine unabhängig verwaltete Matrix-Umgebung, normalerweise mit eigenem Synapse, Element, Datenbank, Identität, Medien, Konfiguration und öffentlichen Hostnamen.

## Matrix-Laufzeitbegriffe

**Matrix** — das offene Protokoll, das MEM 0.2.0 für Messaging und Föderation verwendet.

**Synapse** — die Matrix-Homeserver-Implementierung eines Stacks.

**Element** — der primäre Matrix-Client als Web-Client des Stacks.

**Föderation** — Server-zu-Server-Kommunikation zwischen unabhängigen Matrix-Homeservern.

**TURN / coturn** — TURN leitet Anrufmedien weiter, wenn direkte Verbindungen nicht funktionieren; coturn ist die gemeinsame Serverimplementierung.

**Nginx Proxy Manager / NPM** — die unterstützte Ingress-Komponente für öffentliche HTTPS-Hostnamen, Dienste und Zertifikate.

## Wiederherstellungsbegriffe

**Backup-Katalogeintrag** — die dauerhafte Inventaridentität einer Wiederherstellungsquelle einschließlich Herkunft, Payload-Zustand und Integrität.

**Portables Backup** — ein exportierbares Archiv für Übertragung und Import.

**Restore-Versuch** — der dauerhafte Workflow-Datensatz eines Wiederherstellungsvorgangs.

**Restore Workspace** — Oberfläche und Serverzustand für Phasen, Logs, Nachweise, privaten Test, Neuerstellung und Abschluss.

**Privater Test** — eine Wiederherstellungs- oder Migrationskandidatenumgebung ohne Übernahme der öffentlichen Produktionsrouten.

**Standard Recreate** — normaler Ablauf zur Neuerstellung eines Stacks aus einem validierten Backup-Katalogeintrag.

**Target Claim** — dauerhafte Reservierung gegen konkurrierende Restore-Versuche mit derselben Identität oder denselben Hostnamen.

## Migration und Diagnose

**MEM Migrate** — das separate Produkt mit quellversionsspezifischem Migrationswissen.

**Source Assistant** — die temporäre lokale Web-Anwendung auf dem alten Server.

**Zielanfrage** — die vom Ziel erzeugte Anfrage mit Zielidentität und Verschlüsselungsempfänger.

**Migrationspaket** — das verschlüsselte portable Paket von Quelle zu Ziel.

**Kandidatenartefakt** — eine validierte Konvertierungsausgabe für private Bereitstellung.

**Produktionsübernahme** — kontrollierter Vorgang, der einem verifizierten Kandidaten Produktionsidentität und Routen gibt.

**Vorfall** — ein für Operatoren relevanter Fehlerkontext mit stabiler ID und sicheren korrelierten Nachweisen.

**Sicheres Diagnoseereignis** — ein begrenztes, redigiertes Ereignis für Browser-APIs und Supportberichte.

**Technisches Log** — umfangreichere Serilog-Ausgabe für Konsole, dauerhaftes CLEF und optionales Seq; nicht automatisch browser-sicher.

**Supportbericht** — begrenztes JSON mit sicheren Vorfall-, Betriebs-, Soll-/Ist- und optionalen Docker-Nachweisen.

**Laufzeitabgleich** — Vergleich und Reparatur des erwarteten MEM-Zustands gegenüber Dateisystem- und Docker-Zustand.
