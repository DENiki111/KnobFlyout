# KnobFlyout Browser-Bridge – Privacy Policy

Last updated: 19 November 2026 · *Deutsche Fassung weiter unten.*

## The short version

The extension only talks to the KnobFlyout app on your own computer. It sends nothing to the internet, nothing to me and nothing to anyone else. There is no tracking, no analytics, no ads and no account, and the extension doesn't store anything itself.

## Who this is from and how to reach me

KnobFlyout and the KnobFlyout Browser-Bridge are a free, open-source hobby project by the developer of KnobFlyout (source code: [github.com/DENiki111/KnobFlyout](https://github.com/DENiki111/KnobFlyout)).

Questions about privacy: open an issue at [github.com/DENiki111/KnobFlyout/issues](https://github.com/DENiki111/KnobFlyout/issues), or for anything you don't want to post publicly, use the contact email shown in the extension's store listing.

## What this policy covers

This policy covers the KnobFlyout Browser-Bridge extension for Chrome, Opera, Opera GX and Firefox, and what the KnobFlyout app on your computer does with the data it gets from the extension. KnobFlyout has other features that have nothing to do with the extension, such as the optional Spotify connection. They are not covered here.

## What the extension reads

The extension runs on every site, but it only becomes active on pages that have a video. There it reads:

- **About the video:** title, channel or artist, the address of the thumbnail image. If the site doesn't give a title, the extension uses the title of the page or tab instead.
- **Playback:** whether it is playing or paused, the position, the length, the playback speed and whether it's a live stream.
- **The player:** volume and mute, and the name of the site (for example "YouTube").

To figure out which site it is on, the extension looks at the page address and a few technical parts of the page (for example the addresses of scripts the page loads). That check happens in your browser and is not sent anywhere.

It also sends a few technical details to KnobFlyout:

- which browser it is running in (Chrome, Opera or Firefox), so KnobFlyout can match it to the right app in the Windows volume mixer
- the version of the connection protocol
- the text of an error message if the extension's own script fails (no page content)
- if a video plays on a site the extension doesn't know: only the host name of that site (for example `example.com`), so KnobFlyout's log shows which site isn't supported yet

On YouTube, a small script runs inside the page so it can use the YouTube player's own volume and seek functions. It reads the same information as listed above.

## What the extension does not do

- It doesn't read or send your browsing history, the full addresses of pages you visit, cookies, passwords, form entries, messages, or anything you type.
- It doesn't send anything to any server on the internet, including mine.
- It doesn't use analytics, ads, fingerprinting or tracking of any kind.
- It doesn't load or run code from the internet. All of its code is inside the extension package.
- It doesn't store anything in the browser. What it knows about the current video is kept in memory and is gone when you close the tab.

## Where the data goes

Everything goes only to KnobFlyout on the same computer, over a local connection to `ws://127.0.0.1:8975`. The address 127.0.0.1 only exists inside your own computer, so other devices on your network or the internet can't reach it. KnobFlyout only accepts connections from browser extensions (origin `chrome-extension://` or `moz-extension://`), not from normal websites.

KnobFlyout sends commands back to the extension: change the volume, mute, play, pause, jump to a position, change the speed, and next or previous video on YouTube. The extension carries them out on the video and nothing else.

If KnobFlyout isn't running, the extension just keeps trying to reach it every few seconds. Nothing is sent anywhere else in the meantime.

## What KnobFlyout does with it

KnobFlyout uses the data only for the features you switched on: it shows the video in its media flyout (title, thumbnail, timeline) and lets your volume knob and hotkeys control the player.

- **Thumbnail:** to show the thumbnail, KnobFlyout downloads the image directly from the address the page provides (for example `i.ytimg.com` on YouTube, or your own Jellyfin server). That image server sees a normal image request from your computer, including your IP address, just like when the website loads the image itself. Thumbnails are only kept in memory and are gone when KnobFlyout closes.
- **Log file:** KnobFlyout writes a log file (`knob_service.log`) in its program folder on your computer. With the extension switched on, it can contain video titles, site names, host names of unknown video sites and connection messages. The log never leaves your computer. It stays until you delete it. You find it in KnobFlyout under Settings → Info → Log.

## Sharing and selling

No data is sold, rented or shared with anyone. It isn't used for advertising, isn't used to determine creditworthiness or for lending, and isn't used for anything other than the features described above. The use of information received from the browser adheres to the Chrome Web Store User Data Policy, including the Limited Use requirements.

## Permissions

The extension asks for exactly one thing: permission to run its script on all websites. That's needed because some supported video sites don't have a fixed address. Jellyfin runs on your own server under your own domain, and some video hosts keep switching between many mirror domains. On pages without a video the script does nothing. The extension doesn't request any other permissions (no access to tabs, history, downloads, storage or cookies).

## Legal basis (GDPR)

The developer doesn't receive any personal data from the extension. Everything described here happens on your own device, in your browser and in KnobFlyout, which you installed and switched on yourself. Where the GDPR applies to this at all, the legal basis is Art. 6(1)(f) GDPR: the legitimate interest in providing the feature you switched on.

## How long data is kept and how to delete it

- **Extension:** nothing is stored. Closing the tab or the browser removes everything.
- **KnobFlyout:** the current state of the videos is kept in memory while KnobFlyout runs. The log file stays until you delete it.
- **To stop everything:** remove the extension from your browser, or switch off “Control volume in the video player” in KnobFlyout (Settings → Browser-Addon). Then nothing is exchanged anymore.

## Your rights

Under the GDPR you have the right to access, rectification, erasure, restriction of processing, data portability and to object, and you can lodge a complaint with a data protection supervisory authority. Since I don't store any data about you, there is nothing on my side to hand over or delete. Everything is on your own device and under your control.

## Children

The extension isn't aimed at children. It doesn't collect data from anyone, children included.

## Changes to this policy

If the extension changes in a way that affects this policy, I update this page and the date at the top. Older versions stay visible in the history of this repository.

---

# KnobFlyout Browser-Bridge – Datenschutzerklärung

Stand: 19. November 2026

## Kurz gesagt

Die Erweiterung spricht nur mit dem Programm KnobFlyout auf deinem eigenen Computer. Sie schickt nichts ins Internet, nichts an mich und an niemanden sonst. Es gibt kein Tracking, keine Statistik, keine Werbung und kein Konto, und die Erweiterung speichert selbst nichts.

## Von wem das ist und wie du mich erreichst

KnobFlyout und die KnobFlyout Browser-Bridge sind ein kostenloses Open-Source-Hobbyprojekt des Entwicklers von KnobFlyout (Quellcode: [github.com/DENiki111/KnobFlyout](https://github.com/DENiki111/KnobFlyout)).

Fragen zum Datenschutz: als Issue auf [github.com/DENiki111/KnobFlyout/issues](https://github.com/DENiki111/KnobFlyout/issues), oder für alles, was du nicht öffentlich schreiben willst, über die Kontakt-E-Mail-Adresse im Store-Eintrag der Erweiterung.

## Wofür diese Erklärung gilt

Diese Erklärung gilt für die Erweiterung KnobFlyout Browser-Bridge für Chrome, Opera, Opera GX und Firefox und dafür, was das Programm KnobFlyout auf deinem Computer mit den Daten aus der Erweiterung macht. KnobFlyout hat noch andere Funktionen, die mit der Erweiterung nichts zu tun haben, zum Beispiel die freiwillige Spotify-Verbindung. Für die gilt diese Erklärung nicht.

## Was die Erweiterung liest

Die Erweiterung läuft auf allen Seiten, wird aber nur auf Seiten mit einem Video aktiv. Dort liest sie:

- **Zum Video:** Titel, Kanal bzw. Interpret und die Adresse des Vorschaubilds. Gibt die Seite keinen Titel an, nimmt die Erweiterung stattdessen den Titel der Seite bzw. des Tabs.
- **Zur Wiedergabe:** ob es spielt oder pausiert, die Position, die Länge, das Tempo und ob es ein Livestream ist.
- **Zum Player:** Lautstärke und Stumm sowie den Namen der Seite (zum Beispiel „YouTube“).

Um zu erkennen, auf welcher Seite sie ist, schaut sich die Erweiterung die Adresse der Seite und ein paar technische Teile der Seite an (zum Beispiel die Adressen der Skripte, die die Seite lädt). Das passiert in deinem Browser und wird nirgendwohin geschickt.

Außerdem schickt sie ein paar technische Angaben an KnobFlyout:

- in welchem Browser sie läuft (Chrome, Opera oder Firefox), damit KnobFlyout sie der richtigen App im Windows-Lautstärkemixer zuordnen kann
- die Version des Verbindungsprotokolls
- den Text einer Fehlermeldung, falls das Skript der Erweiterung selbst einen Fehler hat (kein Seiteninhalt)
- wenn ein Video auf einer Seite spielt, die die Erweiterung nicht kennt: nur den Hostnamen dieser Seite (zum Beispiel `example.com`), damit im Protokoll von KnobFlyout steht, welche Seite noch nicht unterstützt wird

Auf YouTube läuft ein kleines Skript in der Seite selbst, damit es die eigenen Lautstärke- und Spul-Funktionen des YouTube-Players nutzen kann. Es liest dieselben Angaben wie oben.

## Was die Erweiterung nicht macht

- Sie liest und schickt weder deinen Verlauf noch die vollständigen Adressen der Seiten, die du besuchst, keine Cookies, Passwörter, Formulareingaben, Nachrichten oder irgendetwas, das du eintippst.
- Sie schickt nichts an irgendeinen Server im Internet, auch nicht an mich.
- Sie nutzt keine Statistik, keine Werbung, kein Fingerprinting und keinerlei Tracking.
- Sie lädt und startet keinen Code aus dem Internet. Ihr ganzer Code steckt im Paket der Erweiterung.
- Sie speichert nichts im Browser. Was sie über das aktuelle Video weiß, liegt nur im Arbeitsspeicher und ist weg, sobald du den Tab schließt.

## Wohin die Daten gehen

Alles geht nur an KnobFlyout auf demselben Computer, über eine lokale Verbindung zu `ws://127.0.0.1:8975`. Die Adresse 127.0.0.1 gibt es nur innerhalb deines eigenen Computers, andere Geräte in deinem Netzwerk oder im Internet kommen da nicht dran. KnobFlyout nimmt nur Verbindungen von Browser-Erweiterungen an (Herkunft `chrome-extension://` oder `moz-extension://`), nicht von normalen Webseiten.

KnobFlyout schickt Befehle an die Erweiterung zurück: Lautstärke ändern, stumm schalten, abspielen, pausieren, an eine Stelle springen, Tempo ändern und bei YouTube nächstes oder vorheriges Video. Die Erweiterung führt sie am Video aus und sonst nichts.

Läuft KnobFlyout nicht, versucht die Erweiterung nur alle paar Sekunden, es zu erreichen. In der Zwischenzeit wird nichts woandershin geschickt.

## Was KnobFlyout damit macht

KnobFlyout nutzt die Daten nur für die Funktionen, die du eingeschaltet hast: Es zeigt das Video im Media-Flyout (Titel, Vorschaubild, Zeitleiste) und lässt deinen Lautstärke-Knopf und deine Hotkeys den Player steuern.

- **Vorschaubild:** Für das Vorschaubild lädt KnobFlyout das Bild direkt von der Adresse, die die Seite angibt (zum Beispiel `i.ytimg.com` bei YouTube oder dein eigener Jellyfin-Server). Dieser Bildserver sieht eine ganz normale Bildanfrage von deinem Computer, mit deiner IP-Adresse, genau wie wenn die Webseite das Bild selbst lädt. Vorschaubilder liegen nur im Arbeitsspeicher und sind weg, wenn KnobFlyout beendet wird.
- **Protokoll:** KnobFlyout schreibt eine Protokolldatei (`knob_service.log`) in seinen Programmordner auf deinem Computer. Bei eingeschalteter Erweiterung können darin Videotitel, Seitennamen, Hostnamen unbekannter Video-Seiten und Verbindungsmeldungen stehen. Das Protokoll verlässt deinen Computer nie und bleibt, bis du es löschst. Du findest es in KnobFlyout unter Einstellungen → Info → Protokoll.

## Weitergabe und Verkauf

Daten werden weder verkauft noch vermietet noch an irgendwen weitergegeben. Sie werden nicht für Werbung genutzt, nicht zur Prüfung der Kreditwürdigkeit oder für Kredite und für nichts anderes als die oben beschriebenen Funktionen. Die Nutzung der Informationen aus dem Browser hält die Chrome-Web-Store-Richtlinie zu Nutzerdaten ein, einschließlich der Anforderungen zur eingeschränkten Nutzung („Limited Use“).

## Berechtigungen

Die Erweiterung fragt genau nach einer Sache: ihr Skript auf allen Webseiten ausführen zu dürfen. Das braucht sie, weil manche unterstützten Video-Seiten keine feste Adresse haben. Jellyfin läuft auf deinem eigenen Server unter deiner eigenen Adresse, und manche Video-Hoster wechseln ständig zwischen vielen Ausweich-Adressen. Auf Seiten ohne Video macht das Skript nichts. Andere Berechtigungen hat die Erweiterung nicht (kein Zugriff auf Tabs, Verlauf, Downloads, Speicher oder Cookies).

## Rechtsgrundlage (DSGVO)

Der Entwickler bekommt von der Erweiterung keine personenbezogenen Daten. Alles, was hier beschrieben ist, passiert auf deinem eigenen Gerät, in deinem Browser und in KnobFlyout, das du selbst installiert und eingeschaltet hast. Soweit die DSGVO darauf überhaupt anwendbar ist, ist die Rechtsgrundlage Art. 6 Abs. 1 lit. f DSGVO: das berechtigte Interesse, die Funktion bereitzustellen, die du eingeschaltet hast.

## Wie lange Daten bleiben und wie du sie löschst

- **Erweiterung:** Es wird nichts gespeichert. Tab oder Browser schließen, und alles ist weg.
- **KnobFlyout:** Der aktuelle Stand der Videos liegt im Arbeitsspeicher, solange KnobFlyout läuft. Das Protokoll bleibt, bis du es löschst.
- **Um alles zu beenden:** die Erweiterung aus dem Browser entfernen oder in KnobFlyout „Lautstärke im Video-Player regeln“ ausschalten (Einstellungen → Browser-Addon). Danach wird nichts mehr ausgetauscht.

## Deine Rechte

Nach der DSGVO hast du das Recht auf Auskunft, Berichtigung, Löschung, Einschränkung der Verarbeitung, Datenübertragbarkeit und Widerspruch, und du kannst dich bei einer Datenschutz-Aufsichtsbehörde beschweren. Da ich keine Daten über dich speichere, gibt es bei mir nichts herauszugeben oder zu löschen. Alles liegt auf deinem eigenen Gerät und in deiner Hand.

## Kinder

Die Erweiterung richtet sich nicht an Kinder. Sie erhebt von niemandem Daten, auch nicht von Kindern.

## Änderungen dieser Erklärung

Ändert sich die Erweiterung so, dass es diese Erklärung betrifft, passe ich diese Seite und das Datum oben an. Ältere Fassungen bleiben in der Versionsgeschichte dieses Repos sichtbar.
