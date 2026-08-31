using HarmonyLib;

namespace MyPuckMod
{
    /// <summary>
    /// Puck startet den Practice-Modus intern mit maxPlayers = 1.
    /// Das ist fuer einen lokalen Bot-Test zu knapp und kann beim ersten
    /// Verbindungsversuch zu "Server is full" fuehren.
    ///
    /// Dieser kleine Patch aendert NUR den lokalen PRACTICE-Server auf vier
    /// Plaetze. Andere selbst gehostete oder oeffentliche Server bleiben gleich.
    /// </summary>
    [HarmonyPatch(typeof(ServerManager), "StartHost")]
    internal static class PracticeCapacityPatch
    {
        private static void Prefix(string name, ref int maxPlayers)
        {
            if (name == "PRACTICE")
            {
                maxPlayers = 4;
            }
        }
    }
}
