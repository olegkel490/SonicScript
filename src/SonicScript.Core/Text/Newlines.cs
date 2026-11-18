namespace SonicScript.Core.Text;

public static class Newlines
{
    /// <summary>\n → \r\n: Ältere Eingabefelder (Editor, Win32-Edit, manche Mailprogramme) zeigen sonst keine Umbrüche.</summary>
    public static string ToWindows(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
}
