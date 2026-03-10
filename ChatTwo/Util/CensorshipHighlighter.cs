using System;
using System.Collections.Generic;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ChatTwo.Util;
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
}