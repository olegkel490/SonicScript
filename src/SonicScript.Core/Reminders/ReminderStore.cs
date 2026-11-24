using System.Text.Json;

namespace SonicScript.Core.Reminders;

public sealed class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Due { get; set; }
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    /// <summary>Wie oft schon angezeigt (ohne Reaktion) – nach 3× gilt sie als erledigt.</summary>
    public int Shown { get; set; }
}

/// <summary>Erinnerungen aus Notizen, in reminders.json. Verpasste (App war aus) werden beim Start nachgeholt.</summary>
public sealed class ReminderStore(string path)
{
    private readonly object _gate = new();
    public const int MaxShown = 3;

    public Reminder Add(DateTime due, string text)
    {
        lock (_gate)
        {
            var list = Load();
            var r = new Reminder { Due = due, Text = text };
            list.Add(r);
            Save(list);
            return r;
        }
    }

    /// <summary>Fällige, noch offene Erinnerungen (älteste zuerst).</summary>
    public IReadOnlyList<Reminder> DueAt(DateTime now)
    {
        lock (_gate) return Load().Where(r => !r.Done && r.Due <= now).OrderBy(r => r.Due).ToList();
    }

    public IReadOnlyList<Reminder> Pending()
    {
        lock (_gate) return Load().Where(r => !r.Done).OrderBy(r => r.Due).ToList();
    }

    public void Complete(string id) => Update(id, r => r.Done = true);

    public void Snooze(string id, DateTime until) => Update(id, r => r.Due = until);

    /// <summary>Angezeigt, aber nicht beantwortet: in 10 Minuten erneut, nach <see cref="MaxShown"/>× erledigt.</summary>
    public void Ignored(string id, DateTime now) => Update(id, r =>
    {
        r.Shown++;
        if (r.Shown >= MaxShown) r.Done = true;
        else r.Due = now.AddMinutes(10);
    });

    private void Update(string id, Action<Reminder> change)
    {
        lock (_gate)
        {
            var list = Load();
            if (list.FirstOrDefault(r => r.Id == id) is not { } r) return;
            change(r);
            // Erledigte älter als 30 Tage aufräumen
            list.RemoveAll(x => x.Done && x.Due < DateTime.Now.AddDays(-30));
            Save(list);
        }
    }

    private List<Reminder> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save(List<Reminder> list)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Storage.AtomicFile.WriteAllText(path, JsonSerializer.Serialize(list));
    }
}
