using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
namespace ChatTwo.Util;

public static class CensorshipHighlighter
{
    /// <summary>
    /// 处理 SeString，将检测到被屏蔽的词语高亮显示。
    /// 高亮颜色固定为 FFXIV 内置色板 ID 17。
    /// </summary>
    /// <param name="original">原始消息</param>
    /// <param name="color">颜色参数已忽略，固定使用 ID 17</param>
    public static SeString Process(SeString original)
    {
        if (original.Payloads.Count == 0)
            return original;

        // 固定高亮颜色 ID 为 17
        ushort colorId = 17;

        var newPayloads = new List<Payload>();
        var modified = false;

        foreach (var payload in original.Payloads)
        {
            // 只处理文本 Payload
            if (payload is TextPayload textPayload && !string.IsNullOrEmpty(textPayload.Text))
            {
                var originalText = textPayload.Text;
                var filteredText = CensorshipScanner.GetFilteredString(originalText);

                if (filteredText != null && filteredText != originalText)
                {
                    modified = true;
                    // 构建高亮 Payload 列表
                    var highlightedParts = BuildHighlightedPayloads(originalText, filteredText, colorId);
                    newPayloads.AddRange(highlightedParts);
                }
                else
                {
                    // 无差异，保留原文本 Payload
                    newPayloads.Add(payload);
                }
            }
            else
            {
                // 非文本 Payload (如物品链接、玩家名) 直接保留
                newPayloads.Add(payload);
            }
        }

        return modified ? new SeString(newPayloads) : original;
    }


    /// <summary>
    /// 遍历 Chunks 将屏蔽词高亮后组装成新的 Chunks
    /// </summary>
    /// <param name="chunks"></param>
    /// <returns></returns>
    public static List<Chunk> HighLightCensorshipWords(List<Chunk> chunks)
    {
        var highlight_chunks = new List<Chunk>();
        foreach (var chunk in chunks)
        {
            // 仅处理文本块
            if (chunk is not TextChunk textChunk)
            {
                highlight_chunks.Add(chunk);
                continue;
            }

            var originalText = textChunk.Content;
            var filteredText = CensorshipScanner.GetFilteredString(originalText);

            // 如果文本未被修改，直接添加原块
            if (originalText == filteredText)
            {
                highlight_chunks.Add(chunk);
                continue;
            }

            int startIndex = 0;
            var censoredRanges = GetCensoredRanges(originalText, filteredText);

            foreach (var range in censoredRanges)
            {
                // 添加高亮前的正常文本
                if (range.Start > startIndex)
                {
                    var normalText = originalText.Substring(startIndex, range.Start - startIndex);
                    highlight_chunks.Add(new TextChunk(
                        textChunk.Source, // 保持原来的 Source (应该是 ChunkSource.Content)
                        textChunk.Link,
                        normalText
                    )
                    {
                        FallbackColour = textChunk.FallbackColour,
                        Foreground = textChunk.Foreground,
                        Glow = textChunk.Glow,
                        Italic = textChunk.Italic
                    });
                }

                // 添加高亮的屏蔽词文本
                var censoredText = originalText.Substring(range.Start, range.Length);
                highlight_chunks.Add(new TextChunk(
                    textChunk.Source, // 保持原来的 Source
                    textChunk.Link,
                    censoredText
                )
                {
                    FallbackColour = textChunk.FallbackColour,
                    Foreground = Plugin.Config.CensorshipHighlightColor, // 红色高亮
                    Glow = textChunk.Glow,
                    Italic = textChunk.Italic
                });

                startIndex = range.Start + range.Length;
            }

            // 添加剩余的文本
            if (startIndex < originalText.Length)
            {
                var remainingText = originalText.Substring(startIndex);
                highlight_chunks.Add(new TextChunk(
                    textChunk.Source, // 保持原来的 Source
                    textChunk.Link,
                    remainingText
                )
                {
                    FallbackColour = textChunk.FallbackColour,
                    Foreground = textChunk.Foreground,
                    Glow = textChunk.Glow,
                    Italic = textChunk.Italic
                });
            }
        }

        return highlight_chunks;
    }

    /// <summary>
    /// 逐字符对比，将不同的部分（屏蔽词）用颜色包裹。
    /// 使用标准的 UIForegroundPayload 进行染色。
    /// </summary>
    private static List<Payload> BuildHighlightedPayloads(string original, string filtered, ushort colorId)
    {
        var result = new List<Payload>();

        // 1. 创建开启颜色的 Payload
        var startPayload = new UIForegroundPayload(colorId);

        // 2. 创建关闭颜色的 Payload (重置为默认聊天颜色)
        var endPayload = UIForegroundPayload.UIForegroundOff;

        var i = 0; // original 索引
        var j = 0; // filtered 索引

        while (i < original.Length && j < filtered.Length)
        {
            if (original[i] == filtered[j])
            {
                // 字符相同，普通文本
                result.Add(new TextPayload(original[i].ToString()));
                i++;
                j++;
            }
            else
            {
                // 字符不同！说明 original[i] 是屏蔽词，filtered[j] 是 '*'

                // A. 插入开启颜色
                result.Add(startPayload);

                // B. 收集连续的所有被屏蔽字符
                var start = i;
                while (i < original.Length && j < filtered.Length && original[i] != filtered[j])
                {
                    i++;
                    j++;
                }

                // C. 插入屏蔽词的原始文本 (高亮部分)
                var censoredText = original[start..i];
                result.Add(new TextPayload(censoredText));

                // D. 插入关闭颜色 (恢复默认)
                result.Add(endPayload);
            }
        }

        // 处理剩余部分
        if (i < original.Length)
            result.Add(new TextPayload(original[i..]));

        return result;
    }

    /// <summary>
    /// 通过对比原始文本和过滤后的文本，找出被屏蔽词替换的区间
    /// </summary>
    /// <param name="originalText">原始文本</param>
    /// <param name="filteredText">经过 CensorshipScanner 处理后的文本</param>
    /// <returns>屏蔽词区间列表</returns>
    private static List<CensoredRange> GetCensoredRanges(string originalText, string filteredText)
    {
        var ranges = new List<CensoredRange>();

        // 如果内容相同，直接返回空
        if (originalText == filteredText)
            return ranges;

        int i = 0;
        while (i < originalText.Length)
        {
            // 如果当前位置字符一致，跳过
            if (i < filteredText.Length && originalText[i] == filteredText[i])
            {
                i++;
                continue;
            }

            // 发现差异，记录起始位置
            int start = i;

            // 跳过差异部分，直到字符重新匹配或到达末尾
            // 这里处理过滤文本可能比原始文本短的情况（例如字符被删除）
            while (i < originalText.Length)
            {
                // 检查是否可以重新对齐
                // 条件1: i还没超出过滤文本长度，且字符匹配了
                // 条件2: i已经超出了过滤文本长度（说明后面都被删了）
                bool isAligned = (i < filteredText.Length && originalText[i] == filteredText[i]);
                bool isEndOfFiltered = (i >= filteredText.Length);

                if (isAligned || isEndOfFiltered)
                    break;

                i++;
            }

            int length = i - start;
            if (length > 0)
            {
                ranges.Add(new CensoredRange(start, length));
            }
        }

        return ranges;
    }


}


public readonly struct CensoredRange
{
    public int Start { get; }
    public int Length { get; }

    public CensoredRange(int start, int length)
    {
        Start = start;
        Length = length;
    }
}