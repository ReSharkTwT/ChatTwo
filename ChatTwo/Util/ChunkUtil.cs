using ChatTwo.Code;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Text;
using Lumina.Text.Payloads;

using PayloadType = Dalamud.Game.Text.SeStringHandling.PayloadType;

namespace ChatTwo.Util;

internal static class ChunkUtil
{
    internal static IEnumerable<Chunk> ToChunks(SeString msg, ChunkSource source, ChatType? chatType)
    {
        var chunks = new List<Chunk>();
        var italic = false;
        var foreground = new Stack<uint>();
        var glow = new Stack<uint>();
        Payload? link = null;

        void Append(string text)
        {
            chunks.Add(new TextChunk(source, link, text)
            {
                FallbackColour = chatType,
                Foreground = foreground.Count > 0 ? foreground.Peek() : null,
                Glow = glow.Count > 0 ? glow.Peek() : null,
                Italic = italic,
            });
        }
        foreach (var payload in msg.Payloads)
        {
            switch (payload.Type)
            {
                case PayloadType.EmphasisItalic:
                    var newStatus = ((EmphasisItalicPayload) payload).IsEnabled;
                    italic = newStatus;
                    break;
                case PayloadType.UIForeground:
                    var foregroundPayload = (UIForegroundPayload) payload;
                    if (foregroundPayload.IsEnabled)
                        foreground.Push(foregroundPayload.UIColor.Value.Dark);
                    else if (foreground.Count > 0)
                        foreground.Pop();
                    break;
                case PayloadType.UIGlow:
                    var glowPayload = (UIGlowPayload) payload;
                    if (glowPayload.IsEnabled)
                        glow.Push(glowPayload.UIColor.Value.Light);
                    else if (glow.Count > 0)
                        glow.Pop();
                    break;
                case PayloadType.AutoTranslateText:
                    chunks.Add(new IconChunk(source, payload, BitmapFontIcon.AutoTranslateBegin));
                    var autoText = ((AutoTranslatePayload) payload).Text;
                    Append(autoText.Substring(2, autoText.Length - 4));
                    chunks.Add(new IconChunk(source, link, BitmapFontIcon.AutoTranslateEnd));
                    break;
                case PayloadType.Icon:
                    chunks.Add(new IconChunk(source, link, ((IconPayload) payload).Icon));
                    break;
                case PayloadType.MapLink:
                case PayloadType.Quest:
                case PayloadType.DalamudLink:
                case PayloadType.Status:
                case PayloadType.Item:
                case PayloadType.Player:
                    link = payload;
                    break;
                case PayloadType.PartyFinder:
                    link = payload;
                    break;
                case PayloadType.Unknown:
                    var rawPayload = (RawPayload) payload;
                    var colorPayload = ColorPayload.From(rawPayload.Data);
                    if (colorPayload != null)
                    {
                        if (colorPayload.Enabled)
                        {
                            if (colorPayload.Color > 0)
                                foreground.Push(colorPayload.Color);
                            else if (foreground.Count > 0) // Push the previous color as we don't want invisible text
                                foreground.Push(foreground.Peek());
                        }
                        else if (foreground.Count > 0)
                        {
                            foreground.Pop();
                        }
                    }
                    else if (rawPayload.Data.Length > 1 && rawPayload.Data[1] == 0x14)
                    {
                        if (glow.Count > 0)
                        {
                            glow.Pop();
                        }
                        else if (rawPayload.Data.Length > 6 && rawPayload.Data[2] == 0x05 && rawPayload.Data[3] == 0xF6)
                        {
                            var (r, g, b) = (rawPayload.Data[4], rawPayload.Data[5], rawPayload.Data[6]);
                            glow.Push(ColourUtil.ComponentsToRgba(r, g, b));
                        }
                    }
                    else if (rawPayload.Data.Length > 7 && rawPayload.Data[1] == 0x27 && rawPayload.Data[3] == 0x0A)
                    {
                        // pf payload
                        var reader = new BinaryReader(new MemoryStream(rawPayload.Data[4..]));
                        var id = GetInteger(reader);
                        link = new PartyFinderPayload(id);
                    }
                    else if (rawPayload.Data.Length > 5 && rawPayload.Data[1] == 0x27 && rawPayload.Data[3] == 0x06)
                    {
                        // achievement payload
                        var reader = new BinaryReader(new MemoryStream(rawPayload.Data[4..]));
                        var id = GetInteger(reader);
                        link = new AchievementPayload(id);
                    }
                    else if (rawPayload.Data is [_, (byte)MacroCode.NonBreakingSpace, _, _])
                    {
                        // NonBreakingSpace payload
                        Append(" ");
                    }
                    // NOTE: no URIPayload because it originates solely from
                    // new Message(). The game doesn't have a URI payload type.
                    else if (Equals(rawPayload, RawPayload.LinkTerminator))
                    {
                        link = null;
                    }
                    break;
                default:
                    if (payload is ITextProvider textProvider)
                    {
                        // We don't want to parse any null string
                        var str = textProvider.Text;
                        var nulIndex = str.IndexOf('\0');
                        if (nulIndex > 0)
                            str = str[..nulIndex];
                        if (string.IsNullOrEmpty(str))
                            break;

                        Append(str);
                    }
                    break;
            }
        }
        if (Plugin.Config.EnableCensorshipHighlight && HighlightWhitelist.Contains(chatType)) {
            Plugin.Log.Info("存在屏蔽词，已高亮");
            return CensorshipHighlighter.HighLightCensorshipWords(chunks);
        }
        return chunks;
    }

