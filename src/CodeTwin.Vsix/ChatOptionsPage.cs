using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CodeTwin.Vsix;

public enum ChatProvider
{
    [Description("GitHub Copilot")]
    Copilot,

    [Description("Claude")]
    Claude,
}

/// <summary>Tools > Options > CodeTwin > Chat, also opened from Tools > CodeTwin > Settings. Stored per user; applying a change restarts the chat on the chosen provider.</summary>
[ComVisible(true)]
[Guid("b7d2e4a1-3c6f-4e8b-9a05-2f1d8c7e6b43")]
public sealed class ChatOptionsPage : DialogPage
{
    [Category("Chat")]
    [DisplayName("AI provider")]
    [Description("Which subscription the CodeTwin chat uses: GitHub Copilot (Copilot CLI) or Claude (Claude Code). Each signs in with your own account. Changing it starts a new chat.")]
    [TypeConverter(typeof(DescriptionEnumConverter))]
    public ChatProvider Provider { get; set; } = ChatProvider.Copilot;

    [Category("Chat")]
    [DisplayName("Claude Code path")]
    [Description("Full path of claude.exe. Leave empty to use the Claude Code on PATH or in %USERPROFILE%\\.local\\bin.")]
    public string ClaudeCliPath { get; set; } = string.Empty;

    public static event EventHandler? Applied;

    /// <summary>The variables that tell the chat sidecar which provider to run.</summary>
    internal IReadOnlyDictionary<string, string> SidecarEnvironment()
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["CODETWIN_CHAT_PROVIDER"] = Provider == ChatProvider.Claude ? "claude" : "copilot",
        };
        if (!string.IsNullOrWhiteSpace(ClaudeCliPath))
        {
            environment["CLAUDE_CLI_PATH"] = ClaudeCliPath.Trim().Trim('"');
        }

        return environment;
    }

    protected override void OnApply(PageApplyEventArgs e)
    {
        base.OnApply(e);
        if (e.ApplyBehavior == ApplyKind.Apply)
        {
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows the enum's [Description] in the options grid instead of its name.</summary>
    private sealed class DescriptionEnumConverter() : EnumConverter(typeof(ChatProvider))
    {
        public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) =>
            value is ChatProvider provider && destinationType == typeof(string)
                ? Describe(provider)
                : base.ConvertTo(context, culture, value, destinationType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object value)
        {
            foreach (ChatProvider provider in Enum.GetValues(typeof(ChatProvider)))
            {
                if (string.Equals(value as string, Describe(provider), StringComparison.Ordinal))
                {
                    return provider;
                }
            }

            return base.ConvertFrom(context, culture, value);
        }

        private static string Describe(ChatProvider provider) =>
            typeof(ChatProvider).GetField(provider.ToString())?.GetCustomAttributes(typeof(DescriptionAttribute), false) is { Length: > 0 } attributes
                ? ((DescriptionAttribute)attributes[0]).Description
                : provider.ToString();
    }
}
