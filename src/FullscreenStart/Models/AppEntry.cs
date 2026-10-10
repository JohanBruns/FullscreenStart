using System.ComponentModel;
using System.Windows.Media;
using FullscreenStart.Services;

namespace FullscreenStart.Models;

/// <summary>An installed app as listed in shell:AppsFolder (or a Start Menu shortcut).</summary>
public sealed class AppEntry : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private ImageSource? _largeIcon;

    public AppEntry(string id, string name, byte[] pidl, string? linkPath)
    {
        Id = id;
        Name = name;
        Pidl = pidl;
        LinkPath = linkPath;
        Letter = LetterOf(name);
    }

    /// <summary>Parsing name inside shell:AppsFolder (AUMID or path), or the .lnk path for extra shortcuts.</summary>
    public string Id { get; }
    public string Name { get; }

    /// <summary>Absolute item ID list used to launch the app and load its icon.</summary>
    public byte[] Pidl { get; }

    /// <summary>Start Menu shortcut, if one was found.</summary>
    public string? LinkPath { get; }

    /// <summary>Store / UWP app (identified by an AppUserModelID such as "Publisher.App_hash!App").</summary>
    public bool IsPackaged => Id.Contains('!') && !Id.Contains('\\');

    public bool IsWebLink => Id.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                             || Id.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Index letter for the "Alle Apps" list: A–Z or "#".</summary>
    public string Letter { get; }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            if (_largeIcon == null) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LargeIcon)));
        }
    }

    /// <summary>High-resolution icon for big tiles (loaded for pinned apps only); falls back to <see cref="Icon"/>.</summary>
    public ImageSource? LargeIcon
    {
        get => _largeIcon ?? _icon;
        set
        {
            if (ReferenceEquals(_largeIcon, value)) return;
            _largeIcon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LargeIcon)));
        }
    }

    public bool HasLargeIcon => _largeIcon != null;

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Name;

    public static string LetterOf(string name)
    {
        var normalized = SearchScorer.Normalize(name).TrimStart();
        if (normalized.Length == 0) return "#";
        char c = char.ToUpperInvariant(normalized[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : "#";
    }
}
