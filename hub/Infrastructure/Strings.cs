using System.Globalization;

namespace VoicedeckEdge.Hub.Infrastructure;

/// <summary>UI texts in German and English, chosen from the Windows display language.</summary>
internal static class Strings
{
    private static readonly bool German = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    private static string T(string de, string en) => German ? de : en;

    public static string AppName => "Voicedeck Edge Hub";

    // Status
    public static string StatusNotConfigured => T("Nicht eingerichtet – hier klicken zum Einrichten", "Not set up – click here to set up");
    public static string StatusDiscordNotRunning => T("Discord ist nicht geöffnet", "Discord is not running");
    public static string StatusConnecting => T("Verbinde mit Discord …", "Connecting to Discord …");
    public static string StatusUnauthorized => T("Noch nicht mit Discord verbunden – hier klicken", "Not connected to Discord yet – click here");
    public static string StatusReady => T("Verbunden mit Discord", "Connected to Discord");
    public static string StatusPortBusy => T("Port 47820 ist belegt – läuft der Hub schon?", "Port 47820 is in use – is the hub already running?");

    // Menu
    public static string MenuSetup => T("Einrichtungs-Assistent …", "Setup assistant …");
    public static string MenuUpdateCheck => T("Nach Updates suchen", "Check for updates");
    public static string MenuDeleteAll => T("Abmelden und alle Daten löschen …", "Sign out and delete all data …");
    public static string MenuAuthorize => T("Mit Discord verbinden", "Connect to Discord");
    public static string MenuPaired => T("Gekoppelte Widgets …", "Paired widgets …");
    public static string MenuAutostart => T("Mit Windows starten", "Start with Windows");
    public static string MenuLog => T("Log öffnen", "Open log");
    public static string MenuExit => T("Beenden", "Exit");

    // Setup assistant
    public static string SetupTitle => T("Voicedeck Edge – Einrichtung", "Voicedeck Edge – Setup");
    public static string Back => T("< Zurück", "< Back");
    public static string Next => T("Weiter >", "Next >");
    public static string Finish => T("Fertig", "Finish");
    public static string Cancel => T("Abbrechen", "Cancel");
    public static string Copy => T("Kopieren", "Copy");
    public static string Copied => T("Kopiert ✓", "Copied ✓");
    public static string StepOf(int step, int total) => T($"Schritt {step} von {total}", $"Step {step} of {total}");

    public static string WelcomeHeading => T("Willkommen bei Voicedeck Edge", "Welcome to Voicedeck Edge");
    public static string WelcomeText => T(
        "Mit Voicedeck Edge steuerst du Discord über deine XENEON EDGE: Sprachkanal, Stummschalten, Lautstärke pro Person, Kanäle und Chat.\n\n" +
        "Die Einrichtung dauert etwa 5 Minuten und passiert nur einmal:\n" +
        "  1.  Eine eigene Discord-App anlegen (Discord verlangt das)\n" +
        "  2.  Mit Discord verbinden\n" +
        "  3.  Das Widget zu iCUE hinzufügen",
        "Voicedeck Edge lets you control Discord from your XENEON EDGE: voice channel, mute, per-person volume, channels and chat.\n\n" +
        "Setup takes about 5 minutes and happens only once:\n" +
        "  1.  Create your own Discord app (Discord requires this)\n" +
        "  2.  Connect to Discord\n" +
        "  3.  Add the widget to iCUE");
    public static string PrivacyNote => T(
        "Datenschutz: Voicedeck Edge hat keine eigenen Server und sammelt keine Daten. Alles bleibt auf diesem PC. Chat-Nachrichten werden nie gespeichert.",
        "Privacy: Voicedeck Edge has no servers of its own and collects no data. Everything stays on this PC. Chat messages are never stored.");

    public static string AppHeading => T("Eigene Discord-App anlegen", "Create your own Discord app");
    public static string AppStep1 => T("1.  Öffne das Developer Portal und melde dich mit Discord an. Klicke auf „New Application“, gib einen Namen ein, z. B. diesen, und klicke auf „Create“:",
        "1.  Open the Developer Portal and sign in with Discord. Click “New Application”, enter a name, e.g. this one, and click “Create”:");
    public static string AppStep2 => T("2.  Klicke links auf „OAuth2“. Unter „Redirects“ auf „Add Redirect“ klicken und das hier eintragen. Dann „Public Client“ einschalten und unten „Save Changes“ klicken:",
        "2.  Click “OAuth2” on the left. Under “Redirects” click “Add Redirect” and enter this. Then switch on “Public Client” and click “Save Changes” at the bottom:");
    public static string AppStep3 => T("3.  Kopiere oben auf derselben Seite die „Client ID“ und füge sie hier ein:",
        "3.  Copy the “Client ID” at the top of the same page and paste it here:");
    public static string OpenPortal => T("Developer Portal öffnen", "Open Developer Portal");
    public static string ClientIdOk => T("✓ Sieht gut aus", "✓ Looks good");
    public static string ClientIdInvalid => T("Die Client-ID besteht nur aus Ziffern (15–21 Stellen). Nicht das Client Secret einfügen!",
        "The client ID consists of digits only (15–21). Do not paste the client secret!");

