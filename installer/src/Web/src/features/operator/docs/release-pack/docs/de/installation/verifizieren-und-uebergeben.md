---
title: Plattform verifizieren und Übergabe abschließen
description: Prüfen Sie die Verifikationsnachweise, bestätigen Sie die dauerhafte Übergabe und wechseln Sie in den normalen Control-Plane-Betrieb.
section: Installation
order: 70
---

# Plattform verifizieren und Übergabe abschließen

Installationsabschluss und erfolgreiche Verifikation sind getrennte Tatsachen.

## Verifikationsbericht

Die Abschlussprüfung validiert den dauerhaften Installationslauf und erforderliche verwaltete Abhängigkeiten wie PostgreSQL, NPM, gemeinsamen Coturn sowie den ausgewählten Zertifikatszustand.

## Private Control-Plane-Grenze verifizieren

Prüfen Sie vor der Übergabe, dass dieselbe private Zugriffsart in folgenden Ansichten erscheint:

- Home → Host status;
- Diagnostics → Control Plane runtime;
- System Information.

Für SSH/local-only wird `127.0.0.1:<port>` erwartet. Für Trusted LAN wird genau die ausgewählte RFC1918-Adresse erwartet.

Bei einer gesunden Installation darf Diagnostics keinen `control_plane.exposure.unsupported`-Incident enthalten.

Akzeptieren Sie keine Übergabe, wenn MEM Wildcard-, öffentliche, mehrfache oder anderweitig nicht unterstützte Exposition meldet.

## Übergabe

Nach erfolgreicher Verifikation bestätigt **Einrichtung abschließen und Dashboard öffnen** den Wechsel in den normalen Operatorbetrieb. Matrix- und Element-Stack-Erstellung bleibt ein separater Workflow nach der Plattformübergabe. Eine erfolgreiche Plattforminstallation beweist noch nicht, dass der erste verwaltete Stack bereitgestellt werden kann.

## Empfohlene nächste Sicherheitsaktionen

- MFA-Anmeldung des Platform Owner erneut prüfen;
- Recovery-Codes sicher aufbewahren;
- privaten Verwaltungsmodus und die Adresse dokumentieren;
- SHA-256-Fingerprint des Control-Plane-Zertifikats dokumentieren;
- Control Plane aus öffentlichem DNS, NPM-Ingress und Internet-NAT heraushalten;
- SSH auch bei normalem Trusted-LAN-Zugriff als Break-Glass-Weg behalten;
- Diagnostics und Plattformzustand prüfen;
- unter **Services → Coturn** eine aktuelle erfolgreiche Funktionsprüfung verlangen, wenn TURN Teil der Bereitstellung ist;
- den ersten Matrix- und Element-Stack erstellen;
- Matrix und Element müssen **Healthy** erreichen;
- im Stack **Doctor** ausführen;
- die öffentlichen Matrix- und Element-Routen prüfen, bevor die Installation betrieblich als abgenommen gilt.
