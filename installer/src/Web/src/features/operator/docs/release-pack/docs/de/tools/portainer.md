---
title: Portainer für erweiterte Containerdiagnose verwenden
description: Sicher von interpretierten MEM-Vorfällen zur owner-beschränkten Portainer-Prüfung mit gepinnter Laufzeit und serverseitigen Links wechseln.
section: Tools
order: 120
---

# Portainer für erweiterte Containerdiagnose verwenden

MEM erklärt einen Fehler; Portainer bietet erweiterte Containerprüfung auf niedriger Ebene. Portainer ersetzt keine MEM-Vorfälle, Supportberichte oder Workflow-Anleitungen, und MEM wird dadurch nicht zu einer allgemeinen Docker-Verwaltungskonsole.

## Portainer aus MEM öffnen

Ein Platform Owner kann `/diagnostics/portainer` öffnen, wenn der Server eine autoritative private Portainer-URL und Umgebungs-ID besitzt. MEM kann anbieten:

- **Portainer öffnen**;
- **Lokale Umgebung öffnen**;
- **Container öffnen**.

Der Browser konstruiert diese Links nicht aus `localhost`, veröffentlichten Ports, Containernamen oder Browser-Origin.

Portainer verwaltet seine eigene Anmeldung. MEM speichert kein Portainer-Passwort, Sitzungstoken oder API-Key im Browser.

## Ersteinrichtung und das Fünf-Minuten-Fenster

Eine frische Portainer-2.39.5-Installation benötigt ein einmal verwendbares Setup-Token, bevor der erste Administrator angelegt werden kann. Portainer schreibt dieses Token in die Serverprotokolle. Lesen Sie auf dem Docker-Host die neueste Token-Zeile mit folgendem Befehl aus:

```bash
docker logs portainer 2>&1 | grep 'setup_token=' | tail -n 1
```

Kopieren Sie den Wert nach `setup_token=` in das Feld **Setup-Token** von Portainer. Das Token kann nur einmal verwendet werden. Der Benutzername ist standardmäßig `admin`, kann aber geändert werden; Portainer verlangt ein Passwort mit mindestens 12 Zeichen.

Der erste Administrator muss weiterhin innerhalb von fünf Minuten angelegt werden. Läuft dieses Fenster ab, starten Sie nur den Portainer-Container neu, öffnen Sie die Oberfläche sofort erneut und führen Sie danach den Token-Befehl erneut aus, damit das neueste Setup-Token verwendet wird:

```bash
docker restart portainer
```

MEM liest oder speichert weder das Portainer-Setup-Token noch das Administratorkennwort. Nach dem Anlegen des ersten Administrators erkennt der Portainer-Einrichtungsassistent die lokale Docker-Umgebung. In hostnativer Entwicklung kann MEM die laufende lokale Portainer-Startseite öffnen, bevor eine exakte Umgebungs-ID konfiguriert wurde; eine exakte Container-Übergabe bleibt bis zur vorhandenen serverseitigen Umgebungsinformation deaktiviert.

## Aktuellen Vorfallscontainer öffnen

Wenn ein Vorfall aktuelle MEM-eigene Docker-Nachweise besitzt, ruft **Diesen Container in Portainer öffnen** einen MEM-Redirect-Endpunkt auf. Der Server löst logische Ressource und aktuelle Containeridentität erst beim Aufruf auf.

Dadurch bleiben Browserlinks nach Container-Neuerstellung nicht veraltet. Nicht unterstützte, entfernte oder nicht auflösbare Ressourcen fallen auf die konfigurierte Containerliste zurück statt auf eine bekannte fehlerhafte Detailseite.

Der Seq-Arbeitsbereich verwendet dasselbe Modell für **Seq-Container in Portainer öffnen**.

## Eigentumsgrenze

Nur MEM-eigene logische Ressourcen sind für kontextbezogene Übergabe zugelassen. Der Browser kann keine beliebige Docker-Container-ID senden. Nicht verwaltete oder identitätsabweichende Ressourcen bleiben schreibgeschützt und werden nicht als MEM-eigen dargestellt.

MEM bietet über Diagnostics keine Container-Aktionen zum Starten, Stoppen, Neustarten, Entfernen, Ausführen, Bereinigen sowie keine Netzwerk- oder Volume-Löschung.

## Freigegebene Laufzeit

Neue MEM-verwaltete Installationen verwenden das exakt freigegebene Portainer-CE-Image:

```text
portainer/portainer-ce:2.39.5
```

Der Installationsworkflow löst die lokale unveränderliche `sha256:`-Imageidentität auf und erstellt den Container daraus. Normale Status-, Start- und Stop-Operationen laden kein Image herunter.

Die Laufzeit verwendet:

- das persistente Volume `portainer_data` unter `/data`;
- den Docker-Socket unter `/var/run/docker.sock`;
- privaten HTTPS-Port `9443`;
- keinen Edge-Agent-Port `8000`, solange kein künftiger expliziter Workflow ihn hinzufügt;
- keine automatische öffentliche NPM-Route.

Eine vorhandene nicht von MEM besessene Portainer-Installation – einschließlich einer funktionierenden 2.39.1-Instanz – wird beobachtet, aber nicht automatisch übernommen, ersetzt, neu markiert, neu gestartet oder aktualisiert.

## Kompatibilität exakter Links

Exakte Ressourcenlinks sind eine Komfortfunktion für den freigegebenen Portainer-2.39-Routenvertrag. Die Containerliste ist der stabile Fallback. Nach einer Versionsänderung muss die Direktlink-Kompatibilität erneut geprüft werden.

Offizielle Referenzen:

- [Portainer-Dokumentation](https://docs.portainer.io/)
- [Portainer CE mit Docker unter Linux installieren](https://docs.portainer.io/start/install-ce/server/docker/linux)
- [Portainer-Ersteinrichtung](https://docs.portainer.io/start/install-ce/server/setup)

## Rollback

Für ein Rollback der Übergabe können MEM-Portainer-Links ausgeblendet werden; Portainer bleibt separat erreichbar. Für ein Runtime-Rollback nur das dokumentierte vorherige unveränderliche Image verwenden und zuvor Datenkompatibilität prüfen. `portainer_data` erhalten und niemals blind downgraden.

Die Fresh-Server-Releaseprüfung soll unveränderliche Imageidentität, Datenpersistenz, Port `9443`, fehlenden Port `8000`, Eigentumslabels, keine öffentliche NPM-Route sowie Direktlink-Fallback prüfen.

## Verwandte Dokumentation

- [Das Diagnose-Kommandozentrum verwenden](../operations/diagnose-kommandocenter.md)
- [Seq mit MEM verwenden](../operations/seq-mit-mem.md)
