using System.Text.Json.Serialization;

namespace PadreScraper;
public class AlphaTrackerCoin
{
    public string? Name { get; set; }
    public string? MentionedInGroup { get; set; }
    public string? TimeAgo { get; set; }
    public string? Link { get; set; }

    // Переопределяем ToString() для удобного вывода в консоль
    public override string ToString()
    {
        return $"--- Монета: {Name ?? "N/A"} ---\n" +
               $"  Упомянута в: {MentionedInGroup ?? "N/A"}\n" +
               $"  Когда: {TimeAgo ?? "N/A"}" +
               $"  Ссылка: {Link ?? "N/A"}";
    }
}