    public static string ConnectHeading => T("Mit Discord verbinden", "Connect to Discord");
    public static string ConnectText => T(
        "Discord zeigt gleich ein Fenster „Voicedeck Edge möchte …“. Klicke dort auf „Autorisieren“.\nDas ist nur einmal nötig.",
        "Discord will show a window “Voicedeck Edge wants to …”. Click “Authorize” there.\nYou only need to do this once.");
    public static string ConnectWaitingDiscord => T("Discord ist nicht geöffnet. Bitte starte Discord – ich warte hier.", "Discord is not running. Please start Discord – I'll wait here.");
    public static string ConnectConnecting => T("Verbinde mit Discord …", "Connecting to Discord …");
    public static string ConnectAuthorizing => T("Warte auf deine Bestätigung in Discord …", "Waiting for your confirmation in Discord …");
    public static string ConnectRetry => T("Erneut versuchen", "Try again");
    public static string ConnectFailed => T("Das hat nicht geklappt. Wurde in Discord auf „Autorisieren“ geklickt? Dann bitte erneut versuchen.",
        "That did not work. Did you click “Authorize” in Discord? Then please try again.");
    public static string ConnectInvalidId => T("Discord kennt diese Client-ID nicht. Bitte gehe zurück und prüfe sie.", "Discord does not know this client ID. Please go back and check it.");
    public static string ConnectDone => T("✓ Verbunden mit Discord", "✓ Connected to Discord");

    public static string WidgetHeading => T("Widget zu iCUE hinzufügen", "Add the widget to iCUE");
    public static string WidgetText => T(
        "Klicke auf den Knopf, um das Widget in iCUE zu importieren. Platziere es danach in iCUE auf deiner XENEON EDGE (Größe Medium, Large oder XL).\n\n" +
        "Beim ersten Start fragt dieser PC einmal „Widget erlauben?“ – dort auf „Erlauben“ klicken.",
        "Click the button to import the widget into iCUE. Then place it on your XENEON EDGE in iCUE (size Medium, Large or XL).\n\n" +
        "On first start this PC asks once “Allow widget?” – click “Allow” there.");
    public static string WidgetInstall => T("Widget zu iCUE hinzufügen", "Add widget to iCUE");
    public static string WidgetMissing => T("Das Widget-Paket liegt (noch) nicht neben dem Hub. Es kommt mit dem Installer.", "The widget package is not next to the hub (yet). It comes with the installer.");
    public static string WidgetOpenFailed => T("iCUE hat sich nicht gemeldet. Importiere die Datei in iCUE von Hand: ", "iCUE did not respond. Import the file in iCUE manually: ");
    public static string OptionAutostart => T("Voicedeck Edge Hub mit Windows starten (empfohlen)", "Start Voicedeck Edge Hub with Windows (recommended)");
    public static string OptionUpdates => T("Einmal täglich nach neuen Versionen suchen (fragt nur GitHub nach der neuesten Versionsnummer)",
        "Check for new versions once a day (only asks GitHub for the latest version number)");

    // Delete all data
    public static string DeleteTitle => T("Abmelden und alle Daten löschen", "Sign out and delete all data");
    public static string DeleteConfirm => T(
        "Das meldet Voicedeck Edge bei Discord ab und löscht:\n\n• deine Discord-Anmeldung (auch bei Discord widerrufen)\n• alle gekoppelten Widgets\n• die Einstellungen inkl. Client-ID\n• alle Logs\n\nDanach musst du Voicedeck Edge neu einrichten. Fortfahren?",
        "This signs Voicedeck Edge out of Discord and deletes:\n\n• your Discord sign-in (also revoked at Discord)\n• all paired widgets\n• the settings incl. client ID\n• all logs\n\nYou will have to set up Voicedeck Edge again. Continue?");
    public static string DeleteDone => T("Alle Daten wurden gelöscht.", "All data has been deleted.");
    public static string DeleteRevokeFailed => T("\n\nHinweis: Discord war nicht erreichbar, die Anmeldung konnte dort nicht widerrufen werden. Du kannst sie in Discord unter Einstellungen → Autorisierte Apps entfernen.",
        "\n\nNote: Discord could not be reached, so the sign-in could not be revoked there. You can remove it in Discord under Settings → Authorized Apps.");

    // Updates
    public static string UpdateAvailable(string version) => T($"Version {version} ist verfügbar – klicken zum Herunterladen.", $"Version {version} is available – click to download.");

    // Pairing
    public static string PairTitle => T("Widget erlauben?", "Allow widget?");
    public static string PairText(string origin) => T(
        $"Ein Widget möchte Discord über Voicedeck Edge steuern.\n\nHerkunft: {origin}\n\nErlaube das nur, wenn du gerade das Voicedeck-Edge-Widget in iCUE hinzugefügt hast.",
        $"A widget wants to control Discord through Voicedeck Edge.\n\nOrigin: {origin}\n\nOnly allow this if you just added the Voicedeck Edge widget in iCUE.");
    public static string Allow => T("Erlauben", "Allow");
    public static string Deny => T("Ablehnen", "Deny");
    public static string UnknownOrigin => T("lokales Programm", "local program");
    public static string IcueOrigin => T("iCUE-Widget (lokale Datei)", "iCUE widget (local file)");

    // Paired widgets form
    public static string PairedTitle => T("Gekoppelte Widgets", "Paired widgets");
    public static string PairedColumnDate => T("Gekoppelt am", "Paired on");
    public static string PairedColumnOrigin => T("Herkunft", "Origin");
    public static string PairedRevoke => T("Kopplung aufheben", "Unpair");
    public static string PairedEmpty => T("Noch keine Widgets gekoppelt.", "No widgets paired yet.");
    public static string Close => T("Schließen", "Close");

    public static string AlreadyRunning => T("Voicedeck Edge Hub läuft bereits (siehe Taskleiste).", "Voicedeck Edge Hub is already running (see taskbar).");
}
