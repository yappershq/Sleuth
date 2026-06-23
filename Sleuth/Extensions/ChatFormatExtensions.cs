namespace Sleuth.Extensions;

using System;
using System.Collections.Generic;
using Sharp.Shared.Definition;

internal static class ChatFormat
{
    private static readonly Dictionary<string, string> ColorCache = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        { "{white}",      ChatColor.White },
        { "{default}",    ChatColor.White },
        { "{darkred}",    ChatColor.DarkRed },
        { "{pink}",       ChatColor.Pink },
        { "{green}",      ChatColor.Green },
        { "{lightgreen}", ChatColor.LightGreen },
        { "{lime}",       ChatColor.Lime },
        { "{red}",        ChatColor.Red },
        { "{grey}",       ChatColor.Grey },
        { "{gray}",       ChatColor.Grey },
        { "{yellow}",     ChatColor.Yellow },
        { "{gold}",       ChatColor.Gold },
        { "{silver}",     ChatColor.Silver },
        { "{blue}",       ChatColor.Blue },
        { "{lightblue}",  ChatColor.Blue },
        { "{darkblue}",   ChatColor.DarkBlue },
        { "{purple}",     ChatColor.Purple },
        { "{lightred}",   ChatColor.LightRed },
        { "{muted}",      ChatColor.Muted },
        { "{head}",       ChatColor.Head },
        { "{whitespace}", " " },
    };

    /// <summary>
    /// Replace color placeholders like {red}, {green}, {default}, etc. with actual ChatColor
    /// escape codes for use in chat messages (HudPrintChannel.Chat).
    /// Copied from SuperPowers.Shared.Extensions.ChatFormat — no external dep needed.
    /// </summary>
    internal static string ProcessColorCodes(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        if (!message.Contains('{'))
            return message;

        var result = message;
        foreach (var (placeholder, code) in ColorCache)
            result = result.Replace(placeholder, code, StringComparison.OrdinalIgnoreCase);

        return result;
    }
}