    internal static string ToRawString(List<Chunk> chunks)
    {
        if (chunks.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var chunk in chunks)
            if (chunk is TextChunk text)
                builder.Append(text.Content);

        return builder.ToString();
    }

    internal static readonly RawPayload PeriodicRecruitmentLink = new([0x02, 0x27, 0x07, 0x08, 0x01, 0x01, 0x01, 0xFF, 0x01, 0x03]);

    private static uint GetInteger(BinaryReader input)
    {
        var num1 = (uint) input.ReadByte();
        if (num1 < 208U)
            return num1 - 1U;

        var num2 = (uint) ((int) num1 + 1 & 15);
        var numArray = new byte[4];
        for (var index = 3; index >= 0; --index)
            numArray[index] = (num2 & 1 << index) == 0L ? (byte) 0 : input.ReadByte();

        return BitConverter.ToUInt32(numArray, 0);
    }

    public static unsafe void PrintMemoryArea(nint address, int length)
    {
        var ptr = (byte*)address;
        var str = new StringBuilder("\n");
        for(var i = 0; i < length; i++)
        {
            str.Append($"{ptr![i]:X02}");

            if (i == 0)
                continue;

            if ((i+1) % 16 == 0)
                str.Append('\n');
            else if ((i+1) % 4 == 0)
                str.Append(' ');
        }
        Plugin.Log.Information(str.ToString());
    }

    private static readonly HashSet<ChatType?> HighlightWhitelist =
    [
        ChatType.Say,         // 说话
        ChatType.Shout,       // 喊话
        ChatType.Yell,        // 呼喊
        ChatType.Party,       // 小队
        ChatType.Alliance,    // 团队
        ChatType.FreeCompany, // 部队
        ChatType.Linkshell1,  // 通讯贝 [1]
        ChatType.Linkshell2,  // 通讯贝 [2]
        ChatType.Linkshell3,  // 通讯贝 [3]
        ChatType.Linkshell4,  // 通讯贝 [4]
        ChatType.Linkshell5,  // 通讯贝 [5]
        ChatType.Linkshell6,  // 通讯贝 [6]
        ChatType.Linkshell7,  // 通讯贝 [7]
        ChatType.Linkshell8,  // 通讯贝 [8]
        ChatType.CrossParty,  // 跨服小队
        ChatType.CrossLinkshell1, // 跨服通讯贝 [1]
        ChatType.CrossLinkshell2, // 跨服通讯贝 [2]
        ChatType.CrossLinkshell3, // 跨服通讯贝 [3]
        ChatType.CrossLinkshell4, // 跨服通讯贝 [4]
        ChatType.CrossLinkshell5, // 跨服通讯贝 [5]
        ChatType.CrossLinkshell6, // 跨服通讯贝 [6]
        ChatType.CrossLinkshell7, // 跨服通讯贝 [7]
        ChatType.CrossLinkshell8, // 跨服通讯贝 [8]
        ChatType.NoviceNetwork,   // 新人频道
        ChatType.TellIncoming,    // 悄悄话(接收)
        ChatType.TellOutgoing,    // 悄悄话(发出)
        ChatType.PvpTeam,         // 战队
        ChatType.CustomEmote,     // 自定义情感动作
        ChatType.StandardEmote,   // 情感动作
    ];
}