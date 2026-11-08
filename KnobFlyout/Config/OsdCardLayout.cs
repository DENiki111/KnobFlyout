using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnobFlyout.Config
{
    /// <summary>
    /// Aufbau der OSD-Karten für ein Design: welche Teile zu sehen sind und in welcher Reihenfolge (von links nach rechts).
    /// Leer heißt „wie das Design es vorgibt“; eine unveränderte Karte zeigt das Design also genau wie gestaltet.
    /// Welche Teile es gibt, hängt vom Design ab (OsdCard.PartKeys); Einträge für Teile, die ein Design nicht hat, zählen nicht.
    /// </summary>
    public sealed class OsdCardLayout
    {
        public const string Volume = "volume", Profile = "profile", LockKey = "lockkey", Snap = "snap";
        public static readonly string[] Kinds = { Volume, Profile, LockKey, Snap };

        public const string Icon = "icon", Name = "name", Bar = "bar", Value = "value", State = "state", Profiles = "profiles";

        /// <summary>Gewählte Vorlage (null = „standard“).</summary>
        [JsonPropertyName("template"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Template { get; set; }

        /// <summary>Reihenfolge der Teile je Karte (Kind → Teile).</summary>
        [JsonPropertyName("order"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, List<string>>? Order { get; set; }

        /// <summary>Ausgeblendete Teile als „karte.teil“, z. B. „volume.name“.</summary>
        [JsonPropertyName("hidden"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<string>? Hidden { get; set; }

        /// <summary>Inhalt der ganzen Karte „left“ | „center“ | „right“ (null = wie im Design, die Leiste füllt den Platz).</summary>
        [JsonPropertyName("align"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Align { get; set; }

        [JsonIgnore] public bool IsEmpty => Template == null && Order == null && Hidden == null && Align == null;

        public bool Shows(string kind, string part) => Hidden?.Contains(kind + "." + part) != true;

        public void Set(string kind, string part, bool show)
        {
            var hidden = Hidden ?? new List<string>();
            hidden.Remove(kind + "." + part);
            if (!show) hidden.Add(kind + "." + part);
            Hidden = hidden.Count > 0 ? hidden : null;
        }

        /// <summary>Reihenfolge ohne eigene Angabe: wie im Design, bei der Vorlage „Gespiegelt“ umgekehrt.</summary>
        private List<string> Natural(IReadOnlyList<string> parts)
        {
            var list = parts.ToList();
            if (Template == "mirrored") list.Reverse();
            return list;
        }

        /// <summary>Reihenfolge der Teile, die das Design hat (parts in seiner Reihenfolge); fehlende kommen hinten dazu.</summary>
        public List<string> OrderFor(string kind, IReadOnlyList<string> parts)
        {
            if (Order == null || !Order.TryGetValue(kind, out var saved)) return Natural(parts);
            var result = saved.Where(parts.Contains).Distinct().ToList();
            result.AddRange(parts.Where(p => !result.Contains(p)));
            return result;
        }

        public void SetOrder(string kind, List<string> order, IReadOnlyList<string> parts)
        {
            var dict = Order ?? new Dictionary<string, List<string>>();
            if (order.SequenceEqual(Natural(parts))) dict.Remove(kind);
            else dict[kind] = new List<string>(order);
            Order = dict.Count > 0 ? dict : null;
        }

        /// <summary>Karte unverändert: Das Design zeichnet sie genau wie gestaltet.</summary>
        public bool IsDefault(string kind, IReadOnlyList<string> parts) => Align == null && OrderFor(kind, parts).SequenceEqual(parts) && parts.All(p => Shows(kind, p));

        public OsdCardLayout Clone() => JsonSerializer.Deserialize<OsdCardLayout>(JsonSerializer.Serialize(this)) ?? new OsdCardLayout();

        /// <summary>Vorlagen, die bei jedem Design gehen: Standard, Kompakt, Minimal, Gespiegelt.</summary>
        public static readonly string[] TemplateKeys = { "standard", "compact", "minimal", "mirrored" };

        public static OsdCardLayout FromTemplate(string? key)
        {
            var layout = new OsdCardLayout { Template = key == "standard" ? null : key };
            switch (key)
            {
                case "compact": // ohne App-Namen und ohne Leisten auf den Infokarten
                    layout.Hidden = new List<string> { "volume.name", "volume.profiles", "profile.bar", "lockkey.bar", "snap.bar" };
                    break;
                case "minimal": // nur das Nötigste: Leiste und Zahl, sonst der Name
                    layout.Hidden = new List<string>
                    {
                        "volume.icon", "volume.name", "volume.profiles", "profile.icon", "profile.bar",
                        "lockkey.icon", "lockkey.bar", "snap.icon", "snap.bar",
                    };
                    break;
            }
            return layout;
        }
    }
}